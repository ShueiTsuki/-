using System.Text.RegularExpressions;
using HexCastingTerraria.Addons.HexParse.Core;
using HexCastingTerraria.Core.Casting.Iotas;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;

namespace HexCastingTerraria.Addons.HexDebug.Interop;

/// <summary>
/// 联动（HexParse 开着才会有注释 iota）：剪接台格子里画 HexParse 的注释（上游 hexparse compat/hexdebug/CommentRenderer.kt）——
/// 深绿色；换行缩进注释写 \\n、右上角小字 +缩进数；大法术占位写 ?；三个字以内照写，更长取前两个字加一点。
/// </summary>
internal static class HexParseCommentRenderer
{
    private static readonly Regex Indent = new(@"^\n\s*$", RegexOptions.Compiled);
    private static readonly Color DarkGreen = new(0x00, 0xAA, 0x00);

    public static bool TryDraw(SpriteBatch sb, Iota iota, Rectangle cell)
    {
        if (iota is not CommentIota c) return false;
        string content = c.Comment;
        int tab = 0;
        string shown;
        if (Indent.IsMatch(content))
        {
            tab = content.Length - 1;
            shown = "\\n";
        }
        else if (content.StartsWith(CommentIota.GreatPlaceholderPrefix, System.StringComparison.Ordinal)) shown = "?";
        else if (content.Length <= 3) shown = content;
        else shown = content.Substring(0, 2) + ".";

        var font = FontAssets.MouseText.Value;
        var size = font.MeasureString(shown) * 0.8f;
        Terraria.Utils.DrawBorderString(sb, shown, new Vector2(cell.Center.X - size.X / 2, cell.Center.Y - size.Y / 2), DarkGreen, 0.8f);
        if (tab > 0)
        {
            string t = "+" + tab;
            Terraria.Utils.DrawBorderString(sb, t, new Vector2(cell.Right - font.MeasureString(t).X * 0.5f - 3, cell.Y + 2), DarkGreen, 0.5f);
        }
        return true;
    }
}
