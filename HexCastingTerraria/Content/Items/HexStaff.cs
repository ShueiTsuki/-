using HexCastingTerraria.Client;
using HexCastingTerraria.Content.Tiles;
using HexCastingTerraria.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using HexCastingTerraria.Config;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 法杖基类。移植自源项目 `common/items/ItemStaff.java`。
///
/// 行为：
///   - 右键 → 打开咒术绘制界面（泰拉侧是我们的画布）
///   - 潜行 + 右键 → 清空已保存的图案 / 施法数据
///   - 施法者若有 FEEBLE_MIND 属性则无法使用（泰拉侧暂无属性系统）
///
/// 原版有 14 种法杖，功能**完全相同**，只是外观与合成材料不同 ——
/// 所以这里把行为全部收在基类，子类只需要提供默认值。
/// </summary>
public abstract class HexStaff : ModItem
{
    /// <summary>法杖的稀有度。基类给个默认，特殊法杖可覆写。</summary>
    public virtual int StaffRarity => ItemRarityID.Blue;

    /// <summary>基础伤害。原版法杖伤害为 0（它不是武器，只是施法媒介）。</summary>
    public virtual int StaffDamage => 0;

    public override void SetDefaults()
    {
        Item.width = 16;
        Item.height = 16;
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.autoReuse = false;
        Item.useTurn = true;
        Item.maxStack = 1;
        Item.consumable = false;
        Item.noMelee = true;
        Item.DamageType = DamageClass.Magic;
        Item.damage = StaffDamage;
        Item.rare = StaffRarity;
        Item.UseSound = null;   // 开画布本身不该有音效；施法音效由 VM 的 EvalSound 负责
        Item.value = Item.sellPrice(silver: 40);
    }

    public override bool AltFunctionUse(Player player) => true;

    /// <summary>
    /// 打开画布。
    ///
    /// ⚠️ 这里**只负责打开**，关闭交给 <see cref="HexClientSystem.PostUpdateInput"/> 的右键处理。
    /// 两边都处理会双触发（同一次右键既开又关，表现成闪烁/无响应）。
    /// </summary>
    public static void OpenCanvas()
    {
        var canvas = HexCanvasState.Canvas;

        // 把设置里的画布参数应用到画布上（用户可在 设置 → 模组配置 里改）
        var config = HexClientConfig.Instance;
        // 原版 GRID_ZOOM：探知透镜（戴着 / 拿在任一只手）×1.33，网格变细
        canvas.Zoom = config.GridZoom * (ScryingOverlay.HasSight(Main.LocalPlayer) ? ScryingOverlay.GridZoom : 1f);
        canvas.SnapThreshold = config.GridSnapThreshold;
        canvas.StrokeScale = config.StrokeScale;
        canvas.WobbleScale = config.WobbleScale;

        HexCanvasState.OpenCanvas();
    }

    /// <summary>潜行 + 右键：清空图案与 VM 栈，重新开始。对应源项目 clearCastingData。</summary>
    public static void ClearAndOpen()
    {
        HexCanvasState.Canvas.Reset();
        HexVmState.Reset();
        HexCanvasState.OpenCanvas();
        HexCanvasState.SetMessage("已清空咒术图案，重新开始");
        Content.SpellSounds.Play("staff.reset");
    }

    public override bool? UseItem(Player player)
    {
        // 画布是纯客户端 UI，服务端不做任何事
        if (player.whoAmI != Main.myPlayer)
        {
            return true;
        }

        // 潜行判定：用真实按键状态而不是 player.controlDown。
        // 因为画布打开时我们把 Main.blockInput 置为 true，玩家的控制状态可能不更新，
        // 读 controlDown 会一直为 false，导致「潜行右键清空」永远触发不了。
        bool shiftHeld =
            Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) ||
            Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift);

        if (shiftHeld)
        {
            ClearAndOpen();
            return true;
        }

        if (!HexCanvasState.Canvas.IsOpen)
        {
            OpenCanvas();
        }

        return true;
    }
}

/// <summary>木材类法杖的公共实现 —— 原版 14 种里的大多数只是换了个木材。</summary>
public abstract class WoodStaff : HexStaff
{
    /// <summary>合成用的「木板」物品 ID。对应源项目里各家 `*_PLANKS`。</summary>
    public abstract int WoodType { get; }

    /// <summary>
    /// 对应源项目 `HexplatRecipes.staffRecipe(...)`，3x3 形状：
    /// <code>
    ///  SA        S = 木棍 ×3
    ///  WS        W = 对应木板 ×1
    /// S         A = 充能紫水晶 ×1
    /// </code>
    ///
    /// ⚠️ **泰拉没有「木棍」物品**（MC 的 Stick 在泰拉没有对应物），
    /// 用 `ItemID.Wood` 代替 —— 它是泰拉里语义最接近的基础材料。
    /// 这是本配方唯一的替换，其余（木板 1、充能紫水晶 1）与原版逐项一致。
    ///
    /// 改成这样之前的写法是「20 木材 + 1 紫晶」，两处都不对：
    /// 数量差 5 倍，而且原版要的是**充能紫水晶**（100k 媒质）不是普通紫晶。
    /// </summary>
    public override void AddRecipes()
    {
        CreateRecipe()
            .AddRecipeGroup(RecipeGroups.Wood, 3)            // 木棍 ×3（泰拉替代物）
            .AddIngredient(WoodType, 1)               // 对应木板 ×1
            .AddIngredient<ChargedAmethyst>(1)        // 充能紫水晶 ×1
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

// ── 以下 10 种对应泰拉各木材（原版是 MC 的各木材）──────────────────

/// <summary>橡木法杖。对应源项目 `staff/oak`。</summary>
public sealed class OakStaff : WoodStaff
{
    public override int WoodType => ItemID.Wood;
}

/// <summary>北地木法杖。对应源项目 `staff/spruce`。</summary>
public sealed class BorealStaff : WoodStaff
{
    public override int WoodType => ItemID.BorealWood;
}

/// <summary>棕榈木法杖。对应源项目 `staff/bamboo`。</summary>
public sealed class PalmStaff : WoodStaff
{
    public override int WoodType => ItemID.PalmWood;
}

/// <summary>红木法杖。对应源项目 `staff/jungle`。</summary>
public sealed class MahoganyStaff : WoodStaff
{
    public override int WoodType => ItemID.RichMahogany;
}

/// <summary>乌木法杖。对应源项目 `staff/dark_oak`。</summary>
public sealed class EbonwoodStaff : WoodStaff
{
    public override int WoodType => ItemID.Ebonwood;
}

/// <summary>阴影木法杖。对应源项目 `staff/crimson`。</summary>
public sealed class ShadewoodStaff : WoodStaff
{
    public override int WoodType => ItemID.Shadewood;
}

/// <summary>珍珠木法杖。对应源项目 `staff/birch`。</summary>
public sealed class PearlwoodStaff : WoodStaff
{
    public override int WoodType => ItemID.Pearlwood;
    public override int StaffRarity => ItemRarityID.LightPurple;
}

/// <summary>王朝木法杖。对应源项目 `staff/acacia`。</summary>
public sealed class DynastyStaff : WoodStaff
{
    public override int WoodType => ItemID.DynastyWood;
    public override int StaffRarity => ItemRarityID.Orange;
}

/// <summary>阴森木法杖。对应源项目 `staff/mangrove`。</summary>
public sealed class SpookyStaff : WoodStaff
{
    public override int WoodType => ItemID.SpookyWood;
    public override int StaffRarity => ItemRarityID.Lime;
}

/// <summary>灰烬木法杖。对应源项目 `staff/warped`（下界木）。</summary>
public sealed class AshStaff : WoodStaff
{
    public override int WoodType => ItemID.AshWood;
    public override int StaffRarity => ItemRarityID.Orange;
}

/// <summary>
/// 樱花木法杖。对应源项目 `staff/cherry`。
///
/// ⚠️ 泰拉**没有樱花木**，而且十种泰拉木材**已经全部被原版的十种木材用掉了**
/// （橡木=木材 / 云杉=北地木 / 白桦=珍珠木 / 丛林=红木 / 金合欢=王朝木 /
/// 深色橡木=乌木 / 绯红=阴影木 / 红树=阴森木 / 诡异=灰烬木 / 竹子=棕榈木）。
///
/// 所以这里不硬凑木材，改用**珍珠木 + 紫水晶粉**：
/// 珍珠木本身就是泰拉最浅、最偏粉白的木材（视觉上最接近樱花木），
/// 再加一份咒法学的媒质材料，配方与其它法杖仍然可区分。
/// 已记进对照表 —— 这是材料替换，不是"少做了一把法杖"。
/// </summary>
public sealed class CherryStaff : HexStaff
{
    public override int StaffRarity => ItemRarityID.LightPurple;

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddRecipeGroup(RecipeGroups.Wood, 3)              // 木棍 ×3（泰拉替代物）
            .AddIngredient(ItemID.Pearlwood, 1)         // 樱花木板：泰拉用珍珠木代替
            .AddIngredient<ChargedAmethyst>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

// ── 以下 3 种是原版的特殊法杖 ────────────────────────────────────

/// <summary>
/// 淬灵晶法杖。对应源项目 `staff/quenched`。
/// 原版是「用淬灵合金做的法杖」，属于后期材料。
/// </summary>
public sealed class QuenchedStaff : HexStaff
{
    public override int StaffRarity => ItemRarityID.Pink;

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddRecipeGroup(RecipeGroups.Wood, 3)
            .AddIngredient<QuenchedAllayShard>(1)       // 原版 staffRecipe 的「木板」槽位 = 淬灵晶碎片
            .AddIngredient<ChargedAmethyst>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>
/// 启迪木法杖。对应源项目 `staff/edified`。
///
/// 原版的 14 把法杖里，这一把的「W」槽用的是 `HexBlocks.EDIFIED_PLANKS` 而不是木板 ——
/// 这里是**唯一一处**它和 <see cref="WoodStaff"/> 不同的地方，所以没有继承那个基类。
///
/// 没有额外门槛：启迪木板整族是肉前（原版 edify 不在启蒙名单里），见 PROGRESSION.generated.md。
///
/// ⚠️ 这段注释以前写的是「用**生命木**代替启迪木（用户决定）」，还专门论证了
/// 生命木没有物品形态、只能落在合成站上 —— 而代码里从来就是 `EdifiedPlanksItem`。
/// 那个方案用户当天就收回了；注释比代码多活了很久，属于最容易误导后来人的那类。
/// </summary>
public sealed class EdifiedStaff : HexStaff
{
    public override int StaffRarity => ItemRarityID.Pink;

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddRecipeGroup(RecipeGroups.Wood, 3)              // 木棍 ×3（泰拉替代物）
            .AddIngredient<EdifiedPlanksItem>(1)        // 启迪木板 ×1 —— 原版就是这个
            .AddIngredient<ChargedAmethyst>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>
/// 剖念法杖。对应源项目 `staff/mindsplice`。
///
/// 原版要的是 `MINDFLAYED_CIRCLE_COMPONENTS` —— **被脑叶切除的法术环组件**。
/// 泰拉侧的对应物就是脑叶切除的产物：红石导线 / 布尔导线 / 阿卡夏记录
/// （见 <c>HexCastingTerraria.ConfigureBrainsweepRecipes</c>）。
/// 之前这里写的是「占位材料，待体系落地后替换」—— 而那套体系**早就落地了**，
/// 属于"注释里的待办比代码还旧"：合成表给的是淬灵晶碎片 + 灵魂，
/// 和「剖念」这个主题毫无关系。
/// </summary>
public sealed class MindspliceStaff : HexStaff
{
    public override int StaffRarity => ItemRarityID.Red;

    public override void AddRecipes()
    {
        CreateRecipe()
            // 被剥离意识的环组件：脑叶切除「空导线」+ 对应职业的城镇 NPC 所得
            .AddRecipeGroup(RecipeGroups.Wood, 3)
            .AddIngredient<HexDirectrixRedstoneItem>(1) // 被脑叶切除的环组件（原版是 MINDFLAYED_CIRCLE_COMPONENTS 标签）
            .AddIngredient<ChargedAmethyst>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
