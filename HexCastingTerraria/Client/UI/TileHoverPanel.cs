using System.Collections.Generic;
using HexCastingTerraria.Content.Tiles;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 鼠标指着刻了图案的石板、存了东西的阿卡夏书架时，在鼠标旁弹出一个框，画出上面的图案（书架另外写出存的内容）。
///
/// 原版是把图案直接画在方块正面；泰拉一格只有 16 像素，图案缩成一团、线也断成点，看不清（2026-10-02 客户端测试截图）。
/// 用户定：这两种方块只分「有图案 / 没图案」两种样子，图案改成鼠标悬停时看 —— 做法照泰拉自己的告示牌（Main.signHover）：
/// 离多远都看得到（方块的 MouseOver / MouseOverFar 都登记），鼠标在界面上时不弹。挂轴框够大，照旧画在上面。
/// 框的样子照物品说明：深蓝底框；石板的图案衬原版石板提示框的底图（和石板物品的说明一样）。
/// </summary>
public sealed class TileHoverPanel : ModSystem
{
    private const float PatternSize = 96f;
    private const int Padding = 10;

    private static Point? _hover;

    /// <summary>上一帧真的画出来的是哪一格（客户端测试用）。</summary>
    internal static Point? LastShown { get; private set; }

    /// <summary>方块的 MouseOver / MouseOverFar 里调用：这一帧鼠标指着 (i, j)。</summary>
    public static void Hover(int i, int j) => _hover = new Point(i, j);

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int at = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
        layers.Insert(at < 0 ? layers.Count : at, new LegacyGameInterfaceLayer("HexCastingTerraria: Tile Hover", () =>
        {
            Draw(Main.spriteBatch);
            return true;
        }, InterfaceScaleType.UI));
    }

    public override void Unload()
    {
        _hover = null;
        LastShown = null;
    }

    private static void Draw(SpriteBatch sb)
    {
        var hover = _hover;
        _hover = null;
        LastShown = null;
        if (hover is not { } p || Main.gameMenu || Main.mapFullscreen || Main.LocalPlayer.mouseInterface) return;
        if (!WorldGen.InWorld(p.X, p.Y) || !Main.tile[p.X, p.Y].HasTile) return;

        string title;
        HexPattern? pattern;
        Iota? datum = null;
        bool slate;
        int type = Main.tile[p.X, p.Y].TileType;
        if (type == ModContent.TileType<HexSlate>())
        {
            pattern = HexSlateEntity.FindAt(p.X, p.Y)?.Pattern;
            title = Language.GetTextValue("Mods.HexCastingTerraria.Items.HexSlateItem.WrittenName");
            slate = true;
        }
        else if (type == ModContent.TileType<AkashicBookshelf>())
        {
            var entity = AkashicBookshelfEntity.FindAt(p.X, p.Y);
            pattern = entity?.Pattern;
            datum = entity?.Datum;
            title = Lang.GetItemNameValue(ModContent.ItemType<Content.Items.AkashicBookshelfItem>());
            slate = false;
        }
        else
        {
            return;
        }
        if (pattern == null) return;

        var font = FontAssets.MouseText.Value;
        var titleSize = font.MeasureString(title);
        float lineHeight = titleSize.Y;
        float width = System.Math.Max(PatternSize, titleSize.X);
        float height = lineHeight + 4 + PatternSize + (datum != null ? 4 + lineHeight : 0);
        if (datum != null) width = System.Math.Max(width, 220f);

        // 照物品说明：鼠标右下方，出了屏幕就往回收
        float screenW = Main.screenWidth / Main.UIScale, screenH = Main.screenHeight / Main.UIScale;
        var pos = new Vector2(Main.mouseX + 20, Main.mouseY + 20);
        if (pos.X + width + Padding * 2 > screenW) pos.X = screenW - width - Padding * 2;
        if (pos.Y + height + Padding * 2 > screenH) pos.Y = screenH - height - Padding * 2;

        var box = new Rectangle((int)pos.X, (int)pos.Y, (int)width + Padding * 2, (int)height + Padding * 2);
        Utils.DrawInvBG(sb, box, new Color(23, 25, 81, 255) * 0.925f);
        var inner = pos + new Vector2(Padding);
        Utils.DrawBorderString(sb, title, inner, Color.White);

        var patternTopLeft = inner + new Vector2((width - PatternSize) / 2f, lineHeight + 4);
        if (slate)
        {
            var bg = ModContent.Request<Texture2D>("HexCastingTerraria/Content/Items/States/SlateTooltip",
                ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
            sb.Draw(bg, patternTopLeft, Color.White);
        }
        if (datum != null)
        {
            RichText.DrawLine(sb, datum.DisplayRich(), inner + new Vector2(0, lineHeight + 4 + PatternSize + 4), 1f, width);
        }
        // 图案最后画：它会先把前面排队的贴图画掉，再画自己（PrimitiveBatch.Flush）
        PatternArt.DrawReadable(pattern, patternTopLeft + new Vector2(PatternSize / 2f), PatternSize);
        LastShown = p;
    }
}
