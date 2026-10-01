using System.Collections.Generic;
using HexCastingTerraria.Core.Canvas;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using ReLogic.OS;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI.Chat;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 把 iota 的显示（<see cref="DisplayText"/>）画进泰拉：
///   - 聊天栏、物品说明这些「字符串」的地方：写成 <see cref="DisplayTags"/> 的标记，这里注册的两个标记处理器负责解析；
///   - HUD 这些自己画的地方：<see cref="DrawLine"/>（一行画不下就截断加灰色「...」，和原版施法界面的栈一样）。
/// 图案画成和文字一样高的小图（原版装着 Inline 时的样子）：悬停显示图案名，点击复制 HexPattern[…]。
/// </summary>
public static class RichText
{
    /// <summary>原版 GuiSpellcasting：一行放不下时截断，末尾加灰色「...」。</summary>
    public static void DrawLine(SpriteBatch sb, DisplayText text, Vector2 position, float scale, float maxWidth)
        => DrawSnippets(sb, Snippets(text), position, scale, maxWidth);

    /// <summary>同上，参数是带 <see cref="DisplayTags"/> 标记的字符串（附属给的现成文字）。</summary>
    public static void DrawTagged(SpriteBatch sb, string tagged, Vector2 position, Color baseColor, float scale, float maxWidth)
        => DrawSnippets(sb, ChatManager.ParseMessage(tagged, baseColor).ToArray(), position, scale, maxWidth);

    public static TextSnippet[] Snippets(DisplayText text)
    {
        var o = new List<TextSnippet>();
        foreach (var s in text.Spans(McColors.White))
        {
            var c = ToColor(s.Color);
            o.Add(s.Pattern is { } p ? new GlyphSnippet(p, c) : new TextSnippet(s.Text!, c));
        }
        return o.ToArray();
    }

    private static void DrawSnippets(SpriteBatch sb, TextSnippet[] snippets, Vector2 position, float scale, float maxWidth)
    {
        var font = FontAssets.MouseText.Value;
        var fitted = Fit(font, snippets, scale, maxWidth);
        ChatManager.DrawColorCodedStringWithShadow(sb, font, fitted, position, 0f, Vector2.Zero, new Vector2(scale), out _);
    }

    /// <summary>上游 getDisplayWithMaxWidth：按宽度截断（留出「...」的位置），截断了就补灰色「...」。</summary>
    private static TextSnippet[] Fit(DynamicSpriteFont font, TextSnippet[] snippets, float scale, float maxWidth)
    {
        if (maxWidth <= 0 || ChatManager.GetStringSize(font, snippets, new Vector2(scale)).X <= maxWidth) return snippets;
        float budget = maxWidth - font.MeasureString("...").X * scale;
        var o = new List<TextSnippet>();
        float used = 0f;
        foreach (var sn in snippets)
        {
            float w = Width(font, sn, scale);
            if (used + w <= budget)
            {
                o.Add(sn);
                used += w;
                continue;
            }
            if (sn is not GlyphSnippet)
            {
                // 文字逐字截
                string t = sn.Text;
                int n = t.Length;
                while (n > 0 && used + font.MeasureString(t.Substring(0, n)).X * scale > budget) n--;
                if (n > 0) o.Add(new TextSnippet(t.Substring(0, n), sn.Color, sn.Scale));
            }
            break;
        }
        o.Add(new TextSnippet("...", ToColor(McColors.Gray)));
        return o.ToArray();
    }

    private static float Width(DynamicSpriteFont font, TextSnippet sn, float scale)
        => sn.UniqueDraw(true, out var size, null, default, default, scale) ? size.X : font.MeasureString(sn.Text).X * scale;

    public static Color ToColor(uint rgb) => new((int)((rgb >> 16) & 0xFF), (int)((rgb >> 8) & 0xFF), (int)(rgb & 0xFF));

    internal static Color ParseColor(string? hex, Color fallback)
        => hex is { Length: 6 } && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? ToColor(v) : fallback;

    // ── 内嵌图案的大小与画法（上游 InlinePatternRenderer）──────────────────────

    /// <summary>
    /// 上游按 MC 的 9 像素行高：相邻两点 4/1.5 像素、整体不高于 8 像素，宽度按比例再加 1 像素，线宽 1 像素。
    /// 这里按泰拉字体的行高等比例放大。
    /// </summary>
    private static (List<Vec2f> Points, float MinX, float MinY, float Unit, float Width, float Height, float Px) Layout(HexPattern pattern, float scale)
    {
        var pts = StaticPatternArt.BarePoints(pattern);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in pts)
        {
            minX = System.Math.Min(minX, p.X); maxX = System.Math.Max(maxX, p.X);
            minY = System.Math.Min(minY, p.Y); maxY = System.Math.Max(maxY, p.Y);
        }
        float rangeX = maxX - minX, rangeY = maxY - minY;
        float px = FontAssets.MouseText.Value.MeasureString("M").Y * scale / 9f;   // MC 的 1 像素
        float perUnit = rangeY > 0.001f ? System.Math.Min(4f / 1.5f, 8f / rangeY) : 4f / 1.5f;
        float width = ((float)System.Math.Ceiling(rangeX * perUnit) + 1f) * px;
        return (pts, minX, minY, perUnit * px, width, rangeY * perUnit * px, px);
    }

    public static float GlyphWidth(HexPattern pattern, float scale) => Layout(pattern, scale).Width;

    public static void DrawGlyph(SpriteBatch sb, HexPattern pattern, Vector2 lineTopLeft, float scale, Color color)
    {
        var l = Layout(pattern, scale);
        float lineH = 9f * l.Px;
        // 水平居中在留出的宽度里，竖直居中在这一行（上游往上挪了半个像素）
        float drawnW = (l.Width / l.Px - 1f) * l.Px;
        var origin = new Vector2(lineTopLeft.X + (l.Width - drawnW) / 2f, lineTopLeft.Y + (lineH - l.Height) / 2f - 0.5f * l.Px);
        var tex = TextureAssets.MagicPixel.Value;
        var src = new Rectangle(0, 0, 1, 1);
        for (int i = 0; i + 1 < l.Points.Count; i++)
        {
            var a = origin + new Vector2((l.Points[i].X - l.MinX) * l.Unit, (l.Points[i].Y - l.MinY) * l.Unit);
            var b = origin + new Vector2((l.Points[i + 1].X - l.MinX) * l.Unit, (l.Points[i + 1].Y - l.MinY) * l.Unit);
            var d = b - a;
            sb.Draw(tex, a, src, color, (float)System.Math.Atan2(d.Y, d.X), new Vector2(0f, 0.5f),
                new Vector2(d.Length(), l.Px), SpriteEffects.None, 0f);
        }
    }

    /// <summary>上游 InlinePatternData.getPatternName：认识的图案用名字，否则 HexPattern(方向 签名)。</summary>
    public static string PatternName(HexPattern pattern)
    {
        string? name = PatternDisplay.NameOf(pattern);
        if (name is not null) return name;
        string sig = pattern.AnglesSignature();
        return $"HexPattern({pattern.StartDir}{(sig.Length > 0 ? " " + sig : "")})";
    }
}

/// <summary>[hext/RRGGBB:文字]：一段有颜色的文字（文字是 % 编码的）。</summary>
internal sealed class HexTextTagHandler : ITagHandler
{
    public TextSnippet Parse(string text, Color baseColor = default, string? options = null)
        => new(DisplayTags.Unescape(text), RichText.ParseColor(options, baseColor));
}

/// <summary>[hexp/RRGGBB:方向/签名]：一个内嵌图案。</summary>
internal sealed class HexGlyphTagHandler : ITagHandler
{
    public TextSnippet Parse(string text, Color baseColor = default, string? options = null)
    {
        var color = RichText.ParseColor(options, baseColor);
        return DisplayTags.ParseGlyph(text) is { } p ? new GlyphSnippet(p, color) : new TextSnippet(text, color);
    }
}

/// <summary>内嵌图案：画成小图，悬停显示名字，点击复制（上游 Inline 的悬停卷轴 / 点击复制）。</summary>
internal sealed class GlyphSnippet : TextSnippet
{
    private readonly HexPattern _pattern;

    public GlyphSnippet(HexPattern pattern, Color color) : base(pattern.ToString(), color)
    {
        _pattern = pattern;
        CheckForHover = true;
    }

    public override void OnHover() => Main.instance.MouseText(RichText.PatternName(_pattern));

    public override void OnClick() => Platform.Get<IClipboard>().Value = _pattern.ToString();

    public override bool UniqueDraw(bool justCheckingString, out Vector2 size, SpriteBatch spriteBatch, Vector2 position = default, Color color = default, float scale = 1f)
    {
        float w = RichText.GlyphWidth(_pattern, scale);
        size = new Vector2(w, FontAssets.MouseText.Value.MeasureString("M").Y * scale);
        // 阴影那几遍颜色是黑的：不画（和泰拉物品图标标记一样）
        if (!justCheckingString && spriteBatch is not null && (color.R != 0 || color.G != 0 || color.B != 0))
            RichText.DrawGlyph(spriteBatch, _pattern, position, scale, color);
        return true;
    }

    public override float GetStringLength(DynamicSpriteFont font) => RichText.GlyphWidth(_pattern, Scale);
}

/// <summary>把两个标记登记给泰拉（只在客户端；卸载时拿掉，免得旧程序集被聊天系统一直引用着）。</summary>
public sealed class RichTextSystem : ModSystem
{
    public override void Load()
    {
        if (Main.dedServ) return;
        ChatManager.Register<HexTextTagHandler>(DisplayTags.TextTag);
        ChatManager.Register<HexGlyphTagHandler>(DisplayTags.GlyphTag);
    }

    public override void Unload()
    {
        if (Main.dedServ) return;
        var field = typeof(ChatManager).GetField("_handlers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (field?.GetValue(null) is System.Collections.Concurrent.ConcurrentDictionary<string, ITagHandler> handlers)
        {
            handlers.TryRemove(DisplayTags.TextTag, out _);
            handlers.TryRemove(DisplayTags.GlyphTag, out _);
        }
    }
}
