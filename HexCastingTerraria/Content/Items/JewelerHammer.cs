using HexCastingTerraria.Core.Media;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 珠宝匠锤。对应源项目 `hexcasting:jeweler_hammer`。
///
/// ## 它的真实作用（读源码才知道，猜的话一定会做错）
///
/// 源码里只有一个静态方法：
/// ```java
/// public static boolean shouldFailToBreak(Player player, BlockState state, BlockPos pos) {
///     ItemStack stack = player.getMainHandItem();
///     return stack.is(HEX_ITEMS.JEWELER_HAMMER) && Block.isShapeFullBlock(state.getShape(...));
/// }
/// ```
/// 也就是：**拿着它的时候，挖不动「完整方块」**。
///
/// 这看起来像是"限制"，其实是**工具**：紫水晶簇长在母岩上，
/// 用普通镐很容易连母岩一起挖掉（母岩挖了就没了）；拿着这把锤子就不会 ——
/// 它只挖得动形状不完整的方块（晶簇、芽）。
///
/// ## 泰拉侧的对应
///
/// 泰拉的「完整方块」= `Main.tileSolid`；晶簇是 `Main.tileSolid = false` 的贴地物件。
/// 所以规则原样成立：**拿着锤子时挖不动实心方块**。
/// 判定放在 <see cref="JewelerHammerTileGuard"/> 里（`GlobalTile`），
/// 因为"能不能挖"是**方块**的属性判定，而不是物品的使用逻辑。
/// </summary>
public sealed class JewelerHammer : ModItem
{
    public override void SetDefaults()
    {
        Item.width = 32;
        Item.height = 32;
        Item.useStyle = ItemUseStyleID.Swing;
        Item.useTime = 10;
        Item.useAnimation = 20;
        Item.autoReuse = true;
        Item.useTurn = true;
        Item.DamageType = DamageClass.Melee;
        Item.damage = 10;
        Item.knockBack = 3f;
        Item.pick = 60;                 // 与梦魇镐同级：足够挖晶簇与普通石
        Item.UseSound = SoundID.Item1;
        Item.rare = ItemRarityID.Blue;
        Item.value = Item.sellPrice(silver: 40);
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:245-253，shaped：
        //     IAN        I = 铁锭 ×1        → 泰拉用原版配方组「铁锭/铅锭」
        //      S         N = 铁粒 ×1        → 泰拉没有铁粒，折进铁锭里
        //      S         A = 紫水晶碎片 ×1  → 泰拉对应物 = ItemID.Amethyst
        //                S = 木棍 ×2        → 泰拉没有木棍，折进木材里
        //
        // 阶段：肉前。源项目这条的解锁条件是「拥有紫水晶碎片」，是**最早**的工具之一
        // （珠宝匠锤就是「把紫晶敲成粉」的那把锤子，见 ItemJewelerHammer）。
        // 之前这里是「铁锭 ×8 + 粉 ×10 + 紫晶 ×3」—— 数量全部凭感觉，比原版贵一个数量级。
        CreateRecipe()
            .AddRecipeGroup(RecipeGroupID.IronBar, 1)
            .AddIngredient<AmethystShard>(1)
            .AddRecipeGroup(RecipeGroupID.Wood, 2)
            .AddTile(TileID.Anvils)
            .Register();
    }
}

/// <summary>
/// 珠宝匠锤的挖掘守卫：拿着它的时候不允许破坏**实心**方块。
///
/// 放在 `GlobalTile` 而不是逐个方块里：这条规则覆盖**所有**方块，
/// 而它依赖的是「手上拿着什么」—— 那只有全局钩子看得到。
///
/// 注意：只在本机玩家身上判定。服务端上 `Main.LocalPlayer` 不是那个挖方块的人，
/// 但泰拉的挖掘本来就是**客户端发起**的（`CanKillTile` 在客户端拦下就不会发出破坏请求），
/// 所以这条判定放在客户端是正确的位置。
/// </summary>
public sealed class JewelerHammerTileGuard : GlobalTile
{
    public override bool CanKillTile(int i, int j, int type, ref bool blockDamaged)
    {
        var player = Main.LocalPlayer;
        if (player is not { active: true }) return true;

        // 拿着锤子 + 这块是实心方块 -> 挖不动
        if (player.HeldItem.type != ModContent.ItemType<JewelerHammer>()) return true;
        if (!Main.tileSolid[type]) return true;

        return false;
    }
}
