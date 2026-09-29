using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Iotas;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 「数据载体」物品的基类：能存一个 iota。
/// 移植自源项目 `common/items/storage/ItemDataHolder`（focus / abacus / spellbook 等）。
///
/// ⚠️ **必须 maxStack = 1**：泰拉的堆叠物品共享一个 <see cref="Item"/> 实例，
/// 没法给同堆里的每一件存不同的 iota。可堆叠的 iota 载体在原理上就不成立。
/// </summary>
public abstract class ItemIotaStorage : ModItem
{
    /// <summary>
    /// 当前存着的 iota。null 表示空。
    ///
    /// 用**显式字段 + 只读属性**而不是自动属性：tModLoader 的
    /// 「引用字段可能在克隆间不安全」检查看的是字段，自动属性的字段是编译器生成的，
    /// 在上面标 `[CloneByReference]` 不生效。
    ///
    /// 之所以敢共享引用：iota 是**不可变**的（改值一律是造新的），
    /// 克隆物品时共享同一个实例完全安全 —— 那条警告对不可变对象是误报。
    /// </summary>
    [CloneByReference]
    private Iota? _stored;

    public Iota? Stored => _stored;

    /// <summary>是否只读（例如卷轴 —— 只能读不能改）。</summary>
    public virtual bool ReadOnlyStorage => false;

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
        var line = new TooltipLine(Mod, "HexStored",
            Stored == null ? "空" : $"存有：{Stored}");
        tooltips.Add(line);
    }

    /// <summary>
    /// 这个载体**能不能存**这种 iota。
    ///
    /// 对应源项目的 `canWrite(stack, datum)` —— 卷轴只接受图案
    /// （`datum instanceof PatternIota || datum == null`），
    /// 这样墙上挂的卷轴不会突然变成「存了一个数字的卷轴」。
    /// </summary>
    /// <summary>本载体的存储策略。子类覆写。</summary>
    public virtual StorageKind StorageKind => StorageKind.Any;

    public virtual bool CanStore(Iota value) => StoragePolicy.CanStore(StorageKind, value);

    /// <summary>写入一个 iota。只读载体、类型不符、都拒绝写入。</summary>
    public virtual bool TryStore(Iota value)
    {
        if (ReadOnlyStorage) return false;
        if (!CanStore(value)) return false;
        _stored = value;
        return true;
    }

    /// <summary>
    /// 读出存着的 iota。
    ///
    /// 声明成 virtual 是为了**法术书**：它有 64 页，Read() 要按当前页去取，
    /// 而不是读那个单一的 Stored 字段。
    /// </summary>
    public virtual Iota? Read() => Stored;

    /// <summary>
    /// 清空载体。对应源项目 `writeIota(null, false)`。
    /// 返回**原来是否有东西** —— 空载体上执行 `erase` 应当报 mishap，不是静默成功。
    /// </summary>
    public virtual bool Clear()
    {
        if (_stored == null) return false;
        _stored = null;
        return true;
    }

    public override void SaveData(TagCompound tag)
    {
        if (Stored == null) return;

        // Serialize() 返回的是「只含 TagCompound 原生类型的信封」，
        // 所以可以直接塞进 TagCompound —— 这正是 IotaSerializer 的设计目的。
        tag["iota"] = Stored.Serialize();
    }

    public override void LoadData(TagCompound tag)
    {
        if (!tag.ContainsKey("iota"))
        {
            _stored = null;
            return;
        }

        // 读不出来就当作空，**不是**静默降级成某个默认值 ——
        // 存档里放一个「看起来正常但内容变了」的 iota，比空着更难查。
        _stored = IotaSerializer.TryDeserialize(tag["iota"], out var iota) ? iota : null;
    }
}

/// <summary>
/// 聚念核心。对应源项目 `hexcasting:focus`。
///
/// 存**一个** iota，配合 `read_into_parens` 使用：
/// 把一段计算好的图案（或任何值）存进去，之后随时读出来复用。
/// 这是「写一次、反复用」的基础，也是原版玩家做自定义法术的第一步。
/// </summary>
public sealed class Focus : ItemIotaStorage
{
    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.Orange;
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:98-117，shaped（两个互为旋转的写法）：
        //     GLG        G = 萤石粉 ×4      → 泰拉对应物 = 坠落之星（同为「发光的基础魔法材料」）
        //     PAP        L = 皮革 ×2        → 折进丝绸
        //     GLG        P = 纸 ×2          → 泰拉没有纸，用丝绸
        //                A = 充能紫水晶 ×1
        //
        // 阶段：肉前 —— 源项目的解锁条件是「拥有任意法杖」，没有进度门槛。
        // 之前这里是「充能紫晶 ×2 + 紫晶 ×5 + 玻璃 ×3」，材料与原版毫无对应关系。
        CreateRecipe()
            .AddIngredient(ItemID.FallenStar, 4)        // 萤石粉
            .AddIngredient(ItemID.Silk, 4)              // 皮革 ×2 + 纸 ×2
            .AddIngredient<ChargedAmethyst>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>
/// 念珠。对应源项目 `hexcasting:thought_knot`。
///
/// 原版里它存的是**一小段图案列表**（用来把常用咒术随身带着）。
/// 泰拉侧先做成「只读的单 iota 载体」—— 写入能力等
/// 阿卡夏记录（P2-4）落地后再补，因为原版的念珠内容是由记录赋予的。
/// </summary>
public sealed class ThoughtKnot : ItemIotaStorage
{
    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.LightRed;
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:93-97，**shapeless 无合成站**：
        //     紫水晶粉 ×1 + 线 ×1 → 1
        //
        // 阶段：肉前（解锁条件同样是「拥有任意法杖」）。
        // 站：源项目是徒手；本模组不允许徒手（check_arch 断言⑧）。
        // 念珠是线绳活，用**织布机**（丝绸就是它产的），语义最贴。
        //
        // 之前这里写的是「聚念核心 ×1 + 丝绸 ×5 + 粉 ×20」—— 比原版贵 20 倍以上，
        // 而且把一个「随手搓的小玩意」变成了必须先把聚念核心做出来才能做的东西。
        CreateRecipe()
            .AddIngredient<AmethystDust>(1)
            .AddIngredient(ItemID.Silk, 1)
            .AddTile(TileID.Loom)
            .Register();
    }
}
