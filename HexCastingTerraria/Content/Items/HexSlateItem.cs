using System.Collections.Generic;
using HexCastingTerraria.Content.Tiles;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 石板（物品形态）。对应源项目 `hexcasting:slate`（ItemSlate）。
///
/// 照原版是一个**数据载体**：只收图案（canWrite：图案或清空），书吏之策略能写进去、书吏之精思能读出来。
/// 放下时图案跟着进方块；挖掉时方块里的图案复制回掉落的物品（原版掉落表 copy BlockEntityTag.pattern），
/// 所以刻了图案的石板能搬走再放。空白的叫「空白石板」，有图案的叫「有图案的石板」，贴图也不同。
///
/// 和其他数据载体不同，石板能堆叠：一堆共用一个图案（原版同一堆物品共用一份数据），
/// 只有图案一样的才叠得到一起。放下后的操作（存着图案的物品右键写入、空手右键转朝向）见 <see cref="HexSlate"/>。
/// </summary>
public sealed class HexSlateItem : ItemIotaStorage
{
    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.width = 16;
        Item.height = 16;
        Item.maxStack = 999;
        Item.useTurn = true;
        Item.autoReuse = true;
        Item.useAnimation = 15;
        Item.useTime = 10;
        Item.useStyle = ItemUseStyleID.Swing;
        Item.consumable = true;
        Item.createTile = ModContent.TileType<HexSlate>();
        Item.rare = ItemRarityID.LightPurple;
        Item.value = Item.sellPrice(silver: 10);
    }

    /// <summary>原版 ItemSlate.canWrite：只收图案，也可以清空。</summary>
    public override bool CanWrite(Iota? datum) => datum is null or PatternIota;

    /// <summary>刻着的图案；空白石板是 null。</summary>
    public HexPattern? Pattern => (Read() as PatternIota)?.Pattern;

    /// <summary>同一堆只能是同一个图案（原版：数据不同的物品叠不到一起）。</summary>
    public override bool CanStack(Item source) => source.ModItem is HexSlateItem other && SamePattern(Pattern, other.Pattern);

    public override bool CanStackInWorld(Item source) => CanStack(source);

    private static bool SamePattern(HexPattern? a, HexPattern? b)
        => a is null ? b is null : b is not null && a.StartDir == b.StartDir && a.AnglesSignature() == b.AnglesSignature();

    /// <summary>原版 getName：block.hexcasting.slate.blank / written；提示框里画出图案（原版 PatternTooltip + 石板底图）。</summary>
    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        base.ModifyTooltips(tooltips);
        if (CustomName is null && Pattern is not null)
        {
            foreach (var line in tooltips)
            {
                if (line.Mod == "Terraria" && line.Name == "ItemName")
                {
                    line.Text = Language.GetTextValue("Mods.HexCastingTerraria.Items.HexSlateItem.WrittenName")
                                + (Item.stack > 1 ? $" ({Item.stack})" : string.Empty);
                }
            }
        }
        if (Pattern is not null)
        {
            // 占几行空白，在这块地方画出图案（同卷轴，见 ItemScroll.PostDrawTooltipLine）
            tooltips.Add(new TooltipLine(Mod, "HexSlatePattern", "　\n　\n　\n　"));
        }
    }

    public override void PostDrawTooltipLine(DrawableTooltipLine line)
    {
        if (line.Name != "HexSlatePattern" || Pattern is not { } p) return;
        const float size = 96f;
        var tl = new Microsoft.Xna.Framework.Vector2(line.X, line.Y);
        var bg = ModContent.Request<Microsoft.Xna.Framework.Graphics.Texture2D>(
            "HexCastingTerraria/Content/Items/States/SlateTooltip", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
        Main.spriteBatch.Draw(bg, tl, Microsoft.Xna.Framework.Color.White);
        Client.UI.PatternArt.DrawReadable(p, tl + new Microsoft.Xna.Framework.Vector2(size / 2f), size);
    }

    /// <summary>
    /// 放下时把物品上的图案写进方块（原版 BlockItem 放置时带上 BlockEntityTag）。
    /// 单机直接写；联机客户端把图案发给服务端（放置图格实体的消息先到，服务端按顺序处理）。
    /// </summary>
    internal static void ApplyToPlaced(int i, int j, Item item)
    {
        if (item.ModItem is not HexSlateItem { Pattern: { } pattern }) return;
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            Net.HexNetSync.RequestSlatePattern(i, j, pattern);
            return;
        }
        if (HexSlateEntity.FindAt(i, j) is { } entity)
        {
            entity.Pattern = pattern;
            entity.Sync();
        }
    }

    // 贴图：Content/Items/HexSlateItem.png（原版 item/slate_blank ×2）；有图案时由 ItemStateArt 换成 States/HexSlate.png 的第二格（slate_written）。
    // 注意 tModLoader 找不到类名同名 PNG 会禁用整个模组，而且专用服务器不报（贴图只在客户端加载）。

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:238-243，shaped：
        //     " A "
        //     "SSS"     A = 紫水晶粉 ×1，S = 深板岩 ×3  → 石板 ×6
        //
        // 泰拉没有深板岩，用**石块**（StoneBlock）代替 —— 同属「地下的基础石头」，
        // 而且泰拉的石块比 MC 的深板岩更易得，正好抵掉「没有深层/表层之分」这点差异。
        // 比例（3 石 + 1 粉 → 6 石板）与原版逐项一致，**没有**额外加价：
        // 石板在源项目里就是廉价消耗品（法术环要摆一堆），做贵了整个体系都推不动。
        CreateRecipe(6)
            .AddIngredient(ItemID.StoneBlock, 3)
            .AddIngredient<AmethystDust>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
