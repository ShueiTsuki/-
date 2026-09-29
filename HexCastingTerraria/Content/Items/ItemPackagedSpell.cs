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
public abstract class ItemPackagedSpell : ModItem, IHexVariantItem
{
    /// <summary>变体数量。源项目 `ItemFocus.NUM_VARIANTS = 8`。</summary>
    public const int NumVariantsConst = 8;

    /// <summary>
    /// 封在里面的咒术（原版 TAG_PROGRAM）。空 = 还没封东西（`craft/*` 图案只认空的）。
    /// 原版存的是**任意 iota 的列表**（craft/* 不检查每一项是不是图案），执行时整串入队。
    ///
    /// 显式字段 + [CloneByReference]：列表只整体替换、不就地改，克隆间共享安全。
    /// </summary>
    [CloneByReference]
    private List<Iota> _program = new();

    public IReadOnlyList<Iota> Program => _program;

    /// <summary>里面还剩多少媒质。</summary>
    public long Media { get; private set; }

    /// <summary>被封进去时的媒质总额（决定容量上限）。</summary>
    public long MaxMedia { get; private set; }

    /// <summary>外观变体编号（`cycle_variant` 改的就是它）。</summary>
    public int Variant { get; private set; }

    public int NumVariants => ItemPackagedSpell.NumVariantsConst;

    public void SetVariant(int variant) => Variant = System.Math.Clamp(variant, 0, NumVariantsConst - 1);

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
    public bool IsEmpty => _program.Count == 0;

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
            tooltips.Add(new TooltipLine(mod, "HexPackagedCount", $"封有 {_program.Count} 个 iota"));
            tooltips.Add(new TooltipLine(mod, "HexPackagedMedia",
                $"媒质：{MediaConstants.Format(Media)} / {MediaConstants.Format(MaxMedia)}"));
        }

        // 变体目前只体现在这一行 —— 贴图是欠账（见 TODO_PLAN.md 的美术章节）
        tooltips.Add(new TooltipLine(mod, "HexPackagedVariant", $"外观变体 {Variant + 1} / {NumVariantsConst}"));
    }

    // ── 供 craft/* 图案与 cycle_variant 使用 ────────────────────────

    /// <summary>把咒术与媒质封进来（原版 writeHex）。只有**空**物品能封。</summary>
    public bool Fill(IReadOnlyList<Iota> program, long media)
    {
        if (!IsEmpty || program.Count == 0) return false;
        _program = new List<Iota>(program);
        Media = media;
        MaxMedia = media;
        return true;
    }

    /// <summary>原版 clearHex（`erase`）：咒术、媒质、上限全清，变回空的。</summary>
    public void ClearHex()
    {
        _program = new List<Iota>();
        Media = 0;
        MaxMedia = 0;
    }

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
        if (_program.Count > 0)
        {
            // 复用 iota 的信封格式（键名沿用旧存档的 "patterns"）
            tag["patterns"] = new ListIota(_program).Serialize();
        }

        tag["media"] = Media;
        tag["maxMedia"] = MaxMedia;
        tag["variant"] = Variant;
    }

    public override void LoadData(TagCompound tag)
    {
        _program = new List<Iota>();
        Media = 0;
        MaxMedia = 0;
        Variant = 0;

        if (tag.TryGet("patterns", out TagCompound envelope)
            && IotaSerializer.TryDeserialize(envelope, out var iota)
            && iota is ListIota list)
        {
            _program = new List<Iota>(list.Items);
        }

        tag.TryGet("media", out long media);
        tag.TryGet("maxMedia", out long maxMedia);
        tag.TryGet("variant", out int variant);

        Media = media;
        MaxMedia = maxMedia;
        Variant = System.Math.Clamp(variant, 0, NumVariantsConst - 1);
    }

    // 联机：背包同步只带 NetSend 写的东西 —— 之前没写，服务端手里的打包法术永远是空的，联机放不出来
    public override void NetSend(System.IO.BinaryWriter writer)
    {
        writer.Write((ushort)_program.Count);
        foreach (var iota in _program) Content.Net.IotaWire.Write(writer, iota);
        writer.Write(Media);
        writer.Write(MaxMedia);
        writer.Write((byte)Variant);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        int n = reader.ReadUInt16();
        var list = new List<Iota>(n);
        for (int i = 0; i < n; i++) list.Add(Content.Net.IotaWire.Read(reader));
        _program = list;
        Media = reader.ReadInt64();
        MaxMedia = reader.ReadInt64();
        Variant = System.Math.Clamp((int)reader.ReadByte(), 0, NumVariantsConst - 1);
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
