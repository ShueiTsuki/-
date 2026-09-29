using System.Collections.Generic;
using HexCastingTerraria.Core.Casting;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 探术透镜。对应源项目 `hexcasting:lens`（`ItemLens`，装备在**头部**装备位）。
///
/// ## 它做什么（读源码的结构得到的结论）
///
/// 原版把它做成一个**头部装备**：戴上之后能看到**别人施法时的图案预览**
/// （那边靠渲染层 `HexAdditionalRenderers` 给附近的施法实体画图案）。
/// 它不是法器、不影响施法本身，是一件**观察工具**。
///
/// ## 泰拉侧的映射
///
/// 泰拉没有"头部装备位"，但**有饰品栏** —— 语义完全对得上（戴上才生效的被动装备）。
/// 效果：戴着它时，附近其他玩家施法会在他们身上显出**瞄准点标记**，
/// 也就是我们联机时已经会广播的 `SpellVisual`。
///
/// ⚠️ 差别要说清楚：原版显示的是**对方画出来的图案**，我们显示的是**对方法术打向哪里**
/// （因为泰拉的施法瞄准就是鼠标方向，图案本身在各自的画布里）。
/// 这是 2D 适配里"信息等价、呈现不同"的一处，已记进对照表。
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
