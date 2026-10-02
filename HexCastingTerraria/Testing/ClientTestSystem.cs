using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;

namespace HexCastingTerraria.Testing;

/// <summary>
/// 客户端自动测试（_tools/client_test.ps1 一键跑，说明见 tests/client/README.md）。
///
/// 只有带 <c>-hexclienttest 输出目录</c> 启动的游戏才加载这几个类（IsLoadingEnabled），玩家正常玩时它们根本不存在。
/// 脚本用独立的存档目录和 <c>-skipselect 角色:世界</c> 直接进测试世界，这里按顺序跑 <see cref="ClientTestCases"/> 的各节，
/// 写报告、截图，然后退出游戏。分两段（<c>-hexclientphase</c>）：
///   setup：布置测试场地、检查、把要验证存档的东西存好；
///   verify：重新开游戏读档，检查存下来的东西还在，再挖掉场地上的方块看掉落。
///
/// 测试期间：时间固定在白天正午、不下雨、不刷怪，玩家不掉血、不受键盘鼠标影响（<see cref="ClientTestPlayer"/>），
/// 鼠标位置由测试指定（视线就是鼠标方向），镜头由测试指定。
/// </summary>
public sealed class ClientTestSystem : ModSystem
{
    internal const string Flag = "-hexclienttest";
    internal const string PhaseFlag = "-hexclientphase";
    internal const string PlayerName = "HexTest";

    /// <summary>进世界后先等这么多刻（世界、光照、界面都稳定下来）再开始。</summary>
    private const int WarmupTicks = 90;

    /// <summary>整轮测试的上限（刻）；超时照样写报告、退出，报告里记一条失败。</summary>
    private const int TimeoutTicks = 60 * 60 * 4;

    internal static bool Active => Program.LaunchParameters.ContainsKey(Flag);

    internal static ClientTestRun? Run { get; private set; }

    /// <summary>镜头中心（世界像素）；null = 跟着玩家。</summary>
    internal static Vector2? Camera { get; set; }

    /// <summary>鼠标相对玩家中心的位置（像素）；null = 不管。玩家的视线由鼠标方向决定，测试要它固定。</summary>
    internal static Vector2? MouseOffset { get; set; }

    /// <summary>界面上画物品 / 增益图标总览（截图用）。</summary>
    internal static bool ShowGallery { get; set; }

    private static string? _pendingShot;
    private static int _inWorldTicks;

    /// <summary>
    /// 画布直接读硬件鼠标（PlayerInput.MouseInfo，见 HexClientSystem.RawMouse），网格点画在它周围。
    /// 只在画这一帧时换成测试指定的位置、画完马上还原：要是一直换着，下一帧游戏会以为真鼠标动了，又拿真鼠标去算视线。
    /// </summary>
    private static Point? _drawMouse;
    private static Microsoft.Xna.Framework.Input.MouseState? _realMouse;

    internal static bool ShotPending => _pendingShot != null;

    /// <summary>最近一张截图的像素（测试拿来比较两帧，比如刻图案前后石板那一格变没变）。</summary>
    internal static Color[]? LastShotPixels { get; private set; }

    internal static int LastShotWidth { get; private set; }

    public override bool IsLoadingEnabled(Mod mod) => Active;

    public override void Load()
    {
        if (Main.dedServ) return;

        string outDir = Program.LaunchParameters[Flag];
        string phase = Program.LaunchParameters.TryGetValue(PhaseFlag, out var p) && p.Length > 0 ? p : "setup";
        Directory.CreateDirectory(outDir);
        Run = new ClientTestRun(Mod, outDir, phase, ClientTestCases.Sections(phase));

        // -skipselect 要求存档目录里已经有这个角色：测试目录每次都是新的，加载完成、进世界之前先建一个。
        // ModLoader.OnSuccessfulLoad 是 internal，只能反射；插在最前面，排在 -skipselect 进世界那一步之前
        var field = typeof(Terraria.ModLoader.ModLoader).GetField("OnSuccessfulLoad", BindingFlags.Static | BindingFlags.NonPublic);
        if (field == null)
        {
            Mod.Logger.Error("[HexCasting/客户端测试] 找不到 ModLoader.OnSuccessfulLoad，没法建测试角色");
        }
        else
        {
            var existing = field.GetValue(null) as Action;
            field.SetValue(null, Delegate.Combine((Action)EnsurePlayer, existing));
        }

        Main.OnPreDraw += SwapMouseForDraw;
        Main.OnPostDraw += CaptureShot;
        Mod.Logger.Info($"[HexCasting/客户端测试] 已启用：阶段 {phase}，输出 {outDir}");
    }

    public override void Unload()
    {
        Main.OnPreDraw -= SwapMouseForDraw;
        Main.OnPostDraw -= CaptureShot;
        Run = null;
        Camera = null;
        MouseOffset = null;
        ShowGallery = false;
        _pendingShot = null;
        _inWorldTicks = 0;
        _drawMouse = null;
        _realMouse = null;
        LastShotPixels = null;
    }

    private static void EnsurePlayer()
    {
        Main.LoadPlayers();
        if (Main.PlayerList.Exists(f => f.Name == PlayerName)) return;
        var player = new Player { name = PlayerName, difficulty = PlayerDifficultyID.SoftCore };
        PlayerFileData.CreateAndSave(player);
    }

    public override void PostUpdateInput()
    {
        if (Run == null || Main.gameMenu) return;
        if (MouseOffset is { } m)
        {
            // PlayerInput.MouseX / MouseY 是鼠标的原始位置：硬件鼠标不动就不会重读，之后几次 SetZoom 都从它算 Main.mouseX。
            // 只改 Main.mouseX 会被同一帧稍后的 SetZoom 改回去（第一次跑时视线就是这样跟着真鼠标走了）
            var at = Main.LocalPlayer.Center + m;
            int x = (int)(at.X - Main.screenPosition.X);
            int y = (int)(at.Y - Main.screenPosition.Y);
            Terraria.GameInput.PlayerInput.MouseX = x;
            Terraria.GameInput.PlayerInput.MouseY = y;
            Main.mouseX = x;
            Main.mouseY = y;
            _drawMouse = new Point(x, y);
        }
        else
        {
            _drawMouse = null;
        }
        Main.mouseLeft = Main.mouseRight = false;
        Main.mouseLeftRelease = Main.mouseRightRelease = true;
    }

    public override void PostUpdateEverything()
    {
        if (Run == null || Main.gameMenu || Run.Finished) return;

        // 世界保持安静：白天正午、不下雨
        Main.dayTime = true;
        Main.time = 27000;
        Main.raining = false;
        Main.rainTime = 0;
        Main.maxRaining = 0f;

        _inWorldTicks++;
        if (_inWorldTicks < WarmupTicks) return;
        if (_inWorldTicks > WarmupTicks + TimeoutTicks)
        {
            Run.Check("整轮测试在时限内跑完", false, $"超过 {TimeoutTicks} 刻，停在「{Run.Section}」");
            Run.Finish();
            return;
        }
        Run.Tick();
    }

    public override void ModifyScreenPosition()
    {
        if (Run == null || Camera is not { } c) return;
        Main.screenPosition = c - new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
    }

    /// <summary>截下一帧：存成 PNG，记进报告。</summary>
    internal static void RequestShot(string name)
    {
        if (Run == null) return;
        string path = Path.Combine(Run.OutDir, $"{Run.Phase}-{name}.png");
        _pendingShot = path;
        Run.AddShot(path);
    }

    private static void SwapMouseForDraw(GameTime _)
    {
        if (_drawMouse is not { } m || Main.gameMenu) return;
        var scale = Terraria.GameInput.PlayerInput.RawMouseScale;
        _realMouse = Terraria.GameInput.PlayerInput.MouseInfo;
        var released = Microsoft.Xna.Framework.Input.ButtonState.Released;
        Terraria.GameInput.PlayerInput.MouseInfo = new Microsoft.Xna.Framework.Input.MouseState(
            (int)(m.X / (scale.X > 0 ? scale.X : 1f)), (int)(m.Y / (scale.Y > 0 ? scale.Y : 1f)), 0,
            released, released, released, released, released);
    }

    private static void CaptureShot(GameTime _)
    {
        if (_realMouse is { } real)
        {
            Terraria.GameInput.PlayerInput.MouseInfo = real;
            _realMouse = null;
        }
        if (_pendingShot is not { } path) return;
        _pendingShot = null;
        try
        {
            var gd = Main.instance.GraphicsDevice;
            int w = gd.PresentationParameters.BackBufferWidth;
            int h = gd.PresentationParameters.BackBufferHeight;
            var data = new Color[w * h];
            gd.GetBackBufferData(data);
            for (int i = 0; i < data.Length; i++) data[i].A = 255;
            LastShotPixels = data;
            LastShotWidth = w;
            using var tex = new Texture2D(gd, w, h);
            tex.SetData(data);
            using var fs = File.Create(path);
            tex.SaveAsPng(fs, w, h);
        }
        catch (Exception e)
        {
            Run?.Check("截图 " + Path.GetFileName(path), false, e.Message);
        }
    }

    /// <summary>物品 / 增益图标总览：按类型号排成网格，每格 40 像素，左上角是第一个。顺序记在报告里。</summary>
    public override void PostDrawInterface(SpriteBatch spriteBatch)
    {
        if (!ShowGallery || Run == null) return;

        var pixel = TextureAssets.MagicPixel.Value;
        spriteBatch.Draw(pixel, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), new Color(24, 20, 36));

        const int cell = 40;
        int perRow = Math.Max(1, (Main.screenWidth - 16) / cell);
        int index = 0;
        foreach (var (texture, frame) in GalleryIcons())
        {
            int cx = 8 + (index % perRow) * cell;
            int cy = 8 + (index / perRow) * cell;
            spriteBatch.Draw(pixel, new Rectangle(cx, cy, cell - 2, cell - 2), new Color(48, 42, 66));
            float scale = Math.Min(1f, (cell - 6f) / Math.Max(frame.Width, frame.Height));
            var pos = new Vector2(cx + (cell - 2) / 2f, cy + (cell - 2) / 2f);
            spriteBatch.Draw(texture, pos, frame, Color.White, 0f, frame.Size() / 2f, scale, SpriteEffects.None, 0f);
            index++;
        }
    }

    /// <summary>总览里的图标：先是本模组全部物品（按类型号），再是全部增益。</summary>
    internal static IEnumerable<(Texture2D Texture, Rectangle Frame)> GalleryIcons()
    {
        foreach (int type in ClientTestCases.ModItemTypes())
        {
            Main.instance.LoadItem(type);
            var tex = TextureAssets.Item[type].Value;
            var frame = Main.itemAnimations[type] is { } anim ? anim.GetFrame(tex) : tex.Frame();
            yield return (tex, frame);
        }
        foreach (int type in ClientTestCases.ModBuffTypes())
        {
            var tex = TextureAssets.Buff[type].Value;
            yield return (tex, tex.Frame());
        }
    }
}

/// <summary>测试期间玩家不受键盘鼠标影响、不掉血、不窒息。</summary>
public sealed class ClientTestPlayer : ModPlayer
{
    public override bool IsLoadingEnabled(Mod mod) => ClientTestSystem.Active;

    public override void SetControls()
    {
        if (ClientTestSystem.Run == null) return;
        Player.controlLeft = Player.controlRight = Player.controlUp = Player.controlDown = false;
        Player.controlJump = Player.controlUseItem = Player.controlUseTile = Player.controlThrow = false;
        Player.controlInv = Player.controlHook = Player.controlMount = Player.controlSmart = false;
        Player.controlQuickHeal = Player.controlQuickMana = false;
        Player.releaseUseItem = true;
    }

    public override void PostUpdate()
    {
        if (ClientTestSystem.Run == null) return;
        Player.statLife = Player.statLifeMax2;
        Player.breath = Player.breathMax;
        Player.immune = true;
        Player.immuneTime = Math.Max(Player.immuneTime, 2);
    }
}

/// <summary>测试期间不刷怪。</summary>
public sealed class ClientTestCalm : GlobalNPC
{
    public override bool IsLoadingEnabled(Mod mod) => ClientTestSystem.Active;

    public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns)
    {
        maxSpawns = 0;
    }
}

/// <summary>
/// 一轮测试：按顺序跑各节（每节是一个迭代器，<c>yield return n</c> 表示等 n 刻再接着跑），记检查结果，最后写报告、退出游戏。
/// 一节里抛了异常就记一条失败、跳到下一节 —— 一处坏了不会让后面的检查全部看不到。
/// </summary>
internal sealed class ClientTestRun
{
    internal sealed record Result(string Section, string Name, bool Ok, string Detail);

    private readonly Mod _mod;
    private readonly Queue<(string Name, Func<ClientTestRun, IEnumerable<int>> Body)> _sections;
    private readonly List<Result> _results = new();
    private readonly List<string> _shots = new();
    private readonly Dictionary<string, object> _info = new();
    private IEnumerator<int>? _current;
    private int _wait;

    public Mod Mod => _mod;
    public string OutDir { get; }
    public string Phase { get; }
    public string Section { get; private set; } = "";
    public bool Finished { get; private set; }

    public ClientTestRun(Mod mod, string outDir, string phase, IEnumerable<(string, Func<ClientTestRun, IEnumerable<int>>)> sections)
    {
        _mod = mod;
        OutDir = outDir;
        Phase = phase;
        _sections = new Queue<(string, Func<ClientTestRun, IEnumerable<int>>)>(sections);
    }

    public bool Check(string name, bool ok, string detail = "")
    {
        _results.Add(new Result(Section, name, ok, detail));
        string line = $"[HexCasting/客户端测试] {(ok ? "通过" : "失败")} {Section} / {name}" + (detail.Length > 0 ? "：" + detail : "");
        if (ok) _mod.Logger.Info(line);
        else _mod.Logger.Warn(line);
        return ok;
    }

    /// <summary>报告里附带的信息（不是检查项）。</summary>
    public void Info(string key, object value) => _info[key] = value;

    public void AddShot(string path) => _shots.Add(path);

    public void Tick()
    {
        if (Finished) return;
        if (_wait > 0)
        {
            _wait--;
            return;
        }

        while (true)
        {
            if (_current == null)
            {
                if (_sections.Count == 0)
                {
                    Finish();
                    return;
                }
                var (name, body) = _sections.Dequeue();
                Section = name;
                _mod.Logger.Info($"[HexCasting/客户端测试] 开始：{name}");
                try
                {
                    _current = body(this).GetEnumerator();
                }
                catch (Exception e)
                {
                    Check("这一节跑完没有异常", false, e.ToString());
                    continue;
                }
            }

            bool more;
            try
            {
                more = _current.MoveNext();
            }
            catch (Exception e)
            {
                Check("这一节跑完没有异常", false, e.ToString());
                more = false;
            }

            if (!more)
            {
                _current = null;
                continue;
            }

            _wait = Math.Max(0, _current.Current);
            return;
        }
    }

    public void Finish()
    {
        if (Finished) return;
        Finished = true;
        Section = "";
        ClientTestSystem.Camera = null;
        ClientTestSystem.ShowGallery = false;

        int failed = _results.FindAll(r => !r.Ok).Count;
        var report = new Dictionary<string, object>
        {
            ["phase"] = Phase,
            ["finished"] = true,
            ["total"] = _results.Count,
            ["failed"] = failed,
            ["results"] = _results,
            ["shots"] = _shots,
            ["info"] = _info,
        };
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(Path.Combine(OutDir, $"report-{Phase}.json"), JsonSerializer.Serialize(report, options));
        _mod.Logger.Info($"[HexCasting/客户端测试] 阶段 {Phase} 结束：共 {_results.Count} 项，失败 {failed} 项");

        Main.instance.Exit();
    }
}
