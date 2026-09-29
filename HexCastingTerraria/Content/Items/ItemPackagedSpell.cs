using System.Collections.Generic;
using HexCastingTerraria.Core.Casting;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Media;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 打包法术物品的基类：把一串图案与一份媒质封在里面，右键即可施放。
/// 移植自源项目 `common/items/magic/ItemPackagedHex.java`。
///
/// 三档的差别（**照抄源项目，不是自己定的**）：
///
/// | | 施放后 | 冷却 | 池子空了以后 |
/// |---|---|---|---|
/// | 符纸 Cypher | **消失** | 8 tick | 直接用掉 |
/// | 饰品 Trinket | 留下 | 5 tick | 罢工，等你再灌媒质 |
/// | 法器 Artifact | 留下 | 3 tick | **可以继续从背包扣媒质** |
///
/// 为什么符纸用完就没了：它是「一次性咒符」，代价最低（1 晶体）。
/// 法器最贵（10 晶体）但最省心 —— 这就是三档的取舍。
/// </summary>
public abstract class ItemPackagedSpell : ModItem
{
    /// <summary>变体数量。源项目 `ItemFocus.NUM_VARIANTS = 8`。</summary>
    public const int NumVariants = 8;

    /// <summary>
    /// 封在里面的图案。空 = 还没封东西（`craft/*` 图案只认空的）。
    ///
    /// 注意这里是**显式字段 + 只读属性**，不是自动属性：
    /// tModLoader 的「引用字段可能在克隆间不安全」检查看的是**字段**，
    /// 而自动属性的字段是编译器生成的（`<Patterns>k__BackingField`），
    /// 在那个字段上标 `[CloneByReference]` 是标不上去的 —— 警告不会消失。
    /// </summary>
    [CloneByReference]
    private List<HexPattern> _patterns = new();

    public IReadOnlyList<HexPattern> Patterns => _patterns;

    /// <summary>里面还剩多少媒质。</summary>
    public long Media { get; private set; }

    /// <summary>被封进去时的媒质总额（决定容量上限）。</summary>
    public long MaxMedia { get; private set; }

    /// <summary>外观变体编号（`cycle_variant` 改的就是它）。</summary>
    public int Variant { get; private set; }

    public abstract PackagedSpellKind Kind { get; }

    /// <summary>施放后是否消失。源项目 `breakAfterDepletion()`。</summary>
    public virtual bool BreakAfterDepletion => Kind == PackagedSpellKind.Cypher;

    /// <summary>冷却（tick）。源项目默认值：符纸 8 / 饰品 5 / 法器 3。</summary>
    public virtual int CooldownTicks => Kind switch
    {
        PackagedSpellKind.Cypher => 8,
        PackagedSpellKind.Trinket => 5,
        _ => 3,
    };

    /// <summary>自己的池子空了之后，能不能继续从背包扣媒质。只有法器可以。</summary>
    public virtual bool CanDrawFromInventory => Kind == PackagedSpellKind.Artifact;

    /// <summary>是不是「空的」（`craft/*` 只认它）。</summary>
    public bool IsEmpty => Patterns.Count == 0;

    public override void SetDefaults()
    {
        Item.width = 20;
        Item.height = 20;
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.useTurn = true;
        Item.autoReuse = false;
        Item.maxStack = 1;              // 每件存的东西不同，堆叠在原理上不成立
        Item.consumable = false;
        Item.noMelee = true;
        Item.rare = RarityFor(Kind);
        Item.value = Item.sellPrice(gold: Kind switch
        {
            PackagedSpellKind.Cypher => 1,
            PackagedSpellKind.Trinket => 5,
            _ => 10,
        });
    }

    private static int RarityFor(PackagedSpellKind kind) => kind switch
    {
        PackagedSpellKind.Cypher => ItemRarityID.Orange,
        PackagedSpellKind.Trinket => ItemRarityID.LightPurple,
        _ => ItemRarityID.Pink,
    };

    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        var mod = Mod;

        if (IsEmpty)
        {
            tooltips.Add(new TooltipLine(mod, "HexPackagedEmpty", "【空】用 craft/… 图案封入咒术"));
        }
        else
        {
            tooltips.Add(new TooltipLine(mod, "HexPackagedCount", $"封有 {Patterns.Count} 个图案"));
            tooltips.Add(new TooltipLine(mod, "HexPackagedMedia",
                $"媒质：{MediaConstants.Format(Media)} / {MediaConstants.Format(MaxMedia)}"));
        }

        // 变体目前只体现在这一行 —— 贴图是欠账（见 TODO_PLAN.md 的美术章节）
        tooltips.Add(new TooltipLine(mod, "HexPackagedVariant", $"外观变体 {Variant + 1} / {NumVariants}"));
    }

    // ── 供 craft/* 图案与 cycle_variant 使用 ────────────────────────

    /// <summary>把图案与媒质封进来。只有**空**物品能封。</summary>
    public bool Fill(IReadOnlyList<Iota> patterns, long media)
    {
        if (!IsEmpty) return false;

        var list = new List<HexPattern>(patterns.Count);
        foreach (var iota in patterns)
        {
            if (iota is not PatternIota p) return false;
            list.Add(p.Pattern);
        }

        if (list.Count == 0) return false;

        _patterns = list;
        Media = media;
        MaxMedia = media;
        return true;
    }

    /// <summary>推进变体编号。对应源项目 `variant = (variant + 1) % numVariants()`。</summary>
    public void CycleVariant() => Variant = (Variant + 1) % NumVariants;

    /// <summary>池子里取出媒质。返回实际取到的量。</summary>
    public long Spend(long amount)
    {
        long paid = System.Math.Min(amount, Media);
        Media -= paid;
        return paid;
    }

    public void Refund(long amount)
    {
        Media = System.Math.Min(MaxMedia, Media + amount);
    }

    // ── 使用 ────────────────────────────────────────────────────────

    public override bool? UseItem(Player player)
    {
        if (IsEmpty)
        {
            if (player.whoAmI == Main.myPlayer)
            {
                Client.HexCanvasState.SetMessage("这个东西还是空的：先用 craft/… 图案封入咒术");
            }
            return false;
        }

        PackagedSpellCast.Request(player, player.inventory[player.selectedItem]);
        return true;
    }

    public override bool CanUseItem(Player player) => !IsEmpty;

    // ── 存档 ────────────────────────────────────────────────────────

    public override void SaveData(TagCompound tag)
    {
        if (Patterns.Count > 0)
        {
            // 复用 iota 的信封格式装一串图案 —— 它本来就只含 TagCompound 原生类型
            var list = new List<Iota>(Patterns.Count);
            foreach (var p in Patterns) list.Add(new PatternIota(p));
            tag["patterns"] = new ListIota(list).Serialize();
        }

        tag["media"] = Media;
        tag["maxMedia"] = MaxMedia;
        tag["variant"] = Variant;
    }

    public override void LoadData(TagCompound tag)
    {
        _patterns = new List<HexPattern>();
        Media = 0;
        MaxMedia = 0;
        Variant = 0;

        if (tag.TryGet("patterns", out TagCompound envelope)
            && IotaSerializer.TryDeserialize(envelope, out var iota)
            && iota is ListIota list)
        {
            foreach (var item in list.Items)
            {
                if (item is PatternIota p) _patterns.Add(p.Pattern);
            }
        }

        tag.TryGet("media", out long media);
        tag.TryGet("maxMedia", out long maxMedia);
        tag.TryGet("variant", out int variant);

        Media = media;
        MaxMedia = maxMedia;
        Variant = System.Math.Clamp(variant, 0, NumVariants - 1);
    }
}

/// <summary>
/// 符纸。对应源项目 `hexcasting:cypher`。一次性：放完就没了。
/// </summary>
public sealed class Cypher : ItemPackagedSpell
{
    public override PackagedSpellKind Kind => PackagedSpellKind.Cypher;

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:131-135，`ringCornerless(CYPHER, 1, 铜锭, 紫水晶粉)`：
        //     铜锭 ×4 + 紫水晶粉 ×1 → 1（cornerless 环 = 四条边中点，共 4 格）。
        //
        // 阶段：肉前。**三种「打包装备」在源项目里是一组并列的铜→铁→金阶梯**，
        // 不是「符纸 → 饰品 → 法器」的链式升级。之前写成了链式，
        // 结果每种都要先把上一种做出来，成本和原版完全脱钩。
        CreateRecipe()
            .AddIngredient(ItemID.CopperBar, 4)
            .AddIngredient<AmethystDust>(1)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>饰品。对应源项目 `hexcasting:trinket`。可重复使用，池子空了要重新灌。</summary>
public sealed class Trinket : ItemPackagedSpell
{
    public override PackagedSpellKind Kind => PackagedSpellKind.Trinket;

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:137-141，`ringCornerless(TRINKET, 1, 铁锭, 紫水晶碎片)`：
        //     铁锭 ×4 + 紫水晶碎片 ×1 → 1
        // 阶段：肉前。铁用原版配方组（铁锭/铅锭都收）；铁活放在铁砧。
        CreateRecipe()
            .AddRecipeGroup(RecipeGroups.IronBar, 4)
            .AddIngredient<AmethystShard>(1)
            .AddTile(TileID.Anvils)
            .Register();
    }
}

/// <summary>法器。对应源项目 `hexcasting:artifact`。最贵，但池子空了还能从背包扣媒质。</summary>
public sealed class Artifact : ItemPackagedSpell
{
    public override PackagedSpellKind Kind => PackagedSpellKind.Artifact;

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:143-151，shaped " F "/"FAF"/" D "：
        //     金锭 ×4 + 充能紫水晶 ×1 + 唱片 ×1
        //
        // 阶段：肉前（这一组阶梯的顶点，但仍在同一阶段）。
        // 唱片那一格泰拉对应物最接近的是**八音盒**，但八音盒得先录一首曲子，
        // 当成配料会让配方变得不可控；改用**钻石**——同为「稀有的收藏品」，
        // 而且泰拉玩家一眼就知道那是好东西。这是有意替换，不是漏抄。
        CreateRecipe()
            .AddIngredient(ItemID.GoldBar, 4)
            .AddIngredient<ChargedAmethyst>(1)
            .AddIngredient(ItemID.Diamond, 1)           // 唱片 → 钻石
            .AddTile(TileID.Anvils)
            .Register();
    }
}
