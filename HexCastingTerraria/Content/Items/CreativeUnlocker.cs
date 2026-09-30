using HexCastingTerraria.Core.Ui;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 媒质立方（原版 ItemCreativeUnlocker，创造模式物品，没有配方）。
///
///   - 放在背包里就是**无限媒质**：原版 MediaHolderItem，getMedia = Long.MAX_VALUE、扣多少都不少（优先级同媒质瓶）
///   - 能塞进促动石：原动力的媒质变成无限（原版 insertMedia 里的 CREATIVE_UNLOCKER 分支，立方被消耗）
///   - **可以吃**（原版 alwaysEat 的食物）：吃了授予咒法学的全部进度 —— 书全解锁、传说全读、启蒙；
///     吃完立方还在（原版 finishUsingItem 返回原物）
///   - 拿到它本身就是一个进度（原版 advancement creative_unlocker「无尽能量！」，书里对应的条目靠它解锁）
///
/// 泰拉没有创造物品栏：开发者面板的测试包里给一个；旅途模式研究过一个之后可以无限复制。
/// </summary>
public sealed class CreativeUnlocker : ModItem
{
    public override void SetStaticDefaults() => Item.ResearchUnlockCount = 1;

    public override void SetDefaults()
    {
        Item.width = 24;
        Item.height = 24;
        Item.maxStack = 1;
        Item.rare = ItemRarityID.Purple;             // 原版 Rarity.EPIC
        Item.useStyle = ItemUseStyleID.EatFood;
        Item.useTime = 32;                            // 原版吃东西 32 刻；这里按泰拉食物的节奏
        Item.useAnimation = 32;
        Item.UseSound = SoundID.Item2;
        Item.consumable = false;
    }

    public override void UpdateInventory(Player player) => HexPlayer.Get(player).FoundMediaCube = true;

    public override bool? UseItem(Player player)
    {
        if (player.whoAmI != Main.myPlayer) return true;

        // 原版：root 与 lore 两棵进度树的全部节点（root 之下有盲目绘制 → 睁开双眼 → 启蒙，还有「无尽能量！」本身）
        var hp = HexPlayer.Get(player);
        hp.ObtainedAmethyst = true;
        hp.FailedGreatSpell = true;
        hp.Overcasted = true;
        hp.FoundMediaCube = true;
        foreach (var lore in BookUnlocks.LoreIds) { hp.FoundLore.Add(lore); }
        hp.GrantEnlightenment();
        SoundEngine.PlaySound(SoundID.Item4, player.Center);
        return true;
    }
}
