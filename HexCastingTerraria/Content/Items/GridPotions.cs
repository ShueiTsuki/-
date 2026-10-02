using HexCastingTerraria.Content.Buffs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 明晰药水 / 蒙翳药水（原版 HexPotions.ENLARGE_GRID / SHRINK_GRID，官方中文名）：喝了得到明晰 / 蒙翳 3 分钟（原版 3600 刻），
/// 咒术网格变细 / 变粗（见 <see cref="EnlargeGrid"/>、<see cref="ShrinkGrid"/>）。
///
/// 原版是酿造：粗制的药水 + 紫水晶粉 → 明晰药水；明晰药水 + 发酵蛛眼 → 蒙翳药水；另有红石延长、萤石加强的版本。
/// 泰拉没有酿造台，药水都在放置的瓶子旁边做：瓶装水 + 紫水晶粉 → 明晰药水；明晰药水 + 腐肉或椎骨（顶替发酵蛛眼）→ 蒙翳药水。
/// 延长 / 加强的版本先不做（泰拉没有这种加料的做法）。
///
/// 其余属性照泰拉的药水：使用时间 17、卖 2 银、蓝色稀有度。贴图借用泰拉小治疗药水的烧瓶，液体换成原版效果的颜色
/// （MC 里这两种药水也是同一个瓶子、颜色不同），见 <see cref="GridPotionArt"/>。
/// 这个烧瓶在泰拉只有红、蓝、粉三色，紫和黄绿不会和别的药水认混；曾经用过夜猫子药水的瓶子，
/// 蒙翳的黄绿和夜猫子本身几乎一样（用户指出，2026-10-02 换掉，三个候选里用户选的这个）。
/// </summary>
public abstract class GridPotion : ModItem
{
    public override string Texture => $"Terraria/Images/Item_{ItemID.LesserHealingPotion}";

    protected abstract int Buff { get; }

    /// <summary>原版效果颜色（HexMobEffects 的第二个参数）。</summary>
    internal abstract Color EffectColor { get; }

    public override void SetStaticDefaults()
    {
        Item.ResearchUnlockCount = 20;
        ItemID.Sets.DrinkParticleColors[Type] = GridPotionArt.DrinkColors(EffectColor);
    }

    public override void SetDefaults()
    {
        Item.UseSound = SoundID.Item3;
        Item.useStyle = ItemUseStyleID.DrinkLiquid;
        Item.useTurn = true;
        Item.useAnimation = 17;
        Item.useTime = 17;
        Item.maxStack = Item.CommonMaxStack;
        Item.consumable = true;
        Item.width = 14;
        Item.height = 24;
        Item.buffType = Buff;
        Item.buffTime = 3 * 60 * 60;
        Item.value = Item.sellPrice(silver: 2);
        Item.rare = ItemRarityID.Blue;
    }
}

/// <summary>明晰药水：瓶装水 + 紫水晶粉（原版：粗制的药水 + 紫水晶粉）。</summary>
public sealed class EnlargeGridPotion : GridPotion
{
    protected override int Buff => ModContent.BuffType<EnlargeGrid>();

    internal override Color EffectColor => new(0xc8, 0x75, 0xff);

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient(ItemID.BottledWater, 1)
            .AddIngredient<AmethystDust>(1)
            .AddTile(TileID.Bottles)
            .Register();
    }
}

/// <summary>蒙翳药水：明晰药水 + 腐肉或椎骨（原版：明晰药水 + 发酵蛛眼，「腐化」成效果相反的药水）。</summary>
public sealed class ShrinkGridPotion : GridPotion
{
    protected override int Buff => ModContent.BuffType<ShrinkGrid>();

    internal override Color EffectColor => new(0xc0, 0xe6, 0x60);

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient<EnlargeGridPotion>(1)
            .AddRecipeGroup(HexRecipeGroups.EvilChunks, 1)
            .AddTile(TileID.Bottles)
            .Register();
    }
}

/// <summary>
/// 客户端：内容加载完以后，用游戏自带的贴图拼出明晰 / 蒙翳的药水和增益图标（<see cref="VanillaRecolor"/>），只在内存里做：
///   - 药水：小治疗药水的烧瓶，红色液体按亮度换成原版效果的颜色；
///   - 增益：泰拉自带的空白增益底图（Images/Buff，蓝框）上叠原版的效果图标（模组里的 Buffs/EnlargeGrid.png 等）；
///     蒙翳是减益，照泰拉减益图标的红框：把底图的四种蓝换成泰拉减益图标框上对应位置的红（对照中毒、黑暗的图标取的色）。
/// </summary>
public sealed class GridPotionArt : ModSystem
{
    /// <summary>空白增益底图的蓝 → 泰拉减益图标框的红（同一位置的颜色）。</summary>
    private static readonly (Color Blue, Color Red)[] DebuffFrame =
    {
        (new Color(205, 237, 254), new Color(255, 165, 136)),
        (new Color(154, 218, 254), new Color(255, 114, 87)),
        (new Color(68, 187, 253), new Color(255, 27, 5)),
        (new Color(2, 139, 218), new Color(231, 0, 0)),
    };

    private static readonly System.Collections.Generic.List<Asset<Texture2D>> Made = new();

    /// <summary>瓶身的色标：效果颜色压暗、提亮成四档。</summary>
    private static (float At, Color Color)[] Liquid(Color c) => new[]
    {
        (0f, Color.Lerp(Color.Black, c, 0.3f)),
        (0.35f, Color.Lerp(Color.Black, c, 0.62f)),
        (0.7f, c),
        (1f, Color.Lerp(c, Color.White, 0.55f)),
    };

    /// <summary>喝药时冒的颗粒：泰拉每种药水三种颜色，取瓶身的中、本色、亮三档。</summary>
    internal static Color[] DrinkColors(Color c) => new[] { Color.Lerp(Color.Black, c, 0.62f), c, Color.Lerp(c, Color.White, 0.55f) };

    public override void PostSetupContent()
    {
        if (Main.dedServ) return;
        Main.QueueMainThreadAction(() =>
        {
            // 拼图只是外观：万一失败，记日志、照用小治疗药水和不带框的图标，不能让整个模组加载失败
            try
            {
                Build<EnlargeGridPotion, EnlargeGrid>(debuff: false);
                Build<ShrinkGridPotion, ShrinkGrid>(debuff: true);
            }
            catch (System.Exception e)
            {
                Mod.Logger.Error("[HexCasting] 明晰 / 蒙翳的贴图拼接失败，照用小治疗药水和不带框的图标：" + e);
            }
        });
    }

    private static void Build<TPotion, TBuff>(bool debuff) where TPotion : GridPotion where TBuff : ModBuff
    {
        var potion = ModContent.GetInstance<TPotion>();
        var liquid = Liquid(potion.EffectColor);
        var potionArt = VanillaRecolor.Create($"Images/Item_{ItemID.LesserHealingPotion}", potion.Name,
            (data, _, _) => VanillaRecolor.RecolorLiquid(data, liquid));
        Made.Add(potionArt);
        TextureAssets.Item[potion.Type] = potionArt;

        var buff = ModContent.GetInstance<TBuff>();
        var icon = VanillaRecolor.ReadPixels(ModContent.Request<Texture2D>(buff.Texture, AssetRequestMode.ImmediateLoad).Value);
        var buffArt = VanillaRecolor.Create("Images/Buff", buff.Name + "Buff", (data, _, _) =>
        {
            if (debuff) ToDebuffFrame(data);
            VanillaRecolor.Overlay(data, icon);
        });
        Made.Add(buffArt);
        TextureAssets.Buff[buff.Type] = buffArt;
    }

    private static void ToDebuffFrame(Color[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            foreach (var (blue, red) in DebuffFrame)
            {
                if (data[i].R == blue.R && data[i].G == blue.G && data[i].B == blue.B)
                {
                    data[i] = red with { A = data[i].A };
                    break;
                }
            }
        }
    }

    /// <summary>拼出来的几张是自己建的（不归资源库管），卸载时自己释放。</summary>
    public override void Unload()
    {
        var made = Made.ToArray();
        Made.Clear();
        if (made.Length == 0) return;
        Main.QueueMainThreadAction(() =>
        {
            foreach (var asset in made) asset.Dispose();
        });
    }
}
