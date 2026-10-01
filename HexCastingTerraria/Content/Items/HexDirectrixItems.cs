using HexCastingTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 导线的公共物品基类。对应源项目三根 `*Directrix` 的物品形态。
///
/// 放下后成为法术环的**分流器**：只能沿一个轴传导，
/// 从垂直于轴的方向进入。空手右键转轴。
/// </summary>
public abstract class HexDirectrixItemBase : ModItem
{
    /// <summary>对应的方块类型。</summary>
    public abstract int TileType { get; }

    /// <summary>
    /// 复用方块贴图。
    ///
    /// 注意：不写这一行会被 tModLoader 判为缺资源（它按**类名**找同名 PNG），
    /// 并**禁用整个模组**。而这个错误**专用服务器不会报** ——
    /// 贴图只在客户端加载，所以「服务端能加载」证明不了客户端能加载。
    /// 审计脚本：`_tools/check_assets.ps1`。
    ///
    /// 贴图路径由子类的方块类型决定，所以这里用虚属性，子类各给一条。
    /// </summary>
    public override string Texture => $"HexCastingTerraria/Content/Items/Blocks/{TextureName}";

    /// <summary>方块贴图的文件名（不含扩展名）。子类覆写。</summary>
    protected abstract string TextureName { get; }

    public override void SetDefaults()
    {
        Item.width = 16;
        Item.height = 16;
        Item.maxStack = 999;
        Item.useTurn = true;
        Item.autoReuse = true;
        Item.useAnimation = 15;
        Item.useTime = 10;
        Item.useStyle = ItemUseStyleID.Swing;
        Item.consumable = true;
        Item.createTile = TileType;
        Item.rare = ItemRarityID.LightPurple;
        Item.value = Item.sellPrice(silver: 15);
    }
}

/// <summary>
/// 空导线：**随机**出轴的一端。
///
/// 源项目用它做「不可预测的分支」—— 配合循环可以做出概率行为。
/// </summary>
public sealed class HexDirectrixEmptyItem : HexDirectrixItemBase
{
    public override int TileType => ModContent.TileType<HexDirectrixEmpty>();
    protected override string TextureName => "HexDirectrixEmpty";

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:400-408，shaped：
        //     CSS        C = 红石比较器 ×2
        //     OAO        O = 观察者 ×2
        //     SSC        A = 充能紫水晶 ×1
        //                S = 板岩块 ×4
        //
        // 阶段：肉后 + 启蒙（源挂 enlightenment 门槛，见上面的说明）。
        //
        // C/O 这两个「红石逻辑件」在泰拉的对应物是逻辑门/逻辑传感器那一族，
        // 但那一族各自还有自己的获取链，直接当成材料会让这条配方变得**不可控**
        // （读者无法一眼判断它到底要什么）。泰拉红石的本体就是**电线**
        // （机械师 NPC 出售），所以这里用电线数量代表那两个逻辑件，
        // 并在注释里如实说明 —— 这比塞两个来路不明的 ID 诚实。
        CreateRecipe()
            .AddIngredient<SlateBlockItem>(4)           // 板岩块 ×4
            .AddIngredient(ItemID.Wire, 20)             // 比较器 ×2 + 观察者 ×2 → 电线 ×20
            .AddIngredient<ChargedAmethyst>(1)
            .AddTile(TileID.MythrilAnvil)       // 秘银砧 / 山铜砧（肉后）
            .AddCondition(HexConditions.Enlightened)
            .Register();
    }
}

/// <summary>
/// 布尔导线：弹栈顶布尔值决定出口（真出 `Facing` 的反方向、假出 `Facing`）。
///
/// 这是法术环里**唯一的分支结构** —— 有了它才能写出条件逻辑。
/// </summary>
public sealed class HexDirectrixBooleanItem : HexDirectrixItemBase
{
    public override int TileType => ModContent.TileType<HexDirectrixBoolean>();
    protected override string TextureName => "HexDirectrixBoolean";

    // 没有合成配方：原版只能对放在世界里的空白导向石剥离意识（牧羊人，移植版对应染料商）得到，见 HexCastingTerraria.ConfigureBrainsweepRecipes。
    // 这里曾经另给了「空白导向石 + 钻石」的合成，2026-10-01 照原版去掉。
}

/// <summary>
/// 红石导线：通电出 `Facing`，否则出反方向。
///
/// 注意：泰拉不像 MC 那样能直接查「这格当前是否通电」（电线信号是瞬时的），
/// 所以用「收到 `HitWire` 后保持若干 tick」来近似 —— 足以让环在一次传导中看到稳定电平。
/// </summary>
public sealed class HexDirectrixRedstoneItem : HexDirectrixItemBase
{
    public override int TileType => ModContent.TileType<HexDirectrixRedstone>();
    protected override string TextureName => "HexDirectrixRedstone";

    // 没有合成配方：原版只能对放在世界里的空白导向石剥离意识（石匠，移植版对应爆破专家）得到。
    // 这里曾经另给了「空白导向石 + 红宝石」的合成，2026-10-01 照原版去掉。
}
