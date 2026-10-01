using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 按物品**实例**的状态换贴图（原版的 item model overrides + 叠层着色）。
///
/// 泰拉一种物品只有一张贴图（TextureAssets.Item[type]），没有「按实例换模型」——
/// 所以在背包和地上自己画：从 `Content/Items/States/*.png` 状态图集（每格 32×32）取格，
/// 画本体，再画按原版规则着色的叠层，然后告诉泰拉不用画默认贴图了。见 TERRARIA_RENDERING_NOTES.md。
///
/// 原版规则（RegisterClientStuff）：
///   - 核心 / 法术书：变体 × [空 / 有内容 / 密封]；叠层颜色 = 存着的 iota 的类型色（图案橙、数字绿……）
///   - 结念绳：写入后加叠层（同样按 iota 类型着色）
///   - 符纸 / 缀品 / 造物 / 远古杂件：变体 × [空 / 已封咒术]（叠层不着色）
///   - 媒质之瓶：大小 = 1.049658·ln(上限 / 晶体 + 9.06152) − 2.1436 取整（0..4），满度 = 存量 / 上限按四分之一取整
/// </summary>
public sealed class ItemStateArt : GlobalItem
{
    private readonly struct Layer
    {
        public readonly string Sheet;
        public readonly int Col, Row;
        public readonly Color Tint;

        public Layer(string sheet, int col, int row, Color tint)
        {
            Sheet = sheet;
            Col = col;
            Row = row;
            Tint = tint;
        }
    }

    /// <summary>原版各 iota 类型的 color()。</summary>
    private static Color IotaColor(Iota? iota) => iota switch
    {
        PatternIota => new Color(0xff, 0xaa, 0x00),
        DoubleIota => new Color(0x55, 0xff, 0x55),
        VectorIota => new Color(0xff, 0x30, 0x30),
        EntityIota => new Color(0x55, 0xff, 0xff),
        BooleanIota => new Color(0xff, 0xff, 0x55),
        ListIota => new Color(0xaa, 0x00, 0xaa),
        NullIota => new Color(0xaa, 0xaa, 0xaa),
        GarbageIota => new Color(0x50, 0x50, 0x50),
        ContinuationIota => new Color(0xcc, 0x00, 0x00),
        _ => new Color(0xf8, 0x00, 0xf8),      // 原版 ERROR_COLOR
    };

    /// <summary>这件物品现在该画哪几层；null = 用默认贴图。</summary>
    private static Layer[]? Layers(Item item)
    {
        switch (item.ModItem)
        {
            case Focus f:
            {
                if (f.Stored is null) return new[] { new Layer("Focus", 0, f.Variant, Color.White) };
                int c = f.Sealed ? 3 : 1;
                return new[] { new Layer("Focus", c, f.Variant, Color.White), new Layer("Focus", c + 1, f.Variant, IotaColor(f.Stored)) };
            }
            case Spellbook b:
            {
                var iota = b.Read();
                if (iota is null) return new[] { new Layer("Spellbook", 0, b.Variant, Color.White) };
                int c = b.IsSealed ? 3 : 1;
                return new[] { new Layer("Spellbook", c, b.Variant, Color.White), new Layer("Spellbook", c + 1, b.Variant, IotaColor(iota)) };
            }
            case ThoughtKnot k:
                return k.Stored is null
                    ? null
                    : new[] { new Layer("ThoughtKnot", 0, 0, Color.White), new Layer("ThoughtKnot", 1, 0, IotaColor(k.Stored)) };
            case ItemPackagedSpell { UsesStateArt: true } p:
            {
                string sheet = p switch
                {
                    AncientCypher => "AncientCypher",
                    Cypher => "Cypher",
                    Trinket => "Trinket",
                    _ => "Artifact",
                };
                return p.IsEmpty
                    ? new[] { new Layer(sheet, 0, p.Variant, Color.White) }
                    : new[] { new Layer(sheet, 0, p.Variant, Color.White), new Layer(sheet, 1, p.Variant, Color.White) };
            }
            case MediaFlask m:
            {
                double pred = 1.049658 * System.Math.Log((double)m.MaxMedia / MediaConstants.CrystalUnit + 9.06152) - 2.1436;
                int size = System.Math.Clamp((int)System.Math.Floor(pred), 0, 4);
                int fill = m.MaxMedia <= 0 ? 0 : System.Math.Clamp((int)System.Math.Floor((double)m.Media / m.MaxMedia * 4), 0, 4);
                return new[] { new Layer("MediaFlask", fill, size, Color.White) };
            }
        }
        return null;
    }

    private static Texture2D Sheet(string name)
        => ModContent.Request<Texture2D>("HexCastingTerraria/Content/Items/States/" + name, ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;

    private static Rectangle Cell(Layer l) => new(l.Col * 32, l.Row * 32, 32, 32);

    public override bool PreDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
        Color drawColor, Color itemColor, Vector2 origin, float scale)
    {
        if (Layers(item) is not { } layers) return true;
        // 泰拉给的 scale 是按默认贴图（32×32）算好的，状态格同样是 32×32，直接用
        foreach (var l in layers)
        {
            spriteBatch.Draw(Sheet(l.Sheet), position, Cell(l), drawColor.MultiplyRGBA(l.Tint), 0f,
                new Vector2(16f, 16f), scale, SpriteEffects.None, 0f);
        }
        return false;
    }

    public override bool PreDrawInWorld(Item item, SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
        ref float rotation, ref float scale, int whoAmI)
    {
        if (Layers(item) is not { } layers) return true;
        // 与泰拉 Main.DrawItem 同一个摆法：底边对齐碰撞箱底边、水平居中
        var center = item.position - Main.screenPosition + new Vector2(item.width / 2f, item.height - 16f);
        var color = item.GetAlpha(lightColor);
        foreach (var l in layers)
        {
            spriteBatch.Draw(Sheet(l.Sheet), center, Cell(l), color.MultiplyRGBA(l.Tint), rotation,
                new Vector2(16f, 16f), scale, SpriteEffects.None, 0f);
        }
        return false;
    }
}
