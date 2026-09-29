using HexCastingTerraria.Client;
using HexCastingTerraria.Content;
using HexCastingTerraria.Core.Media;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 媒质瓶：把宝石蓄入自身的媒质池。
///
/// 对应源项目的媒质来源物品（紫水晶粉 / 充能紫水晶 / 淬灵晶簇），
/// 它们都是「可提供媒质的容器」。泰拉侧没有紫水晶簇，用紫晶（Amethyst）作为对应物。
///
/// 数值对齐源项目 MediaConstants：
///   1 紫水晶粉 = 10,000
///   1 充能紫水晶 = 100,000（= 1 晶体）
/// 因此一个媒质瓶按「1 晶体」计，回复 100,000。
/// </summary>
public class MediaFlask : ModItem
{
    /// <summary>单次回复的媒质量（1 晶体 = 10 粉）。没被 `craft/battery` 改过时的默认值。</summary>
    public const long RestoreAmount = MediaConstants.CrystalUnit;

    /// <summary>
    /// 这个瓶子里实际装了多少媒质。
    ///
    /// 为什么要可变：`craft/battery` 是「把地上那份媒质装进瓶子」，
    /// 装多少就该是多少（源项目 `withMedia(stack, mediamount, mediamount)`）。
    /// 固定成 1 晶体的话，往地上放 3 个充能紫水晶再装瓶会白白亏掉 2/3。
    /// </summary>
    public long StoredMedia { get; private set; } = RestoreAmount;

    public void SetStoredMedia(long media) => StoredMedia = System.Math.Max(0, media);

    public override void ModifyTooltips(System.Collections.Generic.List<TooltipLine> tooltips)
    {
        tooltips.Add(new TooltipLine(Mod, "HexFlaskMedia",
            $"蕴含媒质：{MediaConstants.Format(StoredMedia)}"));
    }

    public override void SaveData(Terraria.ModLoader.IO.TagCompound tag) => tag["media"] = StoredMedia;

    public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
    {
        StoredMedia = tag.TryGet("media", out long media) ? System.Math.Max(0, media) : RestoreAmount;
    }

    public override void SetDefaults()
    {
        Item.width = 20;
        Item.height = 20;
        Item.useStyle = ItemUseStyleID.DrinkLiquid;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.useTurn = true;
        Item.autoReuse = false;
        Item.maxStack = 99;
        Item.consumable = true;
        Item.value = Item.buyPrice(silver: 20);
        Item.rare = ItemRarityID.LightPurple;
        Item.UseSound = SoundID.Item3;
        Item.noMelee = true;
    }

    public override bool? UseItem(Player player)
    {
        var hexPlayer = HexPlayer.Get(player);

        long before = hexPlayer.Media;
        long gained = hexPlayer.MediaStorage.Insert(StoredMedia);
        long after = hexPlayer.Media;

        if (player.whoAmI == Main.myPlayer)
        {
            if (gained <= 0)
            {
                HexCanvasState.SetMessage(
                    $"媒质已满：{MediaConstants.Format(after)} / {MediaConstants.Format(hexPlayer.MaxMedia)}");
                // 满了就不消耗物品
                return false;
            }

            HexCanvasState.SetMessage(
                $"+{MediaConstants.Format(gained)} 媒质  →  {MediaConstants.Format(after)} / {MediaConstants.Format(hexPlayer.MaxMedia)}");
        }

        return true;
    }

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient(ItemID.Bottle, 1)
            .AddIngredient(ItemID.Amethyst, 1)
            .AddTile(TileID.Bottles)
            .Register();
    }
}
