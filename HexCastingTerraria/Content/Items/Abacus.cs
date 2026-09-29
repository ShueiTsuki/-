using System.Collections.Generic;
using HexCastingTerraria.Content.Tiles;
using HexCastingTerraria.Core.Casting.Iotas;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 阿卡夏记录的**物品形态**。
///
/// 原版 `akashic_record` 是可以采下来、搬走、再放下的（`blockItem(...)`），
/// 而我们之前只做了方块本体：挖掉之后**什么都没有**，也放不回去 ——
/// 玩家攒了一屋子记录，想挪个位置都做不到。
///
/// 这个缺口是「对照原版物品清单」时才发现的：方块存在 ≠ 内容完整。
///
/// ⚠️ 它**不含**存储内容：拆下来再放回去，里面的键值对会丢（内容在 TileEntity 上）。
/// 原版是同一个行为（掉落的是空记录），所以这里保持一致，不做特殊处理。
/// </summary>
public sealed class AkashicRecordItem : ModItem
{
    public override void SetDefaults()
    {
        Item.width = 16;
        Item.height = 16;
        Item.maxStack = 99;
        Item.useTurn = true;
        Item.autoReuse = true;
        Item.useAnimation = 15;
        Item.useTime = 10;
        Item.useStyle = ItemUseStyleID.Swing;
        Item.consumable = true;
        Item.createTile = ModContent.TileType<AkashicRecord>();
        Item.rare = ItemRarityID.LightPurple;
        Item.value = Item.sellPrice(silver: 20);
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:497-501：`brainsweep/akashic_record` ——
        // 把**阿卡夏系带** + 图书管理员村民（5 级）脑叶切除 → 阿卡夏记录方块。
        //
        // 阶段：**月后**。脑叶切除是启蒙大战法术之一，源项目这条也挂着 enlightenment 门槛。
        // 泰拉没有村民可切，所以用「系带 + 书」表达同一件事：系带提供阿卡夏的载体、
        // 书提供「记录」的语义。站用远古操控器，把门槛落在进度上而不是运气上。
        CreateRecipe()
            .AddIngredient<AkashicLigatureItem>(1)
            .AddIngredient(ItemID.Book, 3)
            .AddTile(TileID.LunarCraftingStation)
            .Register();
    }
}

/// <summary>
/// 算盘。对应源项目 `hexcasting:abacus`。
///
/// 它就是一个**只存一个数**的数据载体：`read` 读出来的是 double，
/// `write` 写进去也必须是数。原版里右键（潜行）会「摇一摇」随机换一个数。
///
/// 我们已经有能存任意 iota 的聚念核心（`Focus`），为什么还要它：
///   - 原版清单里有，属于「内容完整性」
///   - 它自带一个**随机数**动作（摇算盘），拿来测 `write` / `read` / 数值图案比
///     「先画一堆常数图案再写进核心」方便得多
///
/// 实现上直接复用 <see cref="ItemIotaStorage"/>，只把存储策略收紧到「只收 double」。
/// </summary>
public sealed class Abacus : ItemIotaStorage
{
    /// <summary>只收数值 —— 对应原版 `writeable` 里对 DoubleIota 的检查。</summary>
    public override StorageKind StorageKind => StorageKind.NumberOnly;

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 15;
        Item.useAnimation = 15;
        Item.UseSound = SoundID.Item4;
        Item.rare = ItemRarityID.Blue;
        Item.value = Item.sellPrice(silver: 15);
    }

    /// <summary>
    /// 右键摇一摇：写入一个 [0,1) 的随机数。
    ///
    /// 对应原版 `ItemAbacus.use`（潜行时清空、否则摇出一个新数）。
    /// 摇出一个**新数**而不是清空，是因为「清空」用 `erase` 图案就能做，
    /// 而「随机数」在测试里更常用。
    /// </summary>
    public override bool? UseItem(Player player)
    {
        if (player.whoAmI != Main.myPlayer) return true;

        TryStore(new DoubleIota(Main.rand.NextDouble()));

        Content.SpellSounds.Play("abacus.shake");
        Client.HexCanvasState.SetMessage($"算盘：{Stored}");
        return true;
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:156-163，shaped：
        //     WAW        W = 任意木板 ×4
        //     SAS        A = 紫水晶碎片 ×2   → 泰拉对应物 = ItemID.Amethyst（见 MediaConstants.ResourceMapping）
        //     WAW        S = 木棍 ×2         → 泰拉没有木棍，折进木材里（W4 + S2 = 木材 ×6）
        //
        // 阶段：肉前。源项目这条的解锁条件是「拥有任意法杖」，没有任何进度门槛。
        // 木材用原版配方组 `RecipeGroups.Wood`，这样橡木/北地木/红木……都能用。
        CreateRecipe()
            .AddRecipeGroup(RecipeGroups.Wood, 6)
            .AddIngredient(ItemID.Amethyst, 2)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
