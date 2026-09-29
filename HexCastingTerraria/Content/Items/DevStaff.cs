using HexCastingTerraria.Content;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 开发者法杖（Dev Staff）。
///
/// **不是**原版物品 —— 这是移植期的调试工具：可直接合成、不需要任何前置，
/// 用来验证整条「拿法杖 → 开画布 → 画图案 → 识别 → 求值」链路。
///
/// 正式的法杖见 <see cref="HexStaff"/> 的 13 个子类
/// （10 种木材 + 淬灵晶 / 启迪木 / 剖念）。
/// </summary>
public class DevStaff : HexStaff
{
    public override int StaffRarity => ItemRarityID.Purple;

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.value = 0;
    }

    public override void AddRecipes()
    {
        // 调试配方：只要木头，站在工作台旁即可合成。
        // 不要求紫晶是因为开局未必已经采到宝石，调试阶段不该被材料卡住。
        CreateRecipe()
            .AddIngredient(ItemID.Wood, 20)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
