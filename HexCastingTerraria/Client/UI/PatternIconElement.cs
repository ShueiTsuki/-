using System;
using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.UI;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 把一个**图案**画成一个 UIElement（缩略图）。
///
/// ## 为什么包一层元素，而不是在列表里自己画
///
/// 元素树里能画图案的只有 `UIElement.DrawSelf` —— 一旦进了这个体系：
///   · 命中测试（`ContainsPoint` / `IsMouseHovering`）框架负责
///   · 点击事件（`OnLeftClick`）自动冒泡到父容器
///   · UI 缩放（`InterfaceScaleType.UI`）自动生效
///   · 滚动（放进 `UIList` 里）自动跟随
/// 自己在列表里按坐标画，这四样都得手写 —— 前几轮的坑全是这么来的。
///
/// ## 画法本身复用现成的
///
/// 图案的折线与拐角点由 <see cref="PatternRenderer.DrawStaticPreview"/> 画，
/// 与旧界面用的是同一份实现（它自己又是从 Core 的 <c>HexGrid</c> 拿几何）。
/// 这里只负责把它摆到元素中央。
/// </summary>
public sealed class PatternIconElement : UIElement
{
    private readonly PatternDef _def;
    private readonly float _size;

    /// <summary>颜色按状态区分：可实现 / 未实现 / 不适用 —— 与旧界面一致。</summary>
    public Color Color { get; set; } = new(206, 186, 255);

    public PatternIconElement(PatternDef def, float size = 26f)
    {
        _def = def;
        _size = size;
        Width.Set(size + 8f, 0f);
        Height.Set(size + 8f, 0f);
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        var d = GetDimensions();
        var center = new Vector2(d.X + (d.Width / 2f), d.Y + (d.Height / 2f));

        // line / dot 两个委托就是旧界面传进来的那两个（HexPixel 画的 1x1 像素）
        PatternRenderer.DrawStaticPreview(
            (a, b, thickness, c) => HexPixel.DrawLine(spriteBatch, a, b, thickness, c),
            (p, r, c) => HexPixel.DrawDot(spriteBatch, p, r, c),
            _def.Prototype,
            center,
            _size,
            Color);
    }
}
