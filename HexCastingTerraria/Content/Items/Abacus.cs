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
        // 阶段：**肉后 + 启蒙**。脑叶切除是启蒙大战法术之一，源项目这条也挂着 enlightenment 门槛。
        // 泰拉没有村民可切，所以用「系带 + 书」表达同一件事：系带提供阿卡夏的载体、
        // 书提供「记录」的语义。站用秘银砧（肉后），并要求已启蒙。
        CreateRecipe()
            .AddIngredient<AkashicLigatureItem>(1)
            .AddIngredient(ItemID.Book, 3)
            .AddTile(TileID.MythrilAnvil)       // 秘银砧 / 山铜砧（肉后）
            .AddCondition(HexConditions.Enlightened)
            .Register();
    }
}

/// <summary>
/// 算盘。对应源项目 `hexcasting:abacus`（ItemAbacus）。
///
/// **永远读得出一个数**（没设过就是 0），但**不能写**（writeable / canWrite 都是 false）——
/// 它是拿来「拨出一个数再 read」的，省得画数字图案。操作照原版：
///   - 潜行 + 滚轮：拿在手上 ±1（按住 Ctrl ±10）；放在「另一只手」（快捷栏里施法物品右边那格）±0.1（Ctrl ±0.01）
///     原版加速键是疾跑键，泰拉没有疾跑，用 Ctrl。滚轮向下是加（原版 `increase = delta < 0`）
///   - 潜行 + 右键：归零（原来是 69 的话提示「nice」）
/// 之前这里是「右键摇出一个随机数、可以 write 进去」—— 原版没有这两样。
/// </summary>
public sealed class Abacus : ItemIotaStorage
{
    public double Value { get; private set; }

    public override Iota? Read() => new DoubleIota(Value);

    public override bool Writeable => false;

    public override bool CanWrite(Iota? datum) => false;

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 15;
        Item.useAnimation = 15;
        Item.UseSound = null;
        Item.rare = ItemRarityID.Blue;
        Item.value = Item.sellPrice(silver: 15);
    }

    public override bool AltFunctionUse(Player player) => true;

    /// <summary>原版 use：只有潜行时归零（不潜行 → pass，什么都不做）。</summary>
    public override bool CanUseItem(Player player) => player.altFunctionUse == 2 && HexPlayer.ShiftHeld();

    public override bool? UseItem(Player player)
    {
        if (player.whoAmI != Main.myPlayer) return true;
        double old = Value;
        Value = 0;
        Item.NetStateChanged();
        Content.SpellSounds.Play("abacus.shake");
        Client.HexCanvasState.SetMessage(old == 69 ? "nice" : "重置为0");
        return true;
    }

    /// <summary>原版 MsgShiftScrollC2S.abacus：拨一下。<paramref name="notches"/> = 滚了几格（向下为正 = 加）。</summary>
    public void Scroll(int notches, bool mainHand, bool ctrl)
    {
        double step = mainHand ? (ctrl ? 10 : 1) : (ctrl ? 0.01 : 0.1);
        Value += notches * step;
        Item.NetStateChanged();
        Content.SpellSounds.Play("abacus");
        Client.HexCanvasState.SetMessage($"算盘：{new DoubleIota(Value)}");
    }

    public override void SaveData(Terraria.ModLoader.IO.TagCompound tag) => tag["value"] = Value;

    public override void LoadData(Terraria.ModLoader.IO.TagCompound tag) => Value = tag.GetDouble("value");

    public override void NetSend(System.IO.BinaryWriter writer) => writer.Write(Value);

    public override void NetReceive(System.IO.BinaryReader reader) => Value = reader.ReadDouble();

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
