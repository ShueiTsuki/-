using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;
using HexCastingTerraria.Core.Ui;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 远古卷轴（源项目 ItemScroll + TAG_OP_ID，「%s之远古卷轴」）：写着**本世界**某个大法术的笔顺。
/// 大法术的笔顺每个世界不同（见 PatternRegistry.PerWorldIds），书里只画形状 —— 本世界的画法要靠它学。
/// 只在箱子里找得到（见 HexChestLoot），没有配方。
///
/// 与原版一样只记「是哪个大法术」，内容在读的时候按本世界的笔顺给出
///（源项目「TAG_OP_ID 而没有 TAG_PATTERN 时，在物品栏里补上本世界的图案」）。
/// </summary>
public sealed class AncientScroll : ItemScroll
{
    public override string Texture => "HexCastingTerraria/Content/Items/ScrollLargeAncient";

    public override int BlockSize => 3;

    protected override bool AncientTooltip => true;

    private string _opId = string.Empty;

    public string OpId => _opId;

    public void SetOp(string opId) => _opId = opId;

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.Pink;
        Item.value = Item.sellPrice(gold: 2);
    }

    public override Iota? Read()
    {
        if (base.Read() is { } stored) return stored;
        var def = _opId.Length > 0 ? PatternRegistry.FindById(_opId) : null;
        return def is null ? null : new PatternIota(PatternRegistry.PatternInThisWorld(def));
    }

    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        var def = _opId.Length > 0 ? PatternRegistry.FindById(_opId) : null;
        if (def is not null)
        {
            foreach (var t in tooltips)
            {
                if (t.Name == "ItemName") { t.Text = $"{def.DisplayName()}之远古卷轴"; }
            }
        }
        base.ModifyTooltips(tooltips);
    }

    public override void SaveData(TagCompound tag)
    {
        base.SaveData(tag);
        if (_opId.Length > 0) tag["opId"] = _opId;
    }

    public override void LoadData(TagCompound tag)
    {
        base.LoadData(tag);
        _opId = tag.TryGet("opId", out string id) ? id : string.Empty;
    }

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        base.NetSend(writer);
        writer.Write(_opId);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        base.NetReceive(reader);
        _opId = reader.ReadString();
    }
}

/// <summary>
/// 远古杂件（源项目 ItemAncientCypher，「远古杂件：%s」）：箱子里找到的一次性符纸，封着 16 个预设咒术之一，
/// 自带 32 碎片的媒质（AddHexToAncientCypherFunc 逐字照抄）。
/// 预设咒术里有用到「+Y 朝上」的（升空、起飞……）—— 坐标按原版以后它们也按原版生效。
/// </summary>
public sealed class AncientCypher : ItemPackagedSpell
{

    public override Core.Casting.Eval.PackagedSpellKind Kind => Core.Casting.Eval.PackagedSpellKind.Cypher;

    private string _hexName = string.Empty;

    /// <summary>源项目 LOOT_HEXES：(名字, 图案列表「起始方向 角度」)。</summary>
    public static readonly (string Key, string Name, string[] Patterns)[] Presets =
    {
        ("shatter", "粉碎", new[] { "NORTH_EAST qaq", "EAST aa", "NORTH_EAST qaq", "NORTH_EAST wa", "EAST wqaawdd", "EAST qaqqqqq" }),
        ("kindle", "燃焰", new[] { "NORTH_EAST qaq", "EAST aa", "NORTH_EAST qaq", "NORTH_EAST wa", "EAST wqaawdd", "SOUTH_EAST aaqawawa" }),
        ("illuminate", "耀明", new[] { "NORTH_EAST qaq", "EAST aa", "NORTH_EAST qaq", "NORTH_EAST wa", "EAST aadadaaw", "EAST wqaawdd", "NORTH_EAST ddqdd", "EAST weddwaa", "NORTH_EAST waaw", "NORTH_EAST qqd" }),
        ("growth", "生长", new[] { "NORTH_EAST qaq", "EAST aa", "NORTH_EAST qaq", "NORTH_EAST wa", "EAST aadadaaw", "EAST wqaawdd", "NORTH_EAST ddqdd", "EAST weddwaa", "NORTH_EAST waaw", "SOUTH_EAST aqaaedwd", "EAST aadaadaa", "NORTH_EAST wqaqwawqaqw", "NORTH_EAST wqaqwawqaqw", "NORTH_EAST wqaqwawqaqw" }),
        ("lunge", "猛冲", new[] { "NORTH_EAST qaq", "EAST aadaa", "NORTH_EAST wa", "SOUTH_EAST aqaawa", "SOUTH_EAST waqaw", "SOUTH_WEST awqqqwaqw" }),
        ("sidestep", "侧闪", new[] { "NORTH_EAST qaq", "EAST aadaa", "NORTH_EAST wa", "NORTH_WEST eqqq", "SOUTH_EAST aqaawd", "SOUTH_EAST e", "NORTH_WEST qqqqqew", "SOUTH_WEST eeeeeqw", "SOUTH_EAST awdd", "NORTH_EAST wdedw", "SOUTH_WEST awqqqwaqw" }),
        ("ascend", "升空", new[] { "NORTH_EAST qaq", "SOUTH_EAST aqaae", "WEST qqqqqawwawawd" }),
        ("blink", "闪现", new[] { "NORTH_EAST qaq", "EAST aadaa", "EAST aa", "NORTH_EAST qaq", "NORTH_EAST wa", "EAST wqaawdd", "NORTH_EAST qaq", "EAST aa", "NORTH_WEST wddw", "NORTH_EAST wqaqw", "SOUTH_EAST aqaaw", "NORTH_WEST wddw", "SOUTH_WEST awqqqwaq" }),
        ("blastoff", "起飞", new[] { "NORTH_EAST qaq", "NORTH_WEST qqqqqew", "SOUTH_EAST aqaawaa", "SOUTH_EAST waqaw", "SOUTH_WEST awqqqwaqw" }),
        ("radar", "雷达", new[] { "WEST qqq", "EAST aadaa", "EAST aa", "SOUTH_EAST aqaawa", "SOUTH_WEST ewdqdwe", "NORTH_EAST de", "EAST eee", "NORTH_EAST qaq", "EAST aa", "SOUTH_EAST aqaaeaqq", "SOUTH_EAST qqqqqwdeddwd", "NORTH_EAST dadad" }),
        ("beckon", "召来", new[] { "NORTH_EAST qaq", "EAST aa", "NORTH_EAST qaq", "NORTH_EAST wa", "EAST weaqa", "EAST aadaa", "EAST dd", "NORTH_EAST qaq", "EAST aa", "EAST aawdd", "NORTH_WEST wddw", "EAST aadaa", "NORTH_EAST wqaqw", "NORTH_EAST wdedw", "SOUTH_EAST aqaawa", "SOUTH_EAST waqaw", "SOUTH_WEST awqqqwaqw" }),
        ("detonate", "引爆", new[] { "NORTH_EAST qaq", "EAST aa", "SOUTH_EAST aqaaedwd", "EAST ddwddwdd" }),
        ("shockwave", "冲击波", new[] { "NORTH_EAST qaq", "EAST aa", "SOUTH_EAST aqaawaa", "EAST aadaadaa", "SOUTH_EAST aqawqadaq", "SOUTH_EAST aqaaedwd", "EAST aawaawaa", "NORTH_EAST qqa", "EAST qaqqqqq" }),
        ("heat_wave", "热浪", new[] { "WEST qqq", "SOUTH_EAST aaqawawa", "EAST eee", "NORTH_EAST qaq", "EAST aa", "SOUTH_EAST aqaae", "SOUTH_EAST qqqqqwded", "SOUTH_WEST aaqwqaa", "SOUTH_EAST a", "NORTH_EAST dadad" }),
        ("wither_wave", "凋零波纹", new[] { "WEST qqq", "SOUTH_EAST aqaae", "SOUTH_EAST aqaaw", "SOUTH_WEST qqqqqaewawawe", "EAST eee", "NORTH_EAST qaq", "EAST aa", "SOUTH_EAST aqaae", "SOUTH_EAST qqqqqwdeddwd", "SOUTH_WEST aaqwqaa", "SOUTH_EAST a", "NORTH_EAST dadad" }),
        ("flight_zone", "飞行区", new[] { "NORTH_EAST qaq", "SOUTH_EAST aqaaq", "SOUTH_WEST awawaawq" }),
    };

    /// <summary>源项目 AddHexToAncientCypherFunc.doStatic：随机一个预设 + 32 碎片媒质 + 随机外观。</summary>
    public void Roll(System.Random rand)
    {
        var (key, _, pats) = Presets[rand.Next(Presets.Length)];
        var iotas = new List<Iota>();
        foreach (var s in pats)
        {
            var parts = s.Split(' ');
            var dir = parts[0] switch
            {
                "NORTH_EAST" => HexDir.NorthEast,
                "EAST" => HexDir.East,
                "SOUTH_EAST" => HexDir.SouthEast,
                "SOUTH_WEST" => HexDir.SouthWest,
                "WEST" => HexDir.West,
                _ => HexDir.NorthWest,
            };
            if (HexPattern.TryFromAnglesUnchecked(parts[1], dir, out var p, out _) && p != null) iotas.Add(new PatternIota(p));
        }
        Fill(iotas, 32 * MediaConstants.ShardUnit);
        SetVariant(rand.Next(NumVariantsConst));
        _hexName = key;
    }

    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        foreach (var t in tooltips)
        {
            if (t.Name != "ItemName") continue;
            var preset = System.Array.Find(Presets, p => p.Key == _hexName);
            t.Text = preset.Name is null ? "远古杂件" : $"远古杂件：{preset.Name}";
        }
        base.ModifyTooltips(tooltips);
    }

    public override void SaveData(TagCompound tag)
    {
        base.SaveData(tag);
        tag["hexName"] = _hexName;
    }

    public override void LoadData(TagCompound tag)
    {
        base.LoadData(tag);
        _hexName = tag.TryGet("hexName", out string n) ? n : string.Empty;
    }

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        base.NetSend(writer);
        writer.Write(_hexName);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        base.NetReceive(reader);
        _hexName = reader.ReadString();
    }
}

/// <summary>
/// 故事残卷（源项目 ItemLoreFragment）：读了随机解锁一篇还没读过的传说（咒法学之书的「传说」分类）。
/// 全读过了就提示「似乎我已找齐了此世界上的所有故事。」（原版还给 20 经验，泰拉没有经验）。只在箱子里找得到。
/// </summary>
public sealed class LoreFragment : ModItem
{
    public override void SetDefaults()
    {
        Item.width = 20;
        Item.height = 20;
        Item.maxStack = 9999;
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.consumable = true;
        Item.rare = ItemRarityID.Orange;
        Item.value = Item.sellPrice(silver: 50);
    }

    public override bool? UseItem(Player player)
    {
        if (player.whoAmI != Main.myPlayer) return true;
        var hp = HexPlayer.Get(player);
        string? lore = BookUnlocks.PickUnfoundLore(hp.FoundLore, new System.Random(Main.rand.Next()));
        SoundEngine.PlaySound(SoundID.Item4, player.Center);
        if (lore is null)
        {
            Main.NewText("似乎我已找齐了此世界上的所有故事。", Client.HexColors.Media);
        }
        else
        {
            hp.FoundLore.Add(lore);
            var entry = Client.UI.HexBook.Document.FindEntryByAdvancement(lore);
            Main.NewText($"故事残卷：被阅读 —— 咒法学之书里的「{entry?.DisplayName ?? lore}」已解锁。", Client.HexColors.Media);
        }
        return true;
    }
}
