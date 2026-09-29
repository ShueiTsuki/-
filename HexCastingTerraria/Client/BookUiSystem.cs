using System.Collections.Generic;
using HexCastingTerraria.Client.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace HexCastingTerraria.Client;

/// <summary>
/// 书本 UI 的**骨架** —— 按 tModLoader 官方那套来，而不是我自己接。
///
/// ## 为什么要重做
///
/// 上一版我把自绘的皮肤硬接到旧书的绘制调用点上，结果连着出六个 bug：
/// 崩游戏、书自己开、列表溢出、关不掉、点不动、文字不吃裁剪。
/// 根因不是画法，是**没走泰拉自己的 UI 机制** —— 缩放、命中测试、输入封锁
/// 这些框架都提供了，我全部自己实现，于是每一处接线都成了坑。
///
/// ## 官方骨架（见 BOOK_UI_DESIGN 第 15 节，有出处）
///
///   [Autoload(Side = ModSide.Client)] ModSystem   ← UI 只在客户端
///       UserInterface + UIState
///       UpdateUI(gameTime)        → _ui.Update(gameTime)
///       ModifyInterfaceLayers     → 在 "Vanilla: Mouse Text" 前插入一层，
///                                    InterfaceScaleType.UI 负责 UI 缩放
///
/// 插在 "Vanilla: Mouse Text" **之前**，这样 Main.hoverItemName 设的悬浮文字还能显示。
/// </summary>
[Autoload(Side = ModSide.Client)]
public sealed class BookUiSystem : ModSystem
{
    private UserInterface? _ui;
    private BookUiState? _state;
    private GameTime? _lastUpdateUiGameTime;

    /// <summary>本地玩家是否正把书本 UI 开着（客户端表现，不需要同步）。</summary>
    public static bool Visible { get; private set; }

    public override void Load()
    {
        if (Main.dedServ) { return; }   // 服务端没有图形，别初始化

        _state = new BookUiState();
        _state.Activate();              // 官方文档特别强调：不 Activate 会在开局崩
        _ui = new UserInterface();
    }

    public override void Unload()
    {
        _state = null;
        _ui = null;
    }

    public override void UpdateUI(GameTime gameTime)
    {
        _lastUpdateUiGameTime = gameTime;
        _ui?.Update(gameTime);
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int idx = layers.FindIndex(l => l.Name.Equals("Vanilla: Mouse Text"));
        if (idx == -1) { return; }

        layers.Insert(idx, new LegacyGameInterfaceLayer(
            "HexCastingTerraria: Book",
            delegate
            {
                if (_ui?.CurrentState is not null && _lastUpdateUiGameTime is not null)
                {
                    _ui.Draw(Main.spriteBatch, _lastUpdateUiGameTime);
                }
                return true;
            },
            InterfaceScaleType.UI));    // ← 这一项负责 UI 缩放
    }

    // ── 显隐 ────────────────────────────────────────────────────────

    public static void Show()
    {
        var sys = ModContent.GetInstance<BookUiSystem>();
        if (sys?._ui is null || sys._state is null) { return; }
        sys._ui.SetState(sys._state);
        Visible = true;
    }

    public static void Hide()
    {
        var sys = ModContent.GetInstance<BookUiSystem>();
        sys?._ui?.SetState(null);
        Visible = false;
    }

    public static void Refresh()
    {
        var sys = ModContent.GetInstance<BookUiSystem>();
        sys?._state?.Recalculate();
    }
}
