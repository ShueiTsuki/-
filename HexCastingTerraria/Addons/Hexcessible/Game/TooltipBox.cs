using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace HexCastingTerraria.Addons.Hexcessible.Game;

/// <summary>
/// 画 Minecraft 样式的提示框（上游用 DrawContext.drawTooltip：深紫底、紫色渐变边框，贴在给定点右上方，出屏就往回挪）。
/// 屏幕像素坐标。
/// </summary>
public static class TooltipBox
{
    public const float TextScale = 0.8f;

    // Minecraft Formatting 的颜色
    public static readonly Color White = new(255, 255, 255);
    public static readonly Color Red = new(0xFF, 0x55, 0x55);
    public static readonly Color Yellow = new(0xFF, 0xFF, 0x55);
    public static readonly Color Blue = new(0x55, 0x55, 0xFF);
    public static readonly Color Gray = new(0xAA, 0xAA, 0xAA);
    public static readonly Color DarkGray = new(0x55, 0x55, 0x55);

    // Minecraft TooltipBackgroundRenderer 的颜色（ARGB 0xF0100010 底、0x505000FF → 0x5028007F 边框）
    private static readonly Color Background = new Color(0x10, 0x00, 0x10) * (0xF0 / 255f);
    private static readonly Color BorderTop = new Color(0x50, 0x00, 0xFF) * (0x50 / 255f);
    private static readonly Color BorderBottom = new Color(0x28, 0x00, 0x7F) * (0x50 / 255f);

    private const int Pad = 6;
    private const int Border = 2;

    public static Vector2 Measure(string text) => FontAssets.MouseText.Value.MeasureString(text) * TextScale;

    /// <summary>画一个框，返回框的高度（调用方往下排下一个框用）。</summary>
    public static float Draw(SpriteBatch sb, IReadOnlyList<(string Text, Color Color)> lines, float x, float y)
    {
        if (lines.Count == 0) return 0f;
        float lineH = Measure("A").Y;
        float w = 0f;
        foreach (var (t, _) in lines) w = System.Math.Max(w, Measure(t).X);
        float h = lineH * lines.Count;

        // drawTooltip：贴在 (x + 12, y - 12)，超出屏幕右边 / 下边就往回挪
        float bx = x + 12, by = y - 12;
        if (bx + w + Pad * 2 > Main.screenWidth) bx = System.Math.Max(4, x - 28 - w);
        if (by + h + Pad * 2 > Main.screenHeight) by = Main.screenHeight - h - Pad * 2 - 4;
        if (by < 4) by = 4;

        var px = TextureAssets.MagicPixel.Value;
        var box = new Rectangle((int)bx - Pad, (int)by - Pad, (int)w + Pad * 2, (int)h + Pad * 2);
        sb.Draw(px, box, Background);
        // 边框：上下两条实色，左右两条竖向渐变（分段近似）
        sb.Draw(px, new Rectangle(box.X + Border, box.Y + Border, box.Width - Border * 2, Border), BorderTop);
        sb.Draw(px, new Rectangle(box.X + Border, box.Bottom - Border * 2, box.Width - Border * 2, Border), BorderBottom);
        const int steps = 8;
        int inner = box.Height - Border * 4;
        for (int i = 0; i < steps; i++)
        {
            int y0 = box.Y + Border * 2 + inner * i / steps;
            int y1 = box.Y + Border * 2 + inner * (i + 1) / steps;
            var c = Color.Lerp(BorderTop, BorderBottom, (i + 0.5f) / steps);
            sb.Draw(px, new Rectangle(box.X + Border, y0, Border, y1 - y0), c);
            sb.Draw(px, new Rectangle(box.Right - Border * 2, y0, Border, y1 - y0), c);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            Terraria.Utils.DrawBorderString(sb, lines[i].Text, new Vector2(bx, by + lineH * i), lines[i].Color, TextScale);
        }
        return h + Pad * 2 + 2;
    }

    public static float Draw(SpriteBatch sb, string text, Color color, float x, float y)
        => Draw(sb, new[] { (text, color) }, x, y);
}
