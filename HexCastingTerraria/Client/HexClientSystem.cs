using System;
using HexCastingTerraria.Client.UI;
using HexCastingTerraria.Content;
using HexCastingTerraria.Core;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using HexCastingTerraria.Config;

namespace HexCastingTerraria.Client;

/// <summary>
/// 客户端系统：画布输入、画布绘制、HUD（媒质指示 + 图案识别反馈）。
///
/// 对应源项目 GuiSpellcasting：
///   - 输入：mouseClicked → DrawStart / mouseDragged|mouseMoved → DrawMove / mouseReleased → DrawEnd
///     注意原作在「点击切换模式」下也靠 mouseMoved 驱动，所以这里每帧都调 DrawMove，
///     而不是只在按住左键时调。
///   - 绘制：全屏透明画布，格点 + 图案线
/// </summary>
public sealed class HexClientSystem : ModSystem
{
    /// <summary>咒法学主题紫（取自原作法杖顶端紫水晶）。</summary>
    public static readonly Color MediaColor = new Color(178, 126, 220);
    public static readonly Color MediaColorDim = new Color(96, 62, 130);
    public static readonly Color TextColor = new Color(232, 224, 246);
    public static readonly Color GoodColor = new Color(120, 230, 255);
    public static readonly Color BadColor = new Color(230, 96, 96);
    public static readonly Color DebugColor = new Color(255, 230, 140);

    /// <summary>调试信息面板开关。定位问题时打开。</summary>
    public static bool ShowDebug { get; set; } = true;

    private Texture2D? _pixel;

    /// <summary>
    /// 1x1 白点贴图。必须延迟创建：
    /// FNA3D 要求图形 API 只在主线程调用，而模组 Load() 不在主线程，
    /// 在那里 new Texture2D 会抛 ThreadStateException 并导致模组被禁用。
    /// </summary>
    private Texture2D Pixel
    {
        get
        {
            if (_pixel == null || _pixel.IsDisposed)
            {
                _pixel = new Texture2D(Main.graphics.GraphicsDevice, 1, 1);
                _pixel.SetData(new[] { Color.White });
            }
            return _pixel;
        }
    }

    private bool _prevMouseLeft;
    private bool _prevMouseRight;

    /// <summary>上一帧画布鼠标位置（屏幕像素），用于沿路径采样。</summary>
    private Vector2? _prevCanvasMouse;

    /// <summary>
    /// 屏幕像素下的鼠标位置。不用 Main.mouseX：它在不同阶段会被换算到界面缩放 / 世界缩放空间，
    /// 输入与绘制拿到的可能不是同一套坐标，界面缩放 ≠ 100% 时笔迹就偏离光标。
    /// </summary>
    internal static Vector2 RawMouse()
    {
        var m = Terraria.GameInput.PlayerInput.MouseInfo;
        var s = Terraria.GameInput.PlayerInput.RawMouseScale;
        return new Vector2(m.X * s.X, m.Y * s.Y);
    }

    /// <summary>按键绑定表是否已就绪（见 PostUpdateInput 里的就绪门闩说明）。</summary>
    private bool _keybindReady;

    public override void Load() { }

    public override void Unload()
    {
        _pixel = null;
    }

    public override void PostUpdateInput()
    {
        if (Main.dedServ)
        {
            return;
        }

        var canvas = HexCanvasState.Canvas;
        HexCanvasState.TickMessage();
        HexCanvasState.TickGuard();

        // 开发者面板（F7）与「点世界施放」：画布和书都没开的时候才接鼠标
        if (!canvas.IsOpen && !HexCanvasState.Book.IsOpen)
        {
            HexCanvasState.Dev.HandleInput(
                leftClick: Main.mouseLeft && Main.mouseLeftRelease,
                rightClick: Main.mouseRight && Main.mouseRightRelease);
        }

        // 咒法学之书：输入处理放在最前面 —— 它开着的时候要**先**把
        // 鼠标与滚轮吃掉，否则滚动书页会同时切换快捷栏、点选图案会同时挖方块。
        // 「刚按下」= 这一帧按着、上一帧松开（泰拉的惯用判断）
        HexCanvasState.Book.HandleInput(
            leftClick: Main.mouseLeft && Main.mouseLeftRelease,
            rightClick: Main.mouseRight && Main.mouseRightRelease);

        // ⚠️ 这里必须**重新读一次**开关状态：上一句的 HandleInput 可能刚刚把书关掉
        //（右键 / Esc）。在关闭的那一帧继续压输入，会和下面的「解除压制」打架。
        //
        // ⚠️ 而且必须把 BlockedInput 也置上 —— 解除压制的那段只在**登记过**的情况下才清。
        // 之前书这里只设了 blockInput 没设标记，结果关书之后 blockInput 永远停在 true：
        // 玩家打开一次书就再也动不了（而且不报错）。
        if (HexCanvasState.Book.IsOpen)
        {
            Main.blockInput = true;
            Main.playerInventory = false;
            HexCanvasState.BlockedInput = true;
        }

        // 动画时钟：驱动 zappy 抖动随时间流动。单位是 MC 游戏刻（20/秒），泰拉每秒更新 60 次
        canvas.Tick += 20.0 / 60.0;

        // 无限媒质切换（开发者模式）——画布开不开都要能按。
        //
        // 安全门控：tModLoader 的按键绑定表在模组加载阶段尚未填充，
        // 此时读 JustPressed 会抛 KeyNotFoundException（key 'ModName/BindName' not present）。
        // 加载阶段 Main.gameMenu 为 true，而加载完成、进入主菜单/世界后就已就绪，
        // 所以用 gameMenu 作门控，并在首次成功读取后落闩，之后不再判空。
        // 踩过的坑：不加门控会每帧一条警告把日志刷爆。
        if (!_keybindReady && !Main.gameMenu)
        {
            try
            {
                var probe = HexCastingTerraria.ToggleInfiniteMediaKey;
                if (probe != null)
                {
                    _ = probe.JustPressed;
                    _keybindReady = true;
                }
            }
            catch (System.Collections.Generic.KeyNotFoundException)
            {
                // 还没就绪，下一帧再试
            }
        }

        if (_keybindReady)
        {
            try
            {
                if (HexCastingTerraria.ToggleInfiniteMediaKey?.JustPressed == true)
                {
                    HexPlayer.Get(Main.LocalPlayer).ToggleInfiniteMedia();
                }

                // K：手动补发开发者测试包（配置里也能设成进世界自动发）
                if (HexCastingTerraria.GiveDevKitKey?.JustPressed == true
                    && !HexCanvasState.Canvas.IsOpen
                    && !HexCanvasState.Book.IsOpen)
                {
                    Content.Items.DevKit.Give(Main.LocalPlayer);
                    HexCanvasState.SetMessage("已发放开发者测试包");
                }

                // F7：开发者面板（法术示例 / 调试开关 / 进度）
                if (HexCastingTerraria.DevPanelKey?.JustPressed == true
                    && !HexCanvasState.Canvas.IsOpen
                    && !HexCanvasState.Book.IsOpen)
                {
                    HexCanvasState.Dev.Toggle();
                }

                // H 翻页临摹引导；Shift+H 反向。
                // 只在画布打开时响应 —— 否则 H 会跟其它模组的键位抢。
                if (HexCastingTerraria.CycleGuideKey?.JustPressed == true
                    && HexCanvasState.Canvas.IsOpen)
                {
                    bool back = Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift)
                                || Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift);
                    CycleGuide(back ? -1 : 1);
                }
            }
            catch (System.Collections.Generic.KeyNotFoundException)
            {
                _keybindReady = false;   // 退回未就绪，重新走门控
            }
        }

        bool leftDown = Main.mouseLeft;
        bool rightDown = Main.mouseRight;
        bool leftWasDown = _prevMouseLeft;
        bool rightWasDown = _prevMouseRight;
        _prevMouseLeft = leftDown;
        _prevMouseRight = rightDown;

        if (!canvas.IsOpen)
        {
            // 关键：画布关闭时必须解除输入阻塞，否则玩家会完全无法移动。
            // 之前把 blockInput 设在 IsOpen 判断之外，导致退出画布后永久卡死。
            if (HexCanvasState.BlockedInput)
            {
                Main.blockInput = false;
                HexCanvasState.BlockedInput = false;
            }
            return;
        }

        // 画布打开时禁止玩家操作与物品栏，避免「边画边翻背包」
        Main.playerInventory = false;
        Main.blockInput = true;
        HexCanvasState.BlockedInput = true;

        // 瞄准点标记：让玩家看见「这次法术朝哪打」。
        // 必须每帧刷新（而不是只在打开时画一次）：玩家移动后瞄准点会跟着变。
        UpdateAimMarker();

        // 画布一律用屏幕像素（与 InterfaceScaleType.None 的绘制层一致），不受界面缩放影响
        float w = Main.screenWidth;
        float h = Main.screenHeight;
        var mouse = RawMouse();
        var prevMouse = _prevCanvasMouse ?? mouse;
        _prevCanvasMouse = mouse;

        // F1 切换调试面板
        if (Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.F1)
            && Main.oldKeyState.IsKeyUp(Microsoft.Xna.Framework.Input.Keys.F1))
        {
            ShowDebug = !ShowDebug;
        }

        // 注意：Esc 不再关闭画布（用户明确要求 Esc 无效）。
        // 物品栏的压制交给 ModPlayer.PostUpdate()，它每帧强制 Main.playerInventory = false，
        // 所以画布内按 Esc 既不会关画布、也不会弹背包。

        // 左键按下 → 落笔。原版只在真的落笔时播 START_PATTERN（点在已用格点上不响）
        if (leftDown && !leftWasDown)
        {
            if (canvas.DrawStart(mouse, w, h))
            {
                Content.SpellSounds.Play("casting.pattern.start");
            }
            prevMouse = mouse;
        }

        // 按住左键拖动 → 沿本帧鼠标路径逐步吸附。原版每个吸附事件（含第一段、回退）都播 ADD_TO_PATTERN
        // （抬起那一帧也要把最后一段位移算上，再收笔）
        // 一帧内吸附多步时只响一次，免得叠音爆音。
        if ((leftDown || leftWasDown) && canvas.State != DrawState.BetweenPatterns
            && canvas.DrawMove(prevMouse, mouse, w, h).Count > 0)
        {
            Content.SpellSounds.Play("casting.pattern.add_segment");
        }

        // 法术配色跟着本地玩家的存档值走（`colorize` 改的就是它）。
        // 放在这里而不是做成事件：颜色是**每帧都要用**的东西，
        // 事件驱动的写法漏一次刷新就会「颜色偶尔不生效」，很难查。
        if (!Main.gameMenu && Main.LocalPlayer is { active: true })
        {
            HexPigment.Refresh(Content.HexPlayer.Get(Main.LocalPlayer).PigmentDyeType);
        }

        // 法术环执行游标的存活时间。
        // 放在这里而不是 TileEntity 里：多人时「别人家的环」也要画高亮，
        // 而那个环的 TileEntity 不在我们这边跑。
        Content.Tiles.CircleCursor.Tick();

        // 左键抬起 → 收笔
        if (!leftDown && leftWasDown)
        {
            var result = canvas.DrawEnd();
            if (result != null)
            {
                // 立即送进 VM 求值（对齐原作：每画完一条就求值一次，栈在图案间累积）
                HexVmState.EvaluatePattern(Main.LocalPlayer, result.Pattern);

                // 识别提示受 `ShowPatternId` 控制：画得多了以后这条消息会挡住 HUD
                HexCanvasState.SetMessage(result.IsValid
                    ? (HexClientConfig.Instance.ShowPatternId ? $"识别到：{result.Matched!.Id}" : null)
                    : DescribeUnknownPattern(result.Pattern));

                // 开发者面板的临摹：对一下这条是不是当前这一步（放在识别提示之后，它的提示优先）
                UI.DevPanel.OnPatternDrawn(result.Pattern);

                // 对齐原作：栈已结算完毕（空栈 + 无括号 + 无待转义）时自动关闭画布。
                // 只在**求值成功**时关闭，避免画到未实现图案时把界面弹掉。
                if (result.IsValid
                    && HexVmState.LastResolution.IsSuccess()
                    && HexVmState.IsStackClear
                    && HexVmState.OpsConsumed > 0)
                {
                    HexCanvasState.CloseCanvas();
                }
            }
        }

        // 右键 → 关闭画布（返回）。保护帧内忽略，避免「打开的那次右键」立刻关掉。
        if (rightDown && !rightWasDown && HexCanvasState.CloseGuardFrames == 0)
        {
            HexCanvasState.CloseCanvas();
            HexCanvasState.SetMessage("画布已关闭（图案保留）");
        }
    }

    public override void ModifyInterfaceLayers(System.Collections.Generic.List<GameInterfaceLayer> layers)
    {
        if (Main.dedServ)
        {
            return;
        }

        // 画布打开时移除背包与悬停提示相关界面层。
        // 官方文档（Vanilla Interface layers values）：
        //   Vanilla: Inventory = "Draws and handles logic for everything inventory related."
        //   Vanilla: Sign Tile Bubble = 墓碑/告示牌悬停气泡
        //   Vanilla: Mouse Over = 生物/玩家/掉落物悬停逻辑
        //   Vanilla: Mouse Text = 悬停 tooltip 文本
        // 移除后画布内 Esc 与鼠标悬停都不再触发这些 UI。
        if (HexCanvasState.Canvas.IsOpen)
        {
            layers.RemoveAll(l => l.Name == "Vanilla: Inventory"
                               || l.Name == "Vanilla: Sign Tile Bubble"
                               || l.Name == "Vanilla: Mouse Over"
                               || l.Name == "Vanilla: Mouse Text");
        }

        int index = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
        if (index < 0)
        {
            index = layers.Count;
        }

        // 哨卫：世界里的东西，用「游戏缩放」层（跟着镜头缩放），画在所有 UI 下面
        layers.Insert(0, new LegacyGameInterfaceLayer(
            "HexCastingTerraria: Sentinel",
            () =>
            {
                SentinelRenderer.Draw(Main.spriteBatch);
                return true;
            },
            InterfaceScaleType.Game));
        index++;

        // 笔迹层：屏幕像素坐标（InterfaceScaleType.None），与输入端的 RawMouse() 同一套坐标。
        // 放在 HUD 层下面，HUD 文字压在笔迹上方。
        layers.Insert(index, new LegacyGameInterfaceLayer(
            "HexCastingTerraria: Hex Canvas Strokes",
            () =>
            {
                bool ctrl = Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftControl)
                            || Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightControl);
                // 原版：按住 Ctrl 才显示笔顺渐变（ctrlTogglesOffStrokeOrder 默认 false）
                HexCanvasState.Canvas.DrawContent(Main.screenWidth, Main.screenHeight, RawMouse(),
                    showStrokeOrder: ctrl, Matrix.Identity);
                return true;
            },
            InterfaceScaleType.None));
        index++;

        layers.Insert(index, new LegacyGameInterfaceLayer(
            "HexCastingTerraria: Hex Canvas",
            () =>
            {
                float w = Main.screenWidth;
                float h = Main.screenHeight;
                var mouse = new Vector2(Main.mouseX, Main.mouseY);

                var canvas = HexCanvasState.Canvas;
                DrawHud(Main.spriteBatch, w, h, mouse);
                DrawVmStack(Main.spriteBatch, w, h);

                // 探术透镜：戴着时把附近玩家的施法瞄准点标出来
                if (Content.HexPlayer.Get(Main.LocalPlayer).ScryingLensEquipped)
                {
                    DrawScryingMarks(Main.spriteBatch);
                }

                // 开发者调试叠加层（默认关闭，见 设置 → 模组配置）
                HexDebugOverlay.Draw(Main.spriteBatch, w, h, mouse);

                // 咒法学之书（帕秋莉手册）
                HexCanvasState.Book.Draw(Main.spriteBatch);

                // 开发者面板 / 临摹引导 / 待命施放提示
                HexCanvasState.Dev.Draw(Main.spriteBatch);

                return true;
            },
            InterfaceScaleType.UI));
    }

    private void DrawHud(SpriteBatch sb, float w, float h, Vector2 mouse)
    {
        var canvas = HexCanvasState.Canvas;
        var player = Main.LocalPlayer;
        if (player == null || !player.active)
        {
            return;
        }

        var hexPlayer = HexPlayer.Get(player);
        float x = 18f;
        float y = 18f;

        // ===== 媒质指示：背包里的媒质（配色对齐原作 MediaHelper.mediaBarColor）=====
        // 原版没有媒质条（媒质就在背包的物品里）；这只是个信息显示，可在设置里关掉。
        // 环 = 背包里媒质瓶的存量 / 上限；数字 = 背包里所有媒质来源的总量（与原版扣费时能用到的一致）。
        bool holdingStaff = player.HeldItem != null && player.HeldItem.type == ModContent.ItemType<Content.Items.DevStaff>();
        long invMedia = hexPlayer.InventoryMedia();
        var (flaskStored, flaskMax) = hexPlayer.FlaskMedia();
        bool shouldShowMedia = HexClientConfig.Instance.ShowMediaRing
            && (holdingStaff || canvas.IsOpen || invMedia > 0);
        if (shouldShowMedia)
        {
            float ringRadius = 26f;
            var center = new Vector2(x + ringRadius, y + ringRadius);

            float fullness = flaskMax == 0 ? (invMedia > 0 ? 1f : 0f) : (float)flaskStored / flaskMax;

            // 底环 + 按储量填充的彩色环（无限媒质时满环）
            float ringFullness = hexPlayer.InfiniteMedia ? 1f : fullness;
            DrawRing(sb, center, ringRadius, 4f, new Color(40, 32, 52, 220), 1f);
            DrawRing(sb, center, ringRadius, 4f, MediaBarColor(ringFullness), ringFullness);

            // 环内：数值（以「粉」为单位，与源项目 tooltip 一致）
            // 无限媒质时直接显示 ∞，避免误导
            string amount = hexPlayer.InfiniteMedia
                ? "∞"
                : (invMedia / (double)MediaConstants.DustUnit).ToString("#,##0.#");
            var amountSize = Terraria.GameContent.FontAssets.MouseText.Value.MeasureString(amount);
            Terraria.Utils.DrawBorderString(sb, amount,
                new Vector2(center.X - amountSize.X * 0.42f, center.Y - amountSize.Y * 0.62f),
                hexPlayer.InfiniteMedia ? GoodColor : TextColor, 0.62f);

            // 环内下方：百分比（无限时表示 100%）
            float shownFullness = hexPlayer.InfiniteMedia ? 1f : fullness;
            string pct = $"{(int)(shownFullness * 100)}%";
            var pctSize = Terraria.GameContent.FontAssets.MouseText.Value.MeasureString(pct);
            Terraria.Utils.DrawBorderString(sb, pct,
                new Vector2(center.X - pctSize.X * 0.35f, center.Y + 2f),
                MediaBarColor(fullness), 0.55f);

            // 环右侧：容量上限与图案数
            Terraria.Utils.DrawBorderString(sb, flaskMax > 0
                    ? $"媒质瓶 {flaskStored / (double)MediaConstants.DustUnit:#,##0.#} / {flaskMax / (double)MediaConstants.DustUnit:#,##0} 粉"
                    : "背包媒质（粉）",
                new Vector2(x + ringRadius * 2 + 12, y + 6), MediaColor, 0.75f);
            Terraria.Utils.DrawBorderString(sb, $"图案 {canvas.Patterns.Count} 条",
                new Vector2(x + ringRadius * 2 + 12, y + 26), new Color(190, 184, 210), 0.7f);
        }

        float msgY = y + 62;

        // ===== 正在画的这一笔（实时） =====
        //
        // 这是排查「画出来的和想画的不是一个形状」最直接的信息：
        // 一边拖就能看到系统读到的角度串、以及当前是否已经命中某条图案。
        // 没有它，玩家只有松手之后才知道画错了，而且只知道「无效」，不知道差在哪。
        bool diag = HexClientConfig.Instance.ShowDebugPanel;
        if (diag && canvas.IsOpen && canvas.WipPattern is { } wip)
        {
            var live = PatternRegistry.Match(wip);
            string sig = wip.AnglesSignature();

            string head = live != null
                ? $"✓ 已命中「{live.DisplayName()}」可以松手"
                : $"✎ 正在画 [{wip.StartDir} {sig}] {sig.Length} 笔";

            Terraria.Utils.DrawBorderString(sb, head, new Vector2(x, msgY),
                live != null ? GoodColor : new Color(150, 210, 255), 0.9f);
            msgY += 22f;

            if (live == null)
            {
                string? hint = Core.Casting.Math.PatternSuggestion.Describe(sig);
                if (hint != null)
                {
                    Terraria.Utils.DrawBorderString(sb, hint, new Vector2(x, msgY),
                        new Color(255, 210, 120), 0.8f);
                    msgY += 20f;
                }
            }
        }

        // ===== 最近识别结果 =====
        // （原版靠图案颜色表达结果：蓝=已求值 黄=已转义 红=出错/无效 灰=等待中）
        if (diag && canvas.LastPattern != null)
        {
            var last = canvas.LastPattern;
            string line = last.IsValid
                ? $"✓ {last.Matched!.DisplayName()}"
                : $"✗ 未识别 [{last.Pattern.StartDir} {last.Pattern.AnglesSignature()}]";
            Terraria.Utils.DrawBorderString(sb, line, new Vector2(x, msgY), last.IsValid ? GoodColor : BadColor, 0.9f);
            msgY += 22f;
        }

        // ===== 一次性反馈（不常驻） =====
        if (!string.IsNullOrEmpty(HexCanvasState.LastMessage) && HexCanvasState.MessageTimer > 0)
        {
            Terraria.Utils.DrawBorderString(sb, HexCanvasState.LastMessage, new Vector2(x, msgY), TextColor, 0.8f);
            msgY += 20f;
        }

        // ===== 临摹引导（**仅调试面板开启时**显示）=====
        // 曾经做成画布上常驻，那是**不忠实的设计**：
        // 原版画布上从不显示图案，玩家靠「咒法学之书」学图案（Patchouli 驱动，约 100 个条目）。
        // 所以这里退回成开发期辅助手段，正式方案是自建书 UI（见 TODO_PLAN.md「咒法学之书」）。
        if (canvas.IsOpen && GuideIndex >= 0 && HexClientConfig.Instance.ShowDebugPanel)
        {
            DrawGuideStrip(sb, w, h);
        }

        // ===== 调试面板（F1 开关，也可用设置里的开关彻底关掉）=====
        if (canvas.IsOpen && ShowDebug && HexClientConfig.Instance.ShowDebugPanel)
        {
            DrawDebugPanel(sb, canvas, w, h, mouse);
        }
    }

    /// <summary>当前临摹目标在「已实现图案」列表里的下标；-1 表示不显示引导。</summary>
    public static int GuideIndex { get; private set; } = -1;

    /// <summary>
    /// 在瞄准位置撒一小撮粒子，让玩家看见「这次法术朝哪打」。
    ///
    /// **为什么必须有这个**：画布一打开，鼠标就去画图案了，
    /// 玩家再也看不到自己指哪。而瞄准方向是**打开画布那一刻冻结**的
    /// （见 <see cref="Content.HexPlayer.FrozenAim"/>），
    /// 不标出来的话玩家只能凭记忆猜 —— 这是可用性问题，不是装饰。
    ///
    /// 位置取**射线命中点**（打到墙就是墙上那一点），没打中则取最大射程处。
    /// 只有少数几颗（默认 8，可配置 1~64）：泰拉 dust 数组长度固定，
    /// 刷爆会把其他玩家的粒子挤掉。
    /// </summary>
    private void UpdateAimMarker()
    {
        if (Main.dedServ) return;

        var config = HexClientConfig.Instance;
        if (!config.ShowAimMarker) return;

        var player = Main.LocalPlayer;
        if (player is not { active: true } || player.dead) return;

        var hexPlayer = Content.HexPlayer.Get(player);

        // 画布内一律用冻结的瞄准方向；没有冻结值就说明方向不可用，不标
        if (hexPlayer.FrozenAim is not { } aim) return;

        // 世界坐标 → 图格（与 ICastingWorld 的约定一致）
        float ox = player.Center.X / Core.Casting.HexUnits.PixelsPerTile;
        float oy = player.Center.Y / Core.Casting.HexUnits.PixelsPerTile;

        var hit = Core.World.TileRaycast.Cast(
            Content.TerrariaCastingWorld.SolidAt,
            ox, oy, aim.X, aim.Y, Core.Casting.HexUnits.RaycastDistanceTiles);

        Microsoft.Xna.Framework.Vector2 worldPos = hit is { } h
            ? new Microsoft.Xna.Framework.Vector2(
                (h.TileX + 0.5f) * Core.Casting.HexUnits.PixelsPerTile,
                (h.TileY + 0.5f) * Core.Casting.HexUnits.PixelsPerTile)
            : player.Center + aim * Core.Casting.HexUnits.RaycastDistancePixels;

        int count = System.Math.Clamp(config.AimMarkerDustCount, 1, 64);
        for (int i = 0; i < count; i++)
        {
            // 散布在命中点周围一圈，看起来像一个「落点」而不是一个像素
            double angle = i * System.Math.Tau / count;
            var offset = new Microsoft.Xna.Framework.Vector2(
                (float)System.Math.Cos(angle) * 6f,
                (float)System.Math.Sin(angle) * 6f);

            var dust = Dust.NewDustPerfect(
                worldPos + offset,
                Terraria.ID.DustID.PurpleTorch,
                Microsoft.Xna.Framework.Vector2.Zero,
                0,
                HexPigment.Current,
                1.1f);
            dust.noGravity = true;
            dust.velocity = Microsoft.Xna.Framework.Vector2.Zero;
        }
    }

    /// <summary>
    /// 接收其他玩家的瞄准点标记。由 <c>HexCastingTerraria.HandlePacket</c> 调用。
    ///
    /// 纯表现：在对方瞄准的位置撒一小撮粒子，让附近的人看见「这个法术打在哪儿」。
    /// 不做任何游戏逻辑判定 —— 那是服务端的事。
    /// </summary>
    public static void ReceiveSpellVisual(System.IO.BinaryReader reader)
    {
        int casterWho = reader.ReadByte();
        float aimX = reader.ReadSingle();
        float aimY = reader.ReadSingle();

        if (Main.dedServ) return;
        if (casterWho < 0 || casterWho >= Main.maxPlayers) return;

        var caster = Main.player[casterWho];
        if (caster is not { active: true }) return;

        var config = HexClientConfig.Instance;
        if (!config.ShowAimMarker) return;

        var aim = new Microsoft.Xna.Framework.Vector2(aimX, aimY);
        if (aim.LengthSquared() < 1e-6f) return;

        // 与自己的瞄准点标记同一套算法：打到墙就标在墙上，没打中则标在最大射程处
        float ox = caster.Center.X / Core.Casting.HexUnits.PixelsPerTile;
        float oy = caster.Center.Y / Core.Casting.HexUnits.PixelsPerTile;

        var hit = Core.World.TileRaycast.Cast(
            Content.TerrariaCastingWorld.SolidAt,
            ox, oy, aim.X, aim.Y, Core.Casting.HexUnits.RaycastDistanceTiles);

        Microsoft.Xna.Framework.Vector2 worldPos = hit is { } h
            ? new Microsoft.Xna.Framework.Vector2(
                (h.TileX + 0.5f) * Core.Casting.HexUnits.PixelsPerTile,
                (h.TileY + 0.5f) * Core.Casting.HexUnits.PixelsPerTile)
            : caster.Center + aim * Core.Casting.HexUnits.RaycastDistancePixels;

        int count = System.Math.Clamp(config.AimMarkerDustCount, 1, 64);
        for (int i = 0; i < count; i++)
        {
            double angle = i * System.Math.Tau / count;
            var offset = new Microsoft.Xna.Framework.Vector2(
                (float)System.Math.Cos(angle) * 6f,
                (float)System.Math.Sin(angle) * 6f);

            var dust = Dust.NewDustPerfect(
                worldPos + offset,
                Terraria.ID.DustID.PurpleTorch,
                Microsoft.Xna.Framework.Vector2.Zero,
                0,
                HexPigment.Current,
                1.1f);
            dust.noGravity = true;
            dust.velocity = Microsoft.Xna.Framework.Vector2.Zero;
        }
    }
    /// <summary>
    /// 翻页选择临摹目标。delta = +1 下一个 / -1 上一个；从「未开启」出发时进入首/末项。
    /// 列表来自 <see cref="PatternRenderer.GetImplementedPatterns"/>，只含**已实现行为**的图案。
    /// </summary>
    public static void CycleGuide(int delta)
    {
        var all = PatternRenderer.GetImplementedPatterns();
        if (all.Count == 0) { GuideIndex = -1; return; }

        int next = GuideIndex < 0 ? (delta >= 0 ? 0 : all.Count - 1) : GuideIndex + delta;
        next %= all.Count;
        if (next < 0) next += all.Count;
        GuideIndex = next;
    }

    /// <summary>关闭临摹引导。</summary>
    public static void HideGuide() => GuideIndex = -1;

    /// <summary>屏幕底部的临摹引导条：大号幽灵图案 + Id + 起始方向 / 角度串。</summary>
    private void DrawGuideStrip(SpriteBatch sb, float w, float h)
    {
        var all = PatternRenderer.GetImplementedPatterns();
        if (all.Count == 0) return;

        int idx = System.Math.Clamp(GuideIndex, 0, all.Count - 1);
        var def = all[idx];

        float cx = w * 0.5f;
        float cy = h - 132f;
        float size = 62f;

        // 背板：不画的话图案会跟地形糊在一起，白色地形上几乎看不见
        var box = new Rectangle((int)(cx - 160f), (int)(cy - size - 30f), 320, (int)(size * 2f + 86f));
        Terraria.Utils.DrawInvBG(sb, box, new Color(24, 20, 40) * 0.82f);

        PatternRenderer.DrawStaticPreview(
            (a, b, wd, c) => DrawSegmentForPreview(sb, a, b, wd, c),
            (p, r, c) => DrawDotForPreview(sb, p, r, c),
            PatternRegistry.PatternInThisWorld(def), new Vector2(cx, cy), size, new Color(120, 200, 255));

        string title = $"临摹目标 {idx + 1}/{all.Count}   {def.DisplayName()}";
        Terraria.Utils.DrawBorderString(sb, title,
            new Vector2(cx - 150f, cy - size - 24f), Color.White, 0.82f);
        Terraria.Utils.DrawBorderString(sb,
            $"起始 {def.StartDir}   角度 {def.Angles}",
            new Vector2(cx - 150f, cy + size + 6f), new Color(180, 175, 205), 0.68f);
        Terraria.Utils.DrawBorderString(sb,
            "[H] 下一个    [Shift+H] 上一个    （调试用）",
            new Vector2(cx - 150f, cy + size + 26f), new Color(140, 135, 165), 0.6f);
    }

    /// <summary>
    /// 显示 VM 栈内容 —— 这是「画图案 → 求值」闭环的可见证据。
    /// </summary>
    private void DrawVmStack(SpriteBatch sb, float w, float h)
    {
        var stack = HexVmState.Stack;
        bool hasAny = stack.Count > 0 || HexVmState.ParenCount > 0 || HexVmState.EscapeNext;

        float x = w - 250f;
        float y = 18f;

        if (!hasAny && HexVmState.LastResolution == ResolvedPatternType.Unresolved)
        {
            return;
        }

        Terraria.Utils.DrawBorderString(sb, $"── VM 栈 ({stack.Count}) ──",
            new Vector2(x, y), DebugColor, 0.75f);
        y += 20f;

        // 只显示栈顶若干项（栈可能很长）
        int show = System.Math.Min(stack.Count, 8);
        for (int i = stack.Count - show; i < stack.Count; i++)
        {
            Terraria.Utils.DrawBorderString(sb, HexVmState.Describe(stack[i]),
                new Vector2(x, y), TextColor, 0.7f);
            y += 17f;
        }
        if (stack.Count > show)
        {
            Terraria.Utils.DrawBorderString(sb, $"… 其余 {stack.Count - show} 项",
                new Vector2(x, y), new Color(170, 165, 190), 0.65f);
            y += 17f;
        }

        // 状态行
        string stateText = HexVmState.LastResolution switch
        {
            ResolvedPatternType.Evaluated => "求值成功",
            ResolvedPatternType.Escaped => "已转义",
            ResolvedPatternType.Invalid => "无效图案",
            ResolvedPatternType.Errored => "执行出错",
            _ => "未求值",
        };
        var stateColor = HexVmState.LastResolution.IsSuccess() ? GoodColor : BadColor;
        Terraria.Utils.DrawBorderString(sb, stateText + $"  算力 {HexVmState.OpsConsumed}",
            new Vector2(x, y), stateColor, 0.68f);
        y += 17f;

        if (HexVmState.ParenCount > 0)
        {
            Terraria.Utils.DrawBorderString(sb, $"列表构建中（{HexVmState.ParenCount} 层括号）",
                new Vector2(x, y), new Color(255, 210, 120), 0.68f);
            y += 17f;
        }
        if (HexVmState.EscapeNext)
        {
            Terraria.Utils.DrawBorderString(sb, "下一个值将被转义",
                new Vector2(x, y), new Color(255, 210, 120), 0.68f);
            y += 17f;
        }
        if (HexVmState.LastError != null)
        {
            Terraria.Utils.DrawBorderString(sb, HexVmState.LastError,
                new Vector2(x, y), BadColor, 0.65f);
        }
    }
    /// <summary>
    /// 调试面板：显示输入与坐标换算的中间量，并画出「最容易画对的几条图案」预览。
    /// 「画不出线」「画了不识别」这类问题的根因基本都能在这里一眼看出。
    /// </summary>
    private void DrawDebugPanel(SpriteBatch sb, HexCanvas canvas, float w, float h, Vector2 mouse)
    {
        float size = canvas.HexSize(w, h);
        var hex = canvas.PxToCoord(mouse, w, h);
        var anchor = canvas.AnchorCoord;
        var anchorPx = canvas.CoordToPx(anchor, w, h);
        float distSq = (mouse - anchorPx).LengthSquared();
        float snapSq = size * size * 2f * Math.Clamp(canvas.SnapThreshold, 0.5f, 1f);

        float x = 18f;
        float y = h - 210f;

        string[] lines =
        {
            "── 咒法学调试 (F1 隐藏) ──",
            $"屏幕 {w:0}x{h:0}  格距 {size:0.0}  格点 ({hex.X}, {hex.Y})",
            $"状态 {canvas.State}  锚点 ({anchor.X}, {anchor.Y})",
            $"距离² {distSq:0} / 阈值² {snapSq:0}   {(distSq >= snapSq ? "已达吸附" : "未达吸附")}",
            $"左键 {(Main.mouseLeft ? "按下" : "抬起")}   右键 {(Main.mouseRight ? "按下" : "抬起")}   图案 {canvas.Patterns.Count} 条",
        };

        foreach (var line in lines)
        {
            Terraria.Utils.DrawBorderString(sb, line, new Vector2(x, y), DebugColor, 0.72f);
            y += 17f;
        }

        // ---- 最简单的图案预览：照着画就能命中 ----
        y += 6f;
        Terraria.Utils.DrawBorderString(sb, "照下面任一条画（白点=起笔处）：", new Vector2(x, y), TextColor, 0.72f);
        y += 20f;

        var simplest = PatternRenderer.GetSimplestPatterns(6);
        float previewSize = 34f;
        float slot = 118f;
        for (int i = 0; i < simplest.Count; i++)
        {
            var def = simplest[i];
            var center = new Vector2(x + 26f + (i % 3) * slot, y + (i / 3) * 62f);

            PatternRenderer.DrawStaticPreview(
                (a, b, wd, c) => DrawSegmentForPreview(sb, a, b, wd, c),
                (p, r, c) => DrawDotForPreview(sb, p, r, c),
                PatternRegistry.PatternInThisWorld(def), center, previewSize, new Color(120, 200, 255));

            Terraria.Utils.DrawBorderString(sb, def.DisplayName(),
                new Vector2(center.X - previewSize, center.Y + previewSize * 0.7f), MediaColor, 0.6f);
            Terraria.Utils.DrawBorderString(sb, $"{def.StartDir} {def.Angles}",
                new Vector2(center.X - previewSize, center.Y + previewSize * 0.7f + 13f),
                new Color(170, 165, 190), 0.55f);
        }

        // ---- 若已有未命中的图案，给出最接近的一条 ----
        if (canvas.LastPattern is { IsValid: false } last)
        {
            var closest = PatternRenderer.FindClosest(last.Pattern.AnglesSignature());
            if (closest != null)
            {
                var (def, dist) = closest.Value;
                string hint = dist == 0
                    ? $"与「{def.DisplayName()}」签名相同（异常：应当已命中）"
                    : $"最接近：「{def.DisplayName()}」（差 {dist}）  签名 {def.Angles}";
                Terraria.Utils.DrawBorderString(sb, hint,
                    new Vector2(x, y + 132f), new Color(255, 210, 120), 0.7f);
            }
        }
    }

    private void DrawSegmentForPreview(SpriteBatch sb, Vector2 a, Vector2 b, float width, Color color)
    {
        var pixel = Pixel;
        var delta = b - a;
        float len = delta.Length();
        if (len < 0.01f)
        {
            return;
        }
        sb.Draw(pixel, a, null, color, MathF.Atan2(delta.Y, delta.X),
            new Vector2(0f, pixel.Height * 0.5f), new Vector2(len, width), SpriteEffects.None, 0f);
    }

    private void DrawDotForPreview(SpriteBatch sb, Vector2 center, float radius, Color color)
    {
        var pixel = Pixel;
        int s = Math.Max(1, (int)(radius * 2f));
        sb.Draw(pixel, new Rectangle((int)(center.X - s * 0.5f), (int)(center.Y - s * 0.5f), s, s),
            null, color, 0f, Vector2.Zero, SpriteEffects.None, 0f);
    }

    /// <summary>
    /// 媒质进度条配色。逐行对齐源项目 MediaHelper.mediaBarColor：
    ///   r = lerp(amt, 84, 254)
    ///   g = lerp(amt, 57, 203)
    ///   b = lerp(amt, 138, 230)
    /// 即从深紫（空）渐变到亮粉紫（满）。
    /// </summary>
    public static Color MediaBarColor(float amt)
    {
        amt = MathHelper.Clamp(amt, 0f, 1f);
        return new Color(
            (int)MathHelper.Lerp(84f, 254f, amt),
            (int)MathHelper.Lerp(57f, 203f, amt),
            (int)MathHelper.Lerp(138f, 230f, amt));
    }

    private void DrawRing(SpriteBatch sb, Vector2 center, float radius, float thickness, Color color, float fillFraction)
    {
        var pixel = Pixel;

        fillFraction = MathHelper.Clamp(fillFraction, 0f, 1f);
        const int segments = 64;
        int drawCount = (int)MathF.Round(segments * fillFraction);
        if (drawCount <= 0)
        {
            return;
        }

        float start = -MathHelper.PiOver2;
        float step = MathHelper.TwoPi / segments;

        for (int i = 0; i < drawCount; i++)
        {
            float a0 = start + step * i;
            float a1 = start + step * (i + 1);

            var p0 = center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius;
            var p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
            var delta = p1 - p0;
            float len = delta.Length();
            if (len < 0.01f)
            {
                continue;
            }

            sb.Draw(pixel, p0, null, color, MathF.Atan2(delta.Y, delta.X),
                new Vector2(0f, pixel.Height * 0.5f), new Vector2(len, thickness), SpriteEffects.None, 0f);
        }
    }

    /// <summary>
    /// 探术透镜：在附近**其他玩家**身上标出他们的施法瞄准点。
    ///
    /// 数据来源是联机时已经在广播的 `<see cref="ReceiveSpellVisual"/>` ——
    /// 单人时没有别的玩家，自然什么都不画（原版单人也没得看）。
    ///
    /// 与瞄准标记同一套画法：在鼠标方向上打一条射线，打到墙就标在墙上。
    /// </summary>
    private static void DrawScryingMarks(SpriteBatch sb)
    {
        if (Main.dedServ) return;

        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var other = Main.player[i];
            if (other is not { active: true }) continue;
            if (i == Main.myPlayer) continue;

            // 距离太远的没必要画（也画不出有用信息）
            if (Vector2.DistanceSquared(other.Center, Main.LocalPlayer.Center) > 120f * 16f * (120f * 16f))
            {
                continue;
            }

            // 对方身上一个光点：告诉玩家"这个人在施法范围内"
            var center = other.Center - Main.screenPosition;
            HexPixel.DrawDot(sb, center, 4f, new Color(206, 178, 255, 180));

            Terraria.Utils.DrawBorderString(sb, other.name,
                center + new Vector2(-other.name.Length * 3f, -34f),
                new Color(226, 210, 255, 200), 0.65f);
        }
    }

    /// <summary>
    /// 未识别时的提示：**你画的是什么 + 最接近哪一条 + 差几笔**。
    ///
    /// 只回一句「这不是一个有效的图案」等于把玩家扔在原地 ——
    /// 他既不知道系统读成了什么形状，也不知道该往哪改。
    /// 给出最接近的那条（按匹配用的同一套角度串编辑距离），
    /// 就能把「猜」变成「微调两笔」。
    /// </summary>
    private static string DescribeUnknownPattern(HexPattern drawn)
    {
        string signature = drawn.AnglesSignature();
        string head = $"未识别：[{drawn.StartDir} {signature}]";

        // 文案与施法失败的聊天提示共用同一个函数，保证两处说法一致
        string? hint = Core.Casting.Math.PatternSuggestion.Describe(signature);
        return hint == null ? head : $"{head}  {hint}";
    }
}
