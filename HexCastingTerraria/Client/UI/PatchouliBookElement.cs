using HexCastingTerraria.Core.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.UI;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 帕秋莉皮肤（那本皮革书）作为**一个 UIElement**。
///
/// ## 为什么要包进元素树，而不是像上一版那样直接画
///
/// 皮肤本身仍然自绘 —— `UIPanel` 给不了 Minecraft 那本皮革书，
/// 这套几何与配色已经在离屏出图里验证过（`book_skin_*.png`）。
///
/// 但**壳**必须是元素树：一旦成为 `UIElement`，就自动获得
///   · 命中测试与点击事件（`OnLeftClick`，坐标由框架给）
///   · UI 缩放（`InterfaceScaleType.UI`）
///   · 输入封锁（外层 `DrawSelf` 里设的 `mouseInterface` / `LockVanillaMouseScroll`）
///   · 与另一套皮肤共用同一个 `BookDocument`
/// 上一版我把它直接画在旧书的调用点上，这四样全都要自己处理，结果全出了错。
///
/// ## 点击怎么算
///
/// `UIMouseEvent.MousePosition` 是**屏幕/UI 空间**的坐标，而皮肤的
/// <c>BookMetrics</c> 是相对元素左上角算的 —— 所以要减掉元素位置。
/// 这一步很容易漏，漏了的表现是"点哪都不对"，而且不报错。
/// </summary>
public sealed class PatchouliBookElement : UIElement
{
    private readonly BookDocument _doc;
    private readonly BookView _view;
    private readonly PatchouliSkin _skin = new();
    private readonly SpriteBatchBookCanvas _canvas = new();

    public PatchouliBookElement(BookDocument doc)
    {
        _doc = doc;
        _view = new BookView(doc);

        // 封面 → 目录 → 条目，和原版那本书一样逐级进入
        _view.Open();

        OnLeftClick += HandleLeftClick;
    }

    /// <summary>供外部（配置切换、Esc）控制视图层级。</summary>
    public BookView View => _view;

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        var d = GetDimensions();
        var viewport = new RectF(0f, 0f, d.Width, d.Height);

        var metrics = _skin.Measure(_view, viewport);
        _skin.Draw(_canvas, _view, metrics);
    }

    private void HandleLeftClick(UIMouseEvent evt, UIElement listeningElement)
    {
        var d = GetDimensions();
        var viewport = new RectF(0f, 0f, d.Width, d.Height);
        var metrics = _skin.Measure(_view, viewport);

        // 屏幕坐标 → 元素局部坐标。**这一减法不能省。**
        float lx = evt.MousePosition.X - d.X;
        float ly = evt.MousePosition.Y - d.Y;

        var hit = _skin.HitTest(_view, metrics, lx, ly);
        switch (hit.Kind)
        {
            case BookHitKind.CategoryCell:
                if (hit.Index >= 0 && hit.Index < _doc.Categories.Count)
                {
                    _view.EnterCategory(_doc.Categories[hit.Index].Id);
                }
                break;

            case BookHitKind.EntryRow:
                var cat = _view.CurrentCategory;
                if (cat is not null && hit.Index >= 0 && hit.Index < cat.Entries.Count)
                {
                    _view.OpenEntry(cat.Entries[hit.Index].Id);
                }
                break;

            case BookHitKind.Back:
                _view.Back();
                break;

            case BookHitKind.NextPage:
                _view.NextPageOrEntry();
                break;

            case BookHitKind.PrevPage:
                _view.PrevPageOrEntry();
                break;

            case BookHitKind.PageDot:
                _view.GoToSpread(hit.Index);
                break;

            default:
                if (_view.Kind == BookViewKind.Cover) { _view.Open(); }
                break;
        }
    }
}
