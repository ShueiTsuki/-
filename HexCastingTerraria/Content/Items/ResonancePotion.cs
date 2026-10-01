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
/// 共振药水（移植版新增，用户 2026-10-01 定）：喝了得到「共振」增益（<see cref="Resonance"/>）30 分钟 ——
/// 挖淬灵晶块掉方块本身，和镐子的「共振」前缀一样（原版的精准采集）。
///
/// 用户给的使用时间 17、卖 2 银和泰拉的挖矿药水一样，其余属性也照挖矿药水。
/// 配方：瓶装水 + 闪耀根 + 月光草 + 紫水晶碎片，放置的瓶子（在炼药桌做照泰拉有 1/3 的几率不耗材料，tML 自动加）。
/// 巫师也卖，见 <see cref="ResonancePotionShop"/>。
///
/// 贴图借用泰拉的挖矿药水，客户端加载完把瓶身换成紫水晶色（<see cref="ResonanceArt"/>）。
/// </summary>
public sealed class ResonancePotion : ModItem
{
    public override string Texture => $"Terraria/Images/Item_{ItemID.MiningPotion}";

    public override void SetStaticDefaults()
    {
        Item.ResearchUnlockCount = 20;
        ItemID.Sets.DrinkParticleColors[Type] = ResonanceArt.DrinkColors;
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
        Item.buffType = ModContent.BuffType<Resonance>();
        Item.buffTime = 30 * 60 * 60;
        Item.value = Item.sellPrice(silver: 2);
        Item.rare = ItemRarityID.Blue;
    }

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient(ItemID.BottledWater, 1)
            .AddIngredient(ItemID.Blinkroot, 1)
            .AddIngredient(ItemID.Moonglow, 1)
            .AddIngredient<AmethystShard>(1)
            .AddTile(TileID.Bottles)
            .Register();
    }
}

/// <summary>
/// 巫师卖共振药水，2 银（用户定的价）。什么时候开始卖由我定（用户 2026-10-01 交代）：玩家开悟以后。
/// 理由：药水只对淬灵晶块有用，淬灵晶块要剥离小精灵的意识才有，剥离意识要开悟；巫师本来就要肉后才能救出来，
/// 两条叠起来正好是淬灵晶拿得到的那一档（肉后 · 启蒙）。商店是各人自己看的，开悟按打开商店的那个玩家算。
/// </summary>
public sealed class ResonancePotionShop : GlobalNPC
{
    public override void ModifyShop(NPCShop shop)
    {
        if (shop.NpcType != NPCID.Wizard) return;
        shop.Add(new Item(ModContent.ItemType<ResonancePotion>()) { shopCustomPrice = Item.buyPrice(silver: 2) },
            HexConditions.Enlightened);
    }
}

/// <summary>
/// 客户端：内容加载完以后，把泰拉挖矿药水的贴图和挖矿增益的图标读出来改色，换成共振药水 / 共振增益自己的
/// （<see cref="VanillaRecolor"/>）。药水：瓶颈和瓶塞不动，瓶身按亮度换成紫水晶色；
/// 增益图标：镐子周围的金光换成紫光，蓝框、镐头、木柄不动。颜色取自模组紫水晶碎片的色调。
/// </summary>
public sealed class ResonanceArt : ModSystem
{
    /// <summary>泰拉药水贴图 20×30：第 14 行往下是瓶身，上面是瓶颈和瓶塞。</summary>
    private const int BodyTop = 14;

    /// <summary>瓶身：把瓶身的亮度拉到 0 ~ 1，再在这几个色标之间取。</summary>
    private static readonly (float At, Color Color)[] Liquid =
    {
        (0f, new Color(46, 24, 82)),
        (0.35f, new Color(96, 58, 160)),
        (0.65f, new Color(150, 104, 220)),
        (0.88f, new Color(204, 166, 250)),
        (1f, new Color(240, 222, 255)),
    };

    /// <summary>增益图标的金光：亮度 0.6 ~ 1 对应这几个色标。</summary>
    private static readonly (float At, Color Color)[] Glow =
    {
        (0f, new Color(150, 86, 228)),
        (0.5f, new Color(196, 148, 252)),
        (1f, new Color(240, 222, 255)),
    };

    /// <summary>喝药时冒的颗粒：泰拉每种药水给三种颜色，这里取瓶身的暗、中、亮三档。</summary>
    internal static Color[] DrinkColors => new[] { new Color(96, 58, 160), new Color(150, 104, 220), new Color(204, 166, 250) };

    private static Asset<Texture2D>? _potion;
    private static Asset<Texture2D>? _buff;

    public override void PostSetupContent()
    {
        if (Main.dedServ) return;
        Main.QueueMainThreadAction(() =>
        {
            // 改色只是外观：万一失败，记日志、照用泰拉挖矿药水的样子，不能让整个模组加载失败
            try
            {
                _potion = VanillaRecolor.Create($"Images/Item_{ItemID.MiningPotion}", "ResonancePotion", RecolorPotion);
                _buff = VanillaRecolor.Create($"Images/Buff_{BuffID.Mining}", "ResonanceBuff", RecolorBuff);
                TextureAssets.Item[ModContent.ItemType<ResonancePotion>()] = _potion;
                TextureAssets.Buff[ModContent.BuffType<Resonance>()] = _buff;
            }
            catch (System.Exception e)
            {
                Mod.Logger.Error("[HexCasting] 共振药水改色失败，照用挖矿药水的样子：" + e);
            }
        });
    }

    /// <summary>改过色的两张是自己建的（不归资源库管），卸载时自己释放。</summary>
    public override void Unload()
    {
        var potion = _potion;
        var buff = _buff;
        _potion = null;
        _buff = null;
        if (potion is null && buff is null) return;
        Main.QueueMainThreadAction(() =>
        {
            potion?.Dispose();
            buff?.Dispose();
        });
    }

    private static void RecolorPotion(Color[] data, int width, int height)
    {
        float lo = 1f, hi = 0f;
        for (int i = BodyTop * width; i < data.Length; i++)
        {
            if (data[i].A == 0) continue;
            float l = VanillaRecolor.Luma(data[i]);
            lo = System.Math.Min(lo, l);
            hi = System.Math.Max(hi, l);
        }
        if (hi <= lo) return;
        for (int i = BodyTop * width; i < data.Length; i++)
        {
            var c = data[i];
            if (c.A == 0) continue;
            data[i] = VanillaRecolor.Gradient(Liquid, (VanillaRecolor.Luma(c) - lo) / (hi - lo)) with { A = c.A };
        }
    }

    /// <summary>金光是很亮的暖色（红 ≥ 绿 ≥ 蓝、最亮的通道 ≥ 240、红比蓝多 60 以上）；木柄暗，蓝框是冷色，都挑不中。</summary>
    private static void RecolorBuff(Color[] data, int width, int height)
    {
        for (int i = 0; i < data.Length; i++)
        {
            var c = data[i];
            if (c.A == 0 || c.R < 240 || c.R < c.G || c.G < c.B || c.R - c.B <= 60) continue;
            data[i] = VanillaRecolor.Gradient(Glow, (VanillaRecolor.Luma(c) - 0.6f) / 0.4f) with { A = c.A };
        }
    }
}
