using System.Collections.Generic;
using HexCastingTerraria.Core.Casting;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 探知透镜。对应源项目 `hexcasting:lens`（ItemLens）。
///
/// 原版效果（ItemLens.getDefaultAttributeModifiers：戴在头上 / 拿在任一只手 / 饰品栏）：
///   - SCRY_SIGHT：看方块时旁边列出信息（促动石的媒质和消息等）
///   - GRID_ZOOM ×1.33：咒术网格变细
/// 泰拉：饰品栏，或拿在手上 / 「另一只手」。效果见 Client/ScryingOverlay.cs。
/// （这里曾写成「看到别人施法的瞄准点」，原版透镜没有这个功能。）
/// </summary>
public sealed class ScryingLens : ModItem
{
    public override void SetDefaults()
    {
        Item.width = 24;
        Item.height = 24;
        Item.accessory = true;          // 饰品：戴上才生效
        Item.maxStack = 1;
        Item.rare = ItemRarityID.Orange;
        Item.value = Item.sellPrice(gold: 1);
    }

    public override void UpdateAccessory(Player player, bool hideVisual)
    {
        // 实际效果在 HexClientSystem 里读这个标记（纯表现，不做联机同步）
        player.GetModPlayer<HexPlayer>().ScryingLensEquipped = true;
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:153，`ringCornerless(SCRYING_LENS, 1, GLASS, AMETHYST_DUST)`：
        // 玻璃 ×4 + 紫水晶粉 ×1 → 1（cornerless 环 = 四条边的中点，共 4 格）。
        //
        // 阶段：肉前 —— 源项目的解锁条件是「拥有任意法杖」，没有任何进度门槛。
        // 站：源项目是徒手合成；本模组不允许徒手（check_arch 断言⑧），这是玻璃活，用工作台。
        CreateRecipe()
            .AddIngredient(ItemID.Glass, 4)
            .AddIngredient<AmethystDust>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
