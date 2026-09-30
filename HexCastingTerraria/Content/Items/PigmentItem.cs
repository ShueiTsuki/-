using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 颜料（染色剂）。移植自源项目 <c>common/items/pigment/*</c>：
/// 拿在手上（任一只手）施放「内化染色剂」，颜料被消耗，之后你的法术火花、哨卫就换成它的颜色（见 Core/Media/Pigments.cs）。
/// 纯表现，不影响任何机制；原版不可堆叠。
///
/// 35 个具体类由 _tools/gen_pigments.py 生成（PigmentItems.Generated.cs），贴图是原版的动画贴图按 MC 刻展开的竖排帧。
/// </summary>
public abstract class PigmentItem : ModItem
{
    /// <summary>颜料 id（<see cref="Core.Media.Pigments"/> 里的键）。</summary>
    public abstract string PigmentId { get; }

    /// <summary>贴图帧数（每帧 = 1 MC 刻 = 3 泰拉刻）。</summary>
    protected abstract int FrameCount { get; }

    public override string Texture => "HexCastingTerraria/Content/Items/Pigments/" + Name;

    public override void SetStaticDefaults()
    {
        Main.RegisterItemAnimation(Type, new DrawAnimationVertical(3, FrameCount));
        Item.ResearchUnlockCount = 1;
    }

    public override void SetDefaults()
    {
        Item.width = 24;
        Item.height = 24;
        Item.maxStack = 1;
        Item.rare = ItemRarityID.Blue;
        Item.value = Item.sellPrice(silver: 10);
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes 176-213：
        //   染料 / 骄傲旗：" D " "DCD" " D " —— 紫水晶粉 ×4 + 中间一件
        //   空无：粉 ×4 + 紫水晶碎片；远古：粉 ×4 + 铜锭；灵魂闪光："DCD" "CIC" "DCD" —— 粉 ×8 + 碎片
        if (!Recipes.TryGetValue(PigmentId, out var r)) { return; }
        var recipe = CreateRecipe().AddIngredient<AmethystDust>(r.Dust);
        if (r.Mod) { recipe.AddIngredient<AmethystShard>(1); }
        else { recipe.AddIngredient(r.Item, 1); }
        recipe.AddTile(TileID.WorkBenches).Register();
    }

    /// <summary>
    /// 中间那件材料。MC 染料 → 泰拉同色染料；骄傲旗颜料原版用的是带梗的 MC 物品，
    /// 换成泰拉里全年都拿得到、意思最接近的那件（对照表见 AUDIT_VS_ORIGINAL.md「颜料」）。
    /// </summary>
    private static readonly Dictionary<string, (int Item, int Dust, bool Mod)> Recipes = new()
    {
        ["dye_white"] = (ItemID.BrightSilverDye, 4, false),
        ["dye_orange"] = (ItemID.OrangeDye, 4, false),
        ["dye_magenta"] = (ItemID.VioletDye, 4, false),
        ["dye_light_blue"] = (ItemID.SkyBlueDye, 4, false),
        ["dye_yellow"] = (ItemID.YellowDye, 4, false),
        ["dye_lime"] = (ItemID.LimeDye, 4, false),
        ["dye_pink"] = (ItemID.PinkDye, 4, false),
        ["dye_gray"] = (ItemID.BlackAndWhiteDye, 4, false),
        ["dye_light_gray"] = (ItemID.SilverDye, 4, false),
        ["dye_cyan"] = (ItemID.CyanDye, 4, false),
        ["dye_purple"] = (ItemID.PurpleDye, 4, false),
        ["dye_blue"] = (ItemID.BlueDye, 4, false),
        ["dye_brown"] = (ItemID.BrownDye, 4, false),
        ["dye_green"] = (ItemID.GreenDye, 4, false),
        ["dye_red"] = (ItemID.RedDye, 4, false),
        ["dye_black"] = (ItemID.BlackDye, 4, false),

        ["pride_agender"] = (ItemID.Glass, 4, false),                 // 玻璃
        ["pride_aroace"] = (ItemID.GrassSeeds, 4, false),             // 小麦种子 → 草种
        ["pride_aromantic"] = (ItemID.WoodenArrow, 4, false),         // 箭
        ["pride_asexual"] = (ItemID.Hay, 4, false),                   // 面包 → 干草（泰拉没有面包）
        ["pride_bisexual"] = (ItemID.Seed, 4, false),                 // 小麦 → 种子（割草掉的那种）
        ["pride_demiboy"] = (ItemID.IronOre, 4, false),               // 粗铁
        ["pride_demigirl"] = (ItemID.CopperOre, 4, false),            // 粗铜
        ["pride_gay"] = (ItemID.GrayBrickWall, 4, false),             // 石砖墙
        ["pride_genderfluid"] = (ItemID.WaterBucket, 4, false),       // 水桶
        ["pride_genderqueer"] = (ItemID.Bottle, 4, false),            // 玻璃瓶
        ["pride_intersex"] = (ItemID.Daybloom, 4, false),             // 杜鹃花丛 → 太阳花（开花的植物）
        ["pride_lesbian"] = (ItemID.Hive, 4, false),                  // 蜜脾 → 蜂巢块
        ["pride_nonbinary"] = (ItemID.GreenMoss, 4, false),           // 苔藓块
        ["pride_pansexual"] = (ItemID.CookingPot, 4, false),          // 胡萝卜 / 煎锅（pan）→ 烹饪锅
        ["pride_plural"] = (ItemID.Timer1Second, 4, false),           // 中继器 → 1 秒计时器（泰拉的延时元件）
        ["pride_transgender"] = (ItemID.FriedEgg, 4, false),          // 鸡蛋 → 煎蛋（泰拉没有生蛋）

        ["default"] = (0, 4, true),                                   // 粉 ×4 + 紫水晶碎片
        ["ancient"] = (ItemID.CopperBar, 4, false),                   // 粉 ×4 + 铜锭
        ["uuid"] = (0, 8, true),                                      // 粉 ×8 + 紫水晶碎片
    };
}
