using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Iotas;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 「数据载体」物品的基类：能存一个 iota。
/// 移植自源项目 `api/item/IotaHolderItem`（核心 / 结念绳 / 卷轴 / 法术书 / 算盘）。
///
/// 规则照原版接口，一条都不自己发明：
///   - <see cref="Read"/>      = readIota：存着的东西，空就是 null（原版没有哪个物品定义 emptyIota，空载体 read 报 mishap）
///   - <see cref="Writeable"/> = writeable()：`writable` 图案问的就是它
///   - <see cref="CanWrite"/>  = canWrite(datum)：肯不肯收；datum = null 表示清除
///   - <see cref="WriteIota"/> = writeIota(datum, simulate)：先 canWrite，不是试算才真写
///
/// 注意：**必须 maxStack = 1**：泰拉的堆叠物品共享一个 <see cref="Item"/> 实例，
/// 没法给同堆里的每一件存不同的 iota。
/// </summary>
public abstract class ItemIotaStorage : ModItem
{
    /// <summary>
    /// 当前存着的 iota。null 表示空。
    /// 显式字段 + [CloneByReference]：iota 不可变，克隆物品时共享同一个实例是安全的。
    /// </summary>
    [CloneByReference]
    private Iota? _stored;

    public Iota? Stored => _stored;

    protected void SetStored(Iota? value) => _stored = value;

    /// <summary>readIota。法术书按当前页读、算盘永远读得出一个数、远古卷轴读本世界的笔顺。</summary>
    public virtual Iota? Read() => _stored;

    /// <summary>writeable()。</summary>
    public virtual bool Writeable => true;

    /// <summary>canWrite(datum)。null = 清除。</summary>
    public virtual bool CanWrite(Iota? datum) => true;

    /// <summary>writeDatum(datum)：只在 <see cref="CanWrite"/> 通过后调用。</summary>
    protected virtual void WriteDatum(Iota? datum) => _stored = datum;

    /// <summary>原版 writeIota(datum, simulate)。</summary>
    public bool WriteIota(Iota? datum, bool simulate)
    {
        if (!CanWrite(datum)) return false;
        if (!simulate) WriteDatum(datum);
        return true;
    }

    public override void SetDefaults()
    {
        Item.width = 20;
        Item.height = 20;
        Item.maxStack = 1;              // 见类型注释：可堆叠就存不住 iota
        Item.consumable = false;
        Item.useStyle = ItemUseStyleID.None;
        Item.autoReuse = false;
        Item.noMelee = true;
        Item.rare = ItemRarityID.LightPurple;
        Item.value = Item.sellPrice(gold: 1);
    }

    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        // 原版 IotaHolderItem.appendHoverText：有内容就显示内容，空就不写
        if (Read() is { } iota)
        {
            tooltips.Add(new TooltipLine(Mod, "HexStored", $"存有：{iota}"));
        }
    }

    public override void SaveData(TagCompound tag)
    {
        if (_stored == null) return;
        tag["iota"] = Net.IotaTag.ToTag(_stored);
    }

    public override void LoadData(TagCompound tag)
    {
        // 读不出来就当作空，**不是**静默降级成某个默认值
        _stored = tag.ContainsKey("iota") && Net.IotaTag.TryFromTag(tag["iota"], out var iota) ? iota : null;
    }

    // ── 联机 ───────────────────────────────────────────────────────
    //
    // 背包同步（MessageID.SyncEquipment）只带 NetSend 写的东西。之前没写 ——
    // 联机时服务端手里的核心永远是空的：`read` 读出 null，`write` 写了客户端也看不到。
    // 客户端这边改了内容（翻页、拨算盘、收到服务端的写入）要调 Item.NetStateChanged()，
    // 否则泰拉只比较「类型 / 数量 / 前缀」，认为没变，不会同步。

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        writer.Write(_stored != null);
        if (_stored != null) Net.IotaWire.Write(writer, _stored);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        _stored = reader.ReadBoolean() ? Net.IotaWire.Read(reader) : null;
    }
}

/// <summary>
/// 有「外观变体」的物品（源项目 VariantItem，`cycle_variant` 改的就是它）：
/// 符纸 / 缀品 / 造物、核心、法术书。封了的核心 / 书页不变（原版 setVariant 里的 `if (!isSealed)`）。
/// </summary>
public interface IHexVariantItem
{
    int Variant { get; }

    int NumVariants { get; }

    void SetVariant(int variant);
}

/// <summary>
/// 核心。对应源项目 `hexcasting:focus`（ItemFocus）。
///
/// 存**一个任意** iota。可以**密封**（和蜂巢合成，原版是蜜脾）：密封后写不进，
/// 但 `erase` 照样能清 —— 清掉的同时解封（原版 writeDatum(null) 连 TAG_SEALED 一起删）。
/// </summary>
public sealed class Focus : ItemIotaStorage, IHexVariantItem
{
    public const int Variants = 8;

    public bool Sealed { get; private set; }

    public int Variant { get; private set; }

    public int NumVariants => Variants;

    public void SetVariant(int variant)
    {
        if (!Sealed) Variant = System.Math.Clamp(variant, 0, Variants - 1);
    }

    public void Seal() => Sealed = true;

    public override bool Writeable => !Sealed;

    public override bool CanWrite(Iota? datum) => datum == null || !Sealed;

    protected override void WriteDatum(Iota? datum)
    {
        if (datum == null)
        {
            SetStored(null);
            Sealed = false;
        }
        else if (!Sealed)
        {
            SetStored(datum);
        }
    }

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.Orange;
    }

    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        if (Sealed)
        {
            foreach (var t in tooltips)
            {
                if (t.Name == "ItemName") t.Text = "密封核心";
            }
        }
        base.ModifyTooltips(tooltips);
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:98-117，shaped（两个互为旋转的写法）：
        //     GLG        G = 萤石粉 ×4      → 泰拉对应物 = 坠落之星（同为「发光的基础魔法材料」）
        //     PAP        L = 皮革 ×2        → 折进丝绸
        //     GLG        P = 纸 ×2          → 泰拉没有纸，用丝绸
        //                A = 充能紫水晶 ×1
        CreateRecipe()
            .AddIngredient(ItemID.FallenStar, 4)        // 萤石粉
            .AddIngredient(ItemID.Silk, 4)              // 皮革 ×2 + 纸 ×2
            .AddIngredient<ChargedAmethyst>(1)
            .AddTile(TileID.WorkBenches)
            .Register();

        SealRecipes.Add(this, f => f is Focus { Sealed: false } focus && focus.Stored != null,
            f => ((Focus)f).Seal());
    }

    public override void SaveData(TagCompound tag)
    {
        base.SaveData(tag);
        tag["sealed"] = Sealed;
        tag["variant"] = Variant;
    }

    public override void LoadData(TagCompound tag)
    {
        base.LoadData(tag);
        Sealed = tag.GetBool("sealed");
        Variant = System.Math.Clamp(tag.GetInt("variant"), 0, Variants - 1);
    }

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        base.NetSend(writer);
        writer.Write(Sealed);
        writer.Write((byte)Variant);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        base.NetReceive(reader);
        Sealed = reader.ReadBoolean();
        Variant = System.Math.Clamp((int)reader.ReadByte(), 0, Variants - 1);
    }
}

/// <summary>
/// 结念绳。对应源项目 `hexcasting:thought_knot`（ItemThoughtKnot）。
///
/// **只能写一次**：空的时候可写（writeable = 没存东西），写进去以后就固定了，
/// `erase` 也清不掉（canWrite(null) = false）。原版注释：「本想直接往绳子上写，但 API 要求写完还是同一个物品」。
/// </summary>
public sealed class ThoughtKnot : ItemIotaStorage
{
    public override bool Writeable => Stored == null;

    public override bool CanWrite(Iota? datum) => datum != null && Writeable;

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.LightRed;
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:93-97，**shapeless 无合成站**：紫水晶粉 ×1 + 线 ×1 → 1
        // 站：本模组不允许徒手（check_arch 断言⑧），念珠是线绳活，用织布机。
        CreateRecipe()
            .AddIngredient<AmethystDust>(1)
            .AddIngredient(ItemID.Silk, 1)
            .AddTile(TileID.Loom)
            .Register();
    }
}

/// <summary>
/// 原版 SealThingsRecipe（核心 / 法术书 + 蜜脾 → 密封）。
/// 泰拉没有蜜脾，用**蜂巢块**（地下丛林蜂巢里挖的，语义就是一块蜜脾）。
///
/// 泰拉配方没有「这一件物品满足条件」的写法：用条件检查背包里**会被消耗的那一件**
///（配方按背包顺序取第一个同类物品），合成完把它的数据拷到产物上再封。
/// </summary>
internal static class SealRecipes
{
    public static void Add(ModItem self, System.Func<ModItem, bool> sealable, System.Action<ModItem> seal)
    {
        int type = self.Type;
        var cond = new Condition(
            Terraria.Localization.Language.GetOrRegister("Mods.HexCastingTerraria.Conditions.Sealable"),
            () => FirstOf(Main.LocalPlayer, type) is { ModItem: { } m } && sealable(m));

        self.CreateRecipe()
            .AddIngredient(type, 1)
            .AddIngredient(ItemID.Hive, 1)
            .AddTile(TileID.WorkBenches)        // 原版 2×2 背包格就能合；本模组配方一律要合成站（check_arch ⑧）
            .AddCondition(cond)
            .AddOnCraftCallback((recipe, result, consumed, dest) =>
            {
                // 整件数据（iota、页、变体……）拷到产物上，再封
                if (consumed.Find(i => i.type == type)?.ModItem is not { } src || result.ModItem is null) return;
                var tag = new TagCompound();
                src.SaveData(tag);
                result.ModItem.LoadData(tag);
                seal(result.ModItem);
            })
            .Register();
    }

    private static Item? FirstOf(Player p, int type)
    {
        for (int i = 0; i < 58; i++)
        {
            if (p.inventory[i] is { IsAir: false } it && it.type == type) return it;
        }
        return null;
    }
}
