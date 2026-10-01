using System.Collections.Generic;
using HexCastingTerraria.Content.Tiles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Client;

/// <summary>
/// 探知透镜（原版 ItemLens）的两个效果：
///
///   ① **看方块的信息**（HexAdditionalRenderers.tryRenderScryingLensOverlay + ScryingLensOverlays）：
///      准星对着的方块旁边列出几行「图标 + 文字」。原版登记的有：促动石（媒质、消息、牧师绑定的人）、
///      阿卡夏书架、音符盒、红石元件。泰拉没有后面三样；阿卡夏在移植版是另一套结构（待审），这里先做促动石。
///   ② **咒术网格变细**：GRID_ZOOM ×1.33（在 HexStaff.OpenCanvas 里乘上去）
///
/// 生效条件（原版 getDefaultAttributeModifiers）：戴在头上 / 拿在任意一只手 / 饰品栏（饰品模组）。
/// 泰拉：饰品栏，或者拿在手上 / 「另一只手」（快捷栏里手上那格右边一格）。
/// 准星 → 泰拉的鼠标：鼠标指着的图格，行列在鼠标右上方（原版在准星右边、底对齐准星）。
///
/// 这里曾经是「标出附近其他玩家的施法瞄准点」—— 原版透镜没有这个功能。
/// </summary>
internal static class ScryingOverlay
{
    /// <summary>原版 ItemLens.GRID_ZOOM：MULTIPLY_BASE 0.33。</summary>
    public const float GridZoom = 1.33f;

    public static bool HasSight(Player p)
    {
        if (Content.HexPlayer.Get(p).ScryingLensEquipped) return true;
        int lens = ModContent.ItemType<Content.Items.ScryingLens>();
        if (p.HeldItem.type == lens) return true;
        int sel = p.selectedItem;
        return sel is >= 0 and < 10 && p.inventory[(sel + 1) % 10].type == lens;
    }

    public static void Draw(SpriteBatch sb)
    {
        var player = Main.LocalPlayer;
        if (Main.dedServ || Main.gameMenu || player is not { active: true } || !HasSight(player)) return;

        var tilePos = Main.MouseWorld.ToTileCoordinates();
        if (!WorldGen.InWorld(tilePos.X, tilePos.Y, 1)) return;

        var lines = new List<(int Icon, string Text, Color Color)>();
        Collect(tilePos.X, tilePos.Y, lines);
        if (lines.Count == 0) return;

        const float lineH = 22f;
        var origin = new Vector2(Main.mouseX + 18f, Main.mouseY - lines.Count * lineH);
        foreach (var (icon, text, color) in lines)
        {
            float tx = 0f;
            if (icon > 0)
            {
                Main.instance.LoadItem(icon);
                var tex = TextureAssets.Item[icon].Value;
                float s = 16f / System.Math.Max(tex.Width, tex.Height);
                sb.Draw(tex, origin + new Vector2(0f, 2f), null, Color.White, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
                tx = 20f;
            }
            UI.RichText.DrawTagged(sb, text, origin + new Vector2(tx, 0f), color, 0.8f, -1f);
            origin.Y += lineH;
        }
    }

    private static void Collect(int x, int y, List<(int, string, Color)> lines)
    {
        if (TileLoader.GetTile(Main.tile[x, y].TileType) is not HexImpetusBase { Kind: not ImpetusKind.Empty } impetus) return;
        if (HexImpetusEntity.FindAt(x, y) is not { } e) return;

        // 原版 applyScryingLensOverlay：媒质（无限 / 「%d 紫水晶粉」，DecimalFormat("###,###.##")）
        int dust = ModContent.ItemType<Content.Items.AmethystDust>();
        string media = e.Media < 0
            ? "无限"
            : $"{(double)e.Media / Core.Media.MediaConstants.DustUnit:#,0.##} 紫水晶粉";
        lines.Add((dust, media, Color.White));

        if (e.DisplayMsg is { } msg)
        {
            int icon = e.DisplayIcon switch
            {
                ImpetusDisplay.Print => ItemID.Book,
                ImpetusDisplay.Mishap => ItemID.MusicBox,      // 原版「唱片 11」
                ImpetusDisplay.NoExit => ItemID.Sign,          // 原版告示牌
                ImpetusDisplay.NoClosure => ItemID.Rope,       // 原版拴绳
                _ => 0,
            };
            lines.Add((icon, msg, e.DisplayIcon == ImpetusDisplay.Print ? Color.White : new Color(255, 120, 120)));
        }

        // 原版牧师促动石：「与%s绑定」/「未绑定」
        if (impetus.Kind == ImpetusKind.Redstone)
        {
            lines.Add((0, e.BoundName is { } n ? $"与{n}绑定" : "未绑定", new Color(200, 200, 200)));
        }
    }
}
