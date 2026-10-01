using System;
using System.Collections.Generic;
using HexCastingTerraria.Config;
using HexCastingTerraria.Content;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Dev;
using HexCastingTerraria.Core.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 开发者面板（默认 F7）：不知道该放什么法术时用。
///
///  - 左边「法术示例」（<see cref="SampleHexes"/>，每条都在 vmtest 里真跑过）；
///    右边是选中那条的每一步图案。可以「点世界施放」（不用画），也可以「在画布上临摹」（逐步教画）。
///  - 下面是调试开关（无限媒质、始终启蒙、过载不扣血……）和一键操作（发测试包、补满媒质、获得启蒙、重置进度）。
///  - 最底下一行是进度：启蒙 / 盲目绘制 / 睁开双眼 / 紫水晶 / Boss 里程碑 / 书解锁了几条。
///
/// 坐标全部在界面缩放空间（与书相同）：原始鼠标 ÷ Main.UIScale。
/// 命中区用上一帧画出来的那一份，绘制与点击是同一套几何。
/// </summary>
public sealed class DevPanel
{
    private const float W = 880f;
    private const float H = 610f;

    private int _selected;
    private readonly List<(Rectangle Rect, Action Act, string? Tip)> _hits = new();
    private List<(Rectangle Rect, Action Act, string? Tip)> _lastHits = new();
    private Rectangle _lastPanel;

    public bool IsOpen { get; private set; }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        SoundEngine.PlaySound(IsOpen ? SoundID.MenuOpen : SoundID.MenuClose);
        if (!IsOpen) { _lastHits.Clear(); }
    }

    public void Close()
    {
        if (IsOpen) { Toggle(); }
    }

    private static Vector2 UiMouse() => HexClientSystem.RawMouse() / Main.UIScale;

    // ── 点世界施放 ───────────────────────────────────────────────────

    /// <summary>「点世界施放」待命中的示例：下一次在世界里左键就施放（右键取消）。</summary>
    public static SampleHex? Armed { get; private set; }

    private static bool _swallowLeft;

    // ── 画布临摹 ─────────────────────────────────────────────────────

    /// <summary>正在临摹的示例（null = 没有）与当前第几步。</summary>
    public static SampleHex? Tracing { get; private set; }

    public static int TraceStep { get; private set; }

    /// <summary>每帧输入。返回 true = 这一帧的鼠标被面板 / 待命施放吃掉了。</summary>
    public bool HandleInput(bool leftClick, bool rightClick)
    {
        // 按住不放的那一下左键：施放之后一直吃到松开，免得接着挥法杖
        if (_swallowLeft)
        {
            if (!Main.mouseLeft) { _swallowLeft = false; }
            else { Main.LocalPlayer.mouseInterface = true; Main.mouseLeft = false; Main.mouseLeftRelease = false; return true; }
        }

        var mouse = UiMouse();
        if (IsOpen)
        {
            if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape)
                && Main.oldKeyState.IsKeyUp(Microsoft.Xna.Framework.Input.Keys.Escape))
            {
                Close();
                HexCanvasState.ConsumeEsc();   // 不然同一下 Esc 会让泰拉打开物品栏
                return true;
            }
            if (_lastPanel.Contains((int)mouse.X, (int)mouse.Y))
            {
                Main.LocalPlayer.mouseInterface = true;
                if (leftClick)
                {
                    foreach (var (rect, act, _) in _lastHits)
                    {
                        if (rect.Contains((int)mouse.X, (int)mouse.Y))
                        {
                            SoundEngine.PlaySound(SoundID.MenuTick);
                            act();
                            break;
                        }
                    }
                }
                if (leftClick || rightClick)
                {
                    Main.mouseLeft = false;
                    Main.mouseLeftRelease = false;
                    Main.mouseRight = false;
                    Main.mouseRightRelease = false;
                    return true;
                }
            }
            return false;
        }

        if (Armed is { } armed && !Main.LocalPlayer.mouseInterface)
        {
            if (rightClick)
            {
                Armed = null;
                HexCanvasState.SetMessage("已取消施放");
                Main.mouseRight = false;
                Main.mouseRightRelease = false;
                return true;
            }
            if (leftClick)
            {
                Armed = null;
                Main.LocalPlayer.mouseInterface = true;   // 泰拉：mouseInterface → delayUseItem，这一下不会挥手里的东西
                CastNow(armed);
                _swallowLeft = true;
                Main.mouseLeft = false;
                Main.mouseLeftRelease = false;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 一口气把示例的每一步送进 VM（与在画布上一条条画完全相同的路径）。
    /// 先清空栈：栈上残留的东西会让示例的参数全部错位。
    /// </summary>
    private static void CastNow(SampleHex sample)
    {
        HexVmState.Reset();
        HexCanvasState.Canvas.Reset();
        var player = Main.LocalPlayer;
        for (int i = 0; i < sample.Steps.Length; i++)
        {
            var step = sample.Steps[i];
            var pattern = step.ToPattern();
            if (pattern is null)
            {
                HexCanvasState.SetMessage($"「{sample.Name}」第 {i + 1} 步画不出来：{step.Label}");
                return;
            }
            HexVmState.EvaluatePattern(player, pattern);
            // 联机客户端的结果要等服务端回包，没法逐步检查；单人时出错立刻停，并说清是哪一步
            if (Main.netMode != NetmodeID.MultiplayerClient && !HexVmState.LastResolution.IsSuccess())
            {
                HexCanvasState.SetMessage($"「{sample.Name}」第 {i + 1} 步「{step.Label}」失败：{HexVmState.LastError ?? HexVmState.LastResolution.ToString()}", 300);
                return;
            }
        }
        HexCanvasState.SetMessage($"已施放「{sample.Name}」");
    }

    private void StartTracing(SampleHex sample)
    {
        Tracing = sample;
        TraceStep = 0;
        HexVmState.Reset();
        HexCanvasState.Canvas.Reset();
        Close();
        Content.Items.HexStaff.OpenCanvas();
        HexCanvasState.SetMessage($"临摹「{sample.Name}」：照屏幕上方的图案一步步画", 240);
    }

    /// <summary>画布上刚画完一条（已经送进 VM）。临摹中就对一下是不是这一步。</summary>
    public static void OnPatternDrawn(HexPattern drawn)
    {
        if (Tracing is not { } sample) { return; }
        var step = sample.Steps[TraceStep];
        if (!step.Matches(drawn))
        {
            // 画错的那条已经进栈了，后面的参数全会错位 —— 直接清空，从头来最清楚
            int wrongAt = TraceStep + 1;
            HexVmState.Reset();
            HexCanvasState.Canvas.Reset();
            TraceStep = 0;
            HexCanvasState.SetMessage($"第 {wrongAt} 步应该画「{step.Label}」。已清空，从第 1 步重来", 300);
            return;
        }
        TraceStep++;
        if (TraceStep >= sample.Steps.Length)
        {
            Tracing = null;
            TraceStep = 0;
            HexCanvasState.SetMessage($"临摹完成：「{sample.Name}」", 240);
        }
    }

    public static void StopTracing() => Tracing = null;

    // ── 绘制 ─────────────────────────────────────────────────────────

    private static readonly Color Title = new(255, 230, 140);
    private static readonly Color Text = new(232, 224, 246);
    private static readonly Color Dim = new(170, 165, 190);
    private static readonly Color Good = new(120, 230, 160);
    private static readonly Color Bad = new(230, 110, 110);
    private static readonly Color Great = new(255, 200, 90);
    private static readonly Color PatternColor = new(120, 200, 255);

    /// <summary>在界面层（UI 缩放）里画面板与临摹引导。</summary>
    public void Draw(SpriteBatch sb)
    {
        float vw = Terraria.GameInput.PlayerInput.RealScreenWidth / Main.UIScale;
        float vh = Terraria.GameInput.PlayerInput.RealScreenHeight / Main.UIScale;

        if (HexCanvasState.Canvas.IsOpen && Tracing is not null)
        {
            DrawTraceGuide(sb, vw);
        }
        if (Armed is { } armed && !IsOpen)
        {
            var m = UiMouse();
            Terraria.Utils.DrawBorderString(sb, $"左键施放「{armed.Name}」 · 右键取消", m + new Vector2(18, 18), Great, 0.8f);
        }
        if (!IsOpen) { return; }

        _hits.Clear();
        float x = MathF.Max(8, (vw - W) / 2f);
        float y = MathF.Max(8, (vh - H) / 2f);
        var panel = new Rectangle((int)x, (int)y, (int)W, (int)H);
        _lastPanel = panel;
        Terraria.Utils.DrawInvBG(sb, panel, new Color(24, 20, 44) * 0.94f);
        var mouse = UiMouse();
        if (panel.Contains((int)mouse.X, (int)mouse.Y)) { Main.LocalPlayer.mouseInterface = true; }

        Terraria.Utils.DrawBorderString(sb, "咒法学 · 开发者面板", new Vector2(x + 16, y + 12), Title, 0.95f);
        Terraria.Utils.DrawBorderString(sb, "F7 / Esc 关闭", new Vector2(x + W - 130, y + 14), Dim, 0.7f);

        DrawSampleList(sb, x + 14, y + 44, mouse);
        DrawSampleDetail(sb, x + 262, y + 44, W - 262 - 14, mouse);
        DrawDevControls(sb, x + 14, y + 452, mouse);

        _lastHits = new List<(Rectangle, Action, string?)>(_hits);
        foreach (var (rect, _, tip) in _lastHits)
        {
            if (tip is not null && rect.Contains((int)mouse.X, (int)mouse.Y)) { Main.hoverItemName = tip; }
        }
    }

    private void DrawSampleList(SpriteBatch sb, float x, float y, Vector2 mouse)
    {
        Terraria.Utils.DrawBorderString(sb, "法术示例（[大] = 大法术，要启蒙）", new Vector2(x, y), Text, 0.75f);
        y += 24;
        var all = SampleHexes.All;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            var r = new Rectangle((int)x, (int)y + i * 23, 236, 22);
            bool hover = r.Contains((int)mouse.X, (int)mouse.Y);
            if (i == _selected || hover)
            {
                Terraria.Utils.DrawInvBG(sb, r, (i == _selected ? new Color(90, 70, 150) : new Color(60, 50, 100)) * 0.9f);
            }
            string label = (s.Great ? "[大] " : "     ") + s.Name;
            Terraria.Utils.DrawBorderString(sb, label, new Vector2(r.X + 8, r.Y + 3), s.Great ? Great : Text, 0.75f);
            int index = i;
            _hits.Add((r, () => _selected = index, s.Description));
        }
    }

    private void DrawSampleDetail(SpriteBatch sb, float x, float y, float width, Vector2 mouse)
    {
        var all = SampleHexes.All;
        _selected = Math.Clamp(_selected, 0, all.Count - 1);
        var s = all[_selected];

        Terraria.Utils.DrawBorderString(sb, s.Name + (s.Great ? "  [大法术]" : ""), new Vector2(x, y), s.Great ? Great : Title, 0.9f);
        Terraria.Utils.DrawBorderString(sb, s.Description, new Vector2(x, y + 26), Text, 0.75f);
        Terraria.Utils.DrawBorderString(sb, $"共 {s.Steps.Length} 步，按顺序画（每画一条就求值一次）：", new Vector2(x, y + 48), Dim, 0.7f);

        // 每一步一个格子：序号、图案预览、名字
        const float cellW = 99f, cellH = 92f;
        int perRow = Math.Max(1, (int)(width / cellW));
        for (int i = 0; i < s.Steps.Length; i++)
        {
            var step = s.Steps[i];
            float cx = x + (i % perRow) * cellW;
            float cy = y + 72 + (i / perRow) * (cellH + 4);
            var cell = new Rectangle((int)cx, (int)cy, (int)cellW - 5, (int)cellH);
            Terraria.Utils.DrawInvBG(sb, cell, new Color(40, 34, 70) * 0.9f);
            Terraria.Utils.DrawBorderString(sb, (i + 1).ToString(), new Vector2(cx + 5, cy + 3), Dim, 0.6f);
            if (step.ToPattern() is { } pattern)
            {
                PatternArt.DrawReadable(pattern, new Vector2(cx + (cellW - 5) / 2f, cy + 36), 26f * 1.6f);
            }
            string name = step.Label;
            float scale = 0.6f;
            while (scale > 0.42f && FontAssets.MouseText.Value.MeasureString(name).X * scale > cellW - 12) { scale -= 0.03f; }
            float tw = FontAssets.MouseText.Value.MeasureString(name).X * scale;
            Terraria.Utils.DrawBorderString(sb, name, new Vector2(cx + (cellW - 5 - tw) / 2f, cy + cellH - 22), step.IsNumber ? Great : Text, scale);
            string? tip = step.IsNumber
                ? $"数字 {step.Number}：aqaa 开头，之后 w=+1 q=+5 e=+10 a=×2 d=÷2（按顺序施加）"
                : step.PatternId;
            _hits.Add((cell, () => { }, tip));
        }

        float by = y + 72 + 2 * (cellH + 4) + 8;
        Button(sb, new Rectangle((int)x, (int)by, 190, 30), "点世界施放", mouse, () =>
        {
            Armed = s;
            Close();
            HexCanvasState.SetMessage($"左键点世界里的目标施放「{s.Name}」，右键取消", 300);
        }, "不用画：关掉面板后在世界里左键点一下，就朝那个方向施放（和画完每一步完全相同）");
        Button(sb, new Rectangle((int)x + 200, (int)by, 210, 30), "在画布上逐步临摹", mouse, () => StartTracing(s),
            "打开画布，屏幕上方提示每一步该画什么；画对自动进下一步，画错清空重来");

        if (s.Great && !HexBook.CurrentProgress().Enlightened)
        {
            Terraria.Utils.DrawBorderString(sb, "还没启蒙：施放大法术会失败并丢下手持物品和快捷栏中它右边一格的物品（这也是解锁「过载」的办法）",
                new Vector2(x, by + 38), Bad, 0.66f);
        }
    }

    private void DrawDevControls(SpriteBatch sb, float x, float y, Vector2 mouse)
    {
        var cfg = HexClientConfig.Instance;
        var player = Main.LocalPlayer;
        var hp = HexPlayer.Get(player);

        Terraria.Utils.DrawBorderString(sb, "调试开关（本次游戏有效；要永久保存请到 设置 → 模组配置）", new Vector2(x, y), Text, 0.72f);
        y += 24;
        bool infinite = hp.InfiniteMedia || cfg.InfiniteMedia;
        var toggles = new (string Label, bool On, Action Flip, string Tip)[]
        {
            ("无限媒质", infinite, () => { if (infinite) { hp.InfiniteMedia = false; cfg.InfiniteMedia = false; } else { hp.InfiniteMedia = true; } },
                "施法不消耗媒质、不过载（快捷键 J）"),
            ("始终启蒙", cfg.AlwaysEnlightened, () => cfg.AlwaysEnlightened = !cfg.AlwaysEnlightened, "大法术直接能用"),
            ("过载不扣血", cfg.NoOvercastDamage, () => cfg.NoOvercastDamage = !cfg.NoOvercastDamage, "媒质不够时照常施放、不掉血"),
            ("施法后补满", cfg.RefillMediaAfterCast, () => cfg.RefillMediaAfterCast = !cfg.RefillMediaAfterCast, "每次求值后把媒质补满"),
            ("书全部解锁", cfg.UnlockWholeBook, () => cfg.UnlockWholeBook = !cfg.UnlockWholeBook, "咒法学之书不按进度锁条目"),
        };
        for (int i = 0; i < toggles.Length; i++)
        {
            var t = toggles[i];
            var r = new Rectangle((int)x + (i % 6) * 142, (int)y, 136, 28);
            Button(sb, r, (t.On ? "■ " : "□ ") + t.Label, mouse, t.Flip, t.Tip, t.On ? new Color(60, 120, 90) : (Color?)null);
        }
        y += 38;

        var actions = new (string Label, Action Act, string Tip)[]
        {
            ("发测试包", () => { Content.Items.DevKit.Give(player); HexCanvasState.SetMessage("已发放开发者测试包"); }, "每个子系统的代表物品各一份 + 补满媒质（快捷键 K）"),
            ("补满媒质瓶", () => Content.PlayerEffects.RefillFlasks(player), "背包里的媒质瓶全部补满（没有瓶子就先发测试包）"),
            ("获得启蒙", () => { hp.GrantEnlightenment(); hp.FailedGreatSpell = true; hp.Overcasted = true; }, "等于走完一次「过载到只剩半颗心」：启蒙 + 盲目绘制 + 睁开双眼"),
            ("重置进度", () =>
            {
                hp.Enlightened = false; hp.FailedGreatSpell = false; hp.Overcasted = false; hp.ObtainedAmethyst = false;
                hp.FoundLore.Clear();
                HexCanvasState.SetMessage("已重置咒法学进度");
            }, "清掉本角色的咒法学进度标记（背包里还有紫水晶的话马上又会记上）"),
            ("清空栈", () => { HexVmState.Reset(); HexCanvasState.Canvas.Reset(); StopTracing(); }, "清空 VM 栈与画布（等于潜行 + 右键法杖）"),
            ("发远古卷轴", () =>
            {
                foreach (var id in Core.Registry.PatternRegistry.PerWorldIds)
                {
                    var item = new Item(Terraria.ModLoader.ModContent.ItemType<Content.Items.AncientScroll>());
                    (item.ModItem as Content.Items.AncientScroll)!.SetOp(id);
                    player.QuickSpawnItem(player.GetSource_Misc("HexDevScrolls"), item);
                }
                HexCanvasState.SetMessage("已发放 14 张远古卷轴");
            }, "大法术的笔顺每个世界不同：一次发齐本世界全部 14 张远古卷轴（正常只能在箱子里找）"),
        };
        for (int i = 0; i < actions.Length; i++)
        {
            var a = actions[i];
            Button(sb, new Rectangle((int)x + i * 142, (int)y, 136, 28), a.Label, mouse, a.Act, a.Tip);
        }
        y += 40;

        var p = HexBook.CurrentProgress();
        var (unlocked, total) = HexBook.UnlockStats();
        string Mark(bool b) => b ? "是" : "否";
        Terraria.Utils.DrawBorderString(sb,
            $"背包媒质 {hp.InventoryMedia() / (double)Core.Media.MediaConstants.DustUnit:0.#} 粉   "
            + $"紫水晶 {Mark(p.Amethyst)}   盲目绘制 {Mark(p.FailedGreatSpell)}   睁开双眼 {Mark(p.Overcasted)}   启蒙 {Mark(p.Enlightened)}   "
            + $"书 {unlocked}/{total} 条",
            new Vector2(x, y), Text, 0.72f);
        y += 20;
        var sbm = new System.Text.StringBuilder($"已读传说 {p.FoundLore.Count}/{BookUnlocks.LoreIds.Length} 篇（读「故事残卷」随机解锁一篇）   本世界大法术笔顺：");
        sbm.Append(Core.Registry.PatternRegistry.PerWorldTable.Count).Append(" 条（书里只画形状，笔顺看远古卷轴）");
        Terraria.Utils.DrawBorderString(sb, sbm.ToString(), new Vector2(x, y), Dim, 0.62f);
    }

    private void Button(SpriteBatch sb, Rectangle r, string label, Vector2 mouse, Action act, string? tip, Color? tint = null)
    {
        bool hover = r.Contains((int)mouse.X, (int)mouse.Y);
        var bg = tint ?? new Color(63, 65, 151);
        Terraria.Utils.DrawInvBG(sb, r, (hover ? Color.Lerp(bg, Color.White, 0.25f) : bg) * 0.95f);
        float tw = FontAssets.MouseText.Value.MeasureString(label).X * 0.75f;
        Terraria.Utils.DrawBorderString(sb, label, new Vector2(r.X + (r.Width - tw) / 2f, r.Y + 6), Color.White, 0.75f);
        _hits.Add((r, act, tip));
    }

    /// <summary>画布打开且在临摹时：屏幕上方显示这一步（大）和下一步（小）。</summary>
    private static void DrawTraceGuide(SpriteBatch sb, float vw)
    {
        var s = Tracing!;
        int i = Math.Clamp(TraceStep, 0, s.Steps.Length - 1);
        var step = s.Steps[i];
        float cx = vw / 2f;
        var box = new Rectangle((int)(cx - 210), 70, 420, 150);
        Terraria.Utils.DrawInvBG(sb, box, new Color(24, 20, 44) * 0.88f);
        Terraria.Utils.DrawBorderString(sb, $"临摹「{s.Name}」  第 {i + 1}/{s.Steps.Length} 步", new Vector2(box.X + 12, box.Y + 8), Title, 0.8f);
        if (step.ToPattern() is { } pattern)
        {
            PatternArt.DrawReadable(pattern, new Vector2(box.X + 80, box.Y + 88), 46f * 1.6f);
        }
        Terraria.Utils.DrawBorderString(sb, step.Label, new Vector2(box.X + 160, box.Y + 44), step.IsNumber ? Great : Text, 0.9f);
        Terraria.Utils.DrawBorderString(sb, step.IsNumber ? "数字：画出这个数的任何写法都算" : "起笔方向随意，形状对就行",
            new Vector2(box.X + 160, box.Y + 70), Dim, 0.66f);
        if (i + 1 < s.Steps.Length)
        {
            Terraria.Utils.DrawBorderString(sb, "下一步：" + s.Steps[i + 1].Label, new Vector2(box.X + 160, box.Y + 96), Dim, 0.66f);
        }
        Terraria.Utils.DrawBorderString(sb, "白点 = 起笔处 · 画错会清空重来", new Vector2(box.X + 160, box.Y + 120), Dim, 0.6f);
    }
}
