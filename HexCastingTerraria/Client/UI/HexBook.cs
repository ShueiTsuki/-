using HexCastingTerraria.Core.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 咒法学之书（帕秋莉手册）的开关、输入与绘制。内容与排版在 Core（BookContent / PatchouliRenderer）。
///
/// 操作照 Patchouli：左键点击；右键 = 返回上一界面（在落地页再按就合上）；Esc 合上；滚轮翻页。
/// 坐标一律用「界面缩放空间」：原始鼠标 ÷ Main.UIScale，与界面层（InterfaceScaleType.UI）的绘制坐标一致。
/// </summary>
public sealed class HexBook
{
    private BookView? _view;
    private PatchouliRenderer? _renderer;
    private readonly SpriteBatchBookCanvas _canvas = new();
    private BookFrame? _lastFrame;

    /// <summary>本帧的解锁进度（每帧重算一次：拿到紫水晶、打了 Boss 之后书马上跟着变）。</summary>
    private static BookProgress _progress = new();
    private int _scrollAccum;

    public bool IsOpen { get; private set; }

    public void Open()
    {
        EnsureBuilt();
        _view!.Open();
        IsOpen = true;
        _lastFrame = null;
        SoundEngine.PlaySound(SoundID.MenuOpen);
    }

    public void Close()
    {
        if (!IsOpen) { return; }
        IsOpen = false;
        SoundEngine.PlaySound(SoundID.MenuClose);
    }

    public void Toggle()
    {
        if (IsOpen) { Close(); }
        else { Open(); }
    }

    private void EnsureBuilt()
    {
        if (_view is not null) { return; }
        _view = new BookView(Document);
        _renderer = new PatchouliRenderer(new GameBookData(_canvas));
        _view.EntryUnlocked = _renderer.IsUnlocked;
    }

    private static Vector2 UiMouse() => HexClientSystem.RawMouse() / Main.UIScale;

    /// <summary>
    /// 界面缩放空间里的屏幕大小。⚠️ 必须用**真实**屏幕像素再除以 UIScale：
    /// 绘制界面层时 PlayerInput.SetZoom_UI 已经把 Main.screenWidth 改成了「÷UIScale」之后的值，
    /// 这里曾经再除一次 —— 界面缩放 150% 时书只按 1080/2.25 = 480 高来排，又小又偏左上。
    /// </summary>
    private static Vector2 UiViewport() => new(PlayerInput.RealScreenWidth / Main.UIScale, PlayerInput.RealScreenHeight / Main.UIScale);

    private static BookDocument? _document;

    /// <summary>全书内容（生成一次就缓存：500 多页，别每帧重建）。</summary>
    public static BookDocument Document => _document ??= BookContent.Create();

    /// <summary>当前玩家的解锁进度（书、开发者面板共用）。</summary>
    public static BookProgress CurrentProgress()
    {
        var p = new BookProgress { UnlockAll = Config.HexClientConfig.Instance.UnlockWholeBook };
        if (Main.gameMenu || Main.LocalPlayer is not { active: true } player) { return p; }
        var hp = Content.HexPlayer.Get(player);
        p.Amethyst = hp.ObtainedAmethyst;
        p.FailedGreatSpell = hp.FailedGreatSpell;
        p.Overcasted = hp.Overcasted;
        p.Enlightened = hp.Enlightened || Config.HexClientConfig.Instance.AlwaysEnlightened;
        if (NPC.downedBoss1) { p.Milestones.Add("boss1"); }
        if (NPC.downedBoss2) { p.Milestones.Add("boss2"); }
        if (NPC.downedBoss3) { p.Milestones.Add("boss3"); }
        if (Main.hardMode) { p.Milestones.Add("hardmode"); }
        if (NPC.downedMechBossAny) { p.Milestones.Add("mech"); }
        if (NPC.downedPlantBoss) { p.Milestones.Add("plantera"); }
        if (NPC.downedGolemBoss) { p.Milestones.Add("golem"); }
        if (NPC.downedMoonlord) { p.Milestones.Add("moonlord"); }
        return p;
    }

    /// <summary>已解锁 / 总条目数（开发者面板显示用）。</summary>
    public static (int Unlocked, int Total) UnlockStats()
    {
        var progress = CurrentProgress();
        int n = 0, total = 0;
        foreach (var c in Document.Categories)
        {
            foreach (var e in c.Entries)
            {
                total++;
                if (BookUnlocks.IsUnlocked(e.Advancement, progress)) { n++; }
            }
        }
        return (n, total);
    }

    /// <summary>每帧输入（PostUpdateInput 里调用）。用上一帧画出来的命中区 —— 绘制与命中是同一份几何。</summary>
    public void HandleInput(bool leftClick, bool rightClick)
    {
        if (!IsOpen || _view is null) { return; }

        if (Main.keyState.IsKeyDown(Keys.Escape) && Main.oldKeyState.IsKeyUp(Keys.Escape))
        {
            Close();
            return;
        }

        var mouse = UiMouse();
        if (_lastFrame is { } frame && frame.Book.Contains(mouse.X, mouse.Y))
        {
            Main.LocalPlayer.mouseInterface = true;
        }

        if (leftClick && _lastFrame?.HitAt(mouse.X, mouse.Y) is { } hit)
        {
            bool moved = hit.Kind switch
            {
                BookActionKind.OpenCategory => _view.OpenCategory(hit.Arg),
                BookActionKind.OpenEntry => _view.OpenEntry(hit.Arg),
                BookActionKind.Link => FollowLink(hit.Arg),
                BookActionKind.Back => _view.Back(),
                BookActionKind.PrevSpread => _view.PrevSpread(),
                BookActionKind.NextSpread => _view.NextSpread(),
                _ => false,
            };
            if (moved) { Flip(); }
        }
        else if (rightClick)
        {
            if (_view.Back()) { Flip(); }
            else { Close(); }
        }

        // 滚轮翻页（原版 Patchouli 同样支持）；吃掉滚轮，不让它去切快捷栏
        _scrollAccum += PlayerInput.ScrollWheelDelta;
        PlayerInput.ScrollWheelDelta = 0;
        if (_scrollAccum >= 120) { _scrollAccum = 0; if (_view.PrevSpread()) { Flip(); } }
        else if (_scrollAccum <= -120) { _scrollAccum = 0; if (_view.NextSpread()) { Flip(); } }
    }

    private bool FollowLink(string target)
    {
        if (_view!.FollowLink(target)) { return true; }
        if (target.StartsWith("http", System.StringComparison.Ordinal))
        {
            Main.NewText(target, Color.LightSkyBlue);   // 外部链接：打到聊天栏，玩家自己去开
        }
        return false;
    }

    private static void Flip() => SoundEngine.PlaySound(SoundID.MenuTick);

    /// <summary>在界面层（UI 缩放）里画书。</summary>
    public void Draw(SpriteBatch sb)
    {
        if (!IsOpen || _view is null || _renderer is null) { return; }

        // 像素风贴图整数放大必须用点采样，否则边缘发糊。画完**原样恢复**界面层的批次参数
        //（GameInterfaceLayer 用的是全默认值 + UIScaleMatrix，反编译确认）。
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None,
            RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
        try
        {
            var vp = UiViewport();
            var mouse = UiMouse();
            _renderer.SizeFactor = Config.HexClientConfig.Instance.BookSize;
            _progress = CurrentProgress();
            _lastFrame = _renderer.Render(_canvas, _view, vp.X, vp.Y, mouse.X, mouse.Y);
        }
        finally
        {
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, null, null, null, null, null, Main.UIScaleMatrix);
        }

        if (_lastFrame.Tooltip.Length > 0)
        {
            Main.hoverItemName = _lastFrame.Tooltip;
        }
    }

    /// <summary>书要的游戏数据：物品名、泰拉里**实际存在**的配方（不是 MC 原版配方）。</summary>
    private sealed class GameBookData : IBookData
    {
        private readonly SpriteBatchBookCanvas _canvas;

        public GameBookData(SpriteBatchBookCanvas canvas) => _canvas = canvas;

        public bool IsUnlocked(string advancement) => BookUnlocks.IsUnlocked(advancement, _progress);

        public string ItemName(string itemKey)
        {
            int type = _canvas.ItemType(itemKey);
            return type > 0 ? Lang.GetItemNameValue(type) : string.Empty;
        }

        public BookRecipe? FindRecipe(string resultItemKey)
        {
            int type = _canvas.ItemType(resultItemKey);
            if (type <= 0) { return null; }
            for (int i = 0; i < Recipe.numRecipes; i++)
            {
                var r = Main.recipe[i];
                if (r is null || r.Disabled || r.createItem.type != type) { continue; }
                var book = new BookRecipe { Result = resultItemKey, ResultCount = r.createItem.stack };
                foreach (var ing in r.requiredItem)
                {
                    if (ing.IsAir) { continue; }
                    book.Ingredients.Add(("Id:" + ing.type, ing.stack));
                }
                // 1.4.5 起一条配方只有一个合成站（requiredTile 是 int，-1 = 徒手）
                if (r.requiredTile >= 0)
                {
                    int tileItem = Terraria.ModLoader.TileLoader.GetItemDropFromTypeAndStyle(r.requiredTile);
                    if (tileItem > 0) { book.Station = "Id:" + tileItem; }
                }
                return book;
            }
            return null;
        }
    }
}
