using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace HexCastingTerraria.Addons.Hexcessible.Game;

/// <summary>
/// 画 Minecraft 样式的提示框（上游用 DrawContext.drawTooltip：深紫底、紫色渐变边框，贴在给定点右上方，出屏就往回挪）。
/// 屏幕像素坐标。上游的尺寸是 MC 界面像素（一行字 9 + 1 = 10），这里用 <see cref="Unit"/>（一行字高的十分之一）换算。
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

    /// <summary>一行：几段不同颜色的字。</summary>
    public sealed class Line : List<(string Text, Color Color)>
    {
        public Line() { }

        public Line(string text, Color color) => Add((text, color));

        public string Plain
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var (t, _) in this) sb.Append(t);
                return sb.ToString();
            }
        }
    }

    public static Vector2 Measure(string text) => FontAssets.MouseText.Value.MeasureString(text) * TextScale;

    /// <summary>一行字的高度（MC 的 fontHeight + 1）。</summary>
    public static float LineHeight => Measure("A").Y;

    /// <summary>MC 界面像素换成屏幕像素的比例：一行字 = 10 个 MC 界面像素。</summary>
    public static float Unit => LineHeight / 10f;

    public static float Width(Line line)
    {
        float w = 0f;
        foreach (var (t, _) in line) w += Measure(t).X;
        return w;
    }

    /// <summary>
    /// MC wrapLines：按宽度折行，能在空格处断就在空格处断，否则逐字断（中文没有空格）；原文里的换行照旧。
    /// </summary>
    public static List<Line> Wrap(string text, Color color, float maxWidth)
    {
        var lines = new List<Line>();
        foreach (var para in text.Split('\n'))
        {
            var cur = new StringBuilder();
            int lastSpace = -1;
            foreach (var ch in para)
            {
                cur.Append(ch);
                if (ch == ' ') lastSpace = cur.Length - 1;
                if (Measure(cur.ToString()).X <= maxWidth || cur.Length == 1) continue;
                if (lastSpace > 0)
                {
                    lines.Add(new Line(cur.ToString(0, lastSpace), color));
                    cur.Remove(0, lastSpace + 1);
                }
                else
                {
                    lines.Add(new Line(cur.ToString(0, cur.Length - 1), color));
                    cur.Remove(0, cur.Length - 1);
                }
                lastSpace = cur.ToString().LastIndexOf(' ');
            }
            lines.Add(new Line(cur.ToString(), color));
        }
        return lines;
    }

    /// <summary>
    /// MC drawTooltip：框贴在 (x + 12, y - 12)，超出右边就挪到左边，超出下边就往上顶。返回框的高度（往下排下一个框用）。
    /// </summary>
    public static float Draw(SpriteBatch sb, IReadOnlyList<Line> lines, float x, float y)
    {
        if (lines.Count == 0) return 0f;
        float u = Unit;
        float lineH = LineHeight;
        float w = 0f;
        foreach (var l in lines) w = System.Math.Max(w, Width(l));
        float h = lineH * lines.Count;

        float bx = x + 12 * u, by = y - 12 * u;
        if (bx + w > Main.screenWidth) bx -= 28 * u + w;
        if (by + h + 6 * u > Main.screenHeight) by = Main.screenHeight - h - 6 * u;
        if (bx < 4 * u) bx = 4 * u;
        if (by < 4 * u) by = 4 * u;

        int pad = (int)System.Math.Round(3 * u);
        int border = System.Math.Max(1, (int)System.Math.Round(u));
        var px = TextureAssets.MagicPixel.Value;
        var box = new Rectangle((int)bx - pad - border, (int)by - pad - border, (int)w + (pad + border) * 2, (int)h + (pad + border) * 2);
        sb.Draw(px, box, Background);
        // 边框：上下两条实色，左右两条竖向渐变（分段近似）
        sb.Draw(px, new Rectangle(box.X + border, box.Y + border, box.Width - border * 2, border), BorderTop);
        sb.Draw(px, new Rectangle(box.X + border, box.Bottom - border * 2, box.Width - border * 2, border), BorderBottom);
        const int steps = 8;
        int inner = box.Height - border * 4;
        for (int i = 0; i < steps; i++)
        {
            int y0 = box.Y + border * 2 + inner * i / steps;
            int y1 = box.Y + border * 2 + inner * (i + 1) / steps;
            var c = Color.Lerp(BorderTop, BorderBottom, (i + 0.5f) / steps);
            sb.Draw(px, new Rectangle(box.X + border, y0, border, y1 - y0), c);
            sb.Draw(px, new Rectangle(box.Right - border * 2, y0, border, y1 - y0), c);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            float tx = bx;
            foreach (var (t, c) in lines[i])
            {
                Terraria.Utils.DrawBorderString(sb, t, new Vector2(tx, by + lineH * i), c, TextScale);
                tx += Measure(t).X;
            }
        }
        return h + (pad + border) * 2 + 2;
    }

    public static float Draw(SpriteBatch sb, Line line, float x, float y) => Draw(sb, new[] { line }, x, y);

    public static float Draw(SpriteBatch sb, string text, Color color, float x, float y) => Draw(sb, new Line(text, color), x, y);

    /// <summary>MC drawTextWithShadow：不带框的一行字。</summary>
    public static void Text(SpriteBatch sb, Line line, float x, float y)
    {
        foreach (var (t, c) in line)
        {
            Terraria.Utils.DrawBorderString(sb, t, new Vector2(x, y), c, TextScale);
            x += Measure(t).X;
        }
    }
}
