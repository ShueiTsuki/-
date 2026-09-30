using System.Collections.Generic;
using HexCastingTerraria.Core;
using HexCastingTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 开发者测试包：一次把每个子系统的代表性物品各发一份。
///
/// ## 为什么需要它
///
/// 咒法学有 188 条图案、几十种物品、多套子系统（媒质链 / 数据载体 / 卷轴 /
/// 打包法术 / 法术环三方块 / 脑叶切除原料…）。
/// 靠正常玩法一件件凑齐，光「合成出第一块石板」就要先挖矿砍树 ——
/// 那不是测试机制，那是在玩游戏。
///
/// 所以这里提供一个**覆盖全部子系统**的最小集合：
/// 每样东西只要一份，够你把对应机制跑一遍。
///
/// 发放时机由配置决定（`GiveDevKitOnEnter`），也可以随时按 K 手动再发一次。
/// </summary>
internal static class DevKit
{
    /// <summary>把测试包发进玩家背包（放不下就掉在脚下，不会凭空消失）。</summary>
    public static void Give(Player player)
    {
        foreach (int type in ItemTypes())
        {
            GiveOne(player, type);
        }

        // 媒质也给足：一个 10 晶体的满媒质瓶（媒质只来自背包物品，与原版一致）
        var flask = new Item(ModContent.ItemType<MediaFlask>());
        (flask.ModItem as MediaFlask)!.SetMedia(10 * Core.Media.MediaConstants.CrystalUnit, 10 * Core.Media.MediaConstants.CrystalUnit);
        player.QuickSpawnItem(player.GetSource_Misc("HexDevKit"), flask);
    }

    /// <summary>测试包的内容。新增子系统时**记得往这里加一件**代表物。</summary>
    private static IEnumerable<int> ItemTypes()
    {
        // 施法媒介与引导
        yield return ModContent.ItemType<DevStaff>();
        yield return ModContent.ItemType<HexBookItem>();

        // 媒质链（粉 → 碎片 → 充能 → 淬灵）+ 媒质瓶
        yield return ModContent.ItemType<AmethystDust>();
        yield return ModContent.ItemType<AmethystShard>();
        yield return ModContent.ItemType<ChargedAmethyst>();
        yield return ModContent.ItemType<QuenchedAllayShard>();
        yield return ModContent.ItemType<CreativeUnlocker>();   // 媒质立方（原版的创造模式物品）

        // 颜料（内化染色剂用）：单色、多色渐变、灵魂闪光各一
        yield return ModContent.ItemType<PigmentDyeRed>();
        yield return ModContent.ItemType<PigmentPrideTransgender>();
        yield return ModContent.ItemType<PigmentSoulglimmer>();

        // 数据载体（读写图案的三种）
        yield return ModContent.ItemType<Focus>();
        yield return ModContent.ItemType<ThoughtKnot>();
        yield return ModContent.ItemType<Abacus>();
        yield return ModContent.ItemType<Spellbook>();

        // 珠宝匠锤与探术透镜（工具类）
        yield return ModContent.ItemType<JewelerHammer>();
        yield return ModContent.ItemType<ScryingLens>();
        yield return ModContent.ItemType<ScrollSmall>();
        yield return ModContent.ItemType<ScrollMedium>();
        yield return ModContent.ItemType<ScrollLarge>();

        // 打包法术三件套
        yield return ModContent.ItemType<Cypher>();
        yield return ModContent.ItemType<Trinket>();
        yield return ModContent.ItemType<Artifact>();

        // 法术环三方块（普通石板 / 原动力 / 导线三种）
        yield return ModContent.ItemType<HexSlateItem>();
        yield return ModContent.ItemType<HexImpetusItem>();
        yield return ModContent.ItemType<HexDirectrixEmptyItem>();
        yield return ModContent.ItemType<HexDirectrixBooleanItem>();
        yield return ModContent.ItemType<HexDirectrixRedstoneItem>();

        // 阿卡夏记录（用它测 storage 类图案）
        yield return ModContent.ItemType<AkashicRecordItem>();

        // 建材与装饰各一份（测放置、油漆兼容、壁挂卷轴流程）
        yield return ModContent.ItemType<SlateBlockItem>();
        yield return ModContent.ItemType<EdifiedPlanksItem>();
        yield return ModContent.ItemType<AmethystTilesItem>();
        yield return ModContent.ItemType<QuenchedAllayItem>();
        yield return ModContent.ItemType<WallScrollFrameMedium>();
        yield return ModContent.ItemType<ScrollPaperItem>();
        yield return ModContent.ItemType<AmethystSconceItem>();

        // 原版材料：合成配方里用到的那几样
        yield return ItemID.Bottle;
        yield return ItemID.Wood;
        yield return ItemID.Hay;
        yield return ItemID.Silk;
        yield return ItemID.Amethyst;
        yield return ItemID.Torch;
    }

    /// <summary>
    /// 发一件（数量按物品类型决定）到背包。
    ///
    /// 用 `QuickSpawnItem` 而不是直接塞 `inventory[i]`：
    /// 它会处理「背包满了掉在地上」，而不是把物品**静默丢弃**。
    /// </summary>
    private static void GiveOne(Player player, int type)
    {
        int stack = type switch
        {
            var t when t == ItemID.Wood => 200,
            var t when t == ItemID.Hay => 200,
            var t when t == ItemID.Silk => 50,
            var t when t == ItemID.Torch => 50,
            var t when t == ModContent.ItemType<AmethystDust>() => 100,
            var t when t == ModContent.ItemType<HexSlateItem>() => 30,
            var t when t == ModContent.ItemType<HexImpetusItem>() => 4,
            _ => 1,
        };

        player.QuickSpawnItem(player.GetSource_Misc("HexDevKit"), type, stack);
    }
}
