using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Iotas;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 法术书。对应源项目 `hexcasting:spellbook`（ItemSpellbook，**不是**那本引导书）。
///
/// 一本 64 页的笔记本，每页存**一个任意** iota（原版 canWrite 不看类型）。规则逐条照原版：
///   - 页码 1..64；整本是空的时候页码是 0（「空白」），只能停在那里；翻页不回卷（到头就停）
///   - **潜行 + 滚轮翻页**（原版 MsgShiftScrollC2S；手上或「另一只手」都行，见 HexPlayer.SetControls）
///   - **密封**当前页：和蜂巢合成（原版和蜜脾合成，SealThingsRecipe）。封了写不进
///   - `erase` 封了也能清，清掉当前页的同时解封（原版 writeDatum(null) 连 sealed 一起删）
///   - 没有右键功能（原版 ItemSpellbook 没有 use）
///
/// 原版会把物品的自定义名字存成页名（铁砧改名），泰拉的物品不能改名，这一条不做。
/// </summary>
public sealed class Spellbook : ItemIotaStorage, IHexVariantItem
{
    /// <summary>最大页数。源项目 `MAX_PAGES = 64`。</summary>
    public const int MaxPages = 64;

    public const int Variants = 8;

    // 写时复制：tModLoader 克隆物品时共享引用字段，就地改会让两本书串页 —— 任何修改都整体换新集合
    [CloneByReference]
    private Dictionary<int, Iota> _pages = new();

    [CloneByReference]
    private HashSet<int> _sealed = new();

    /// <summary>选中的页（原版 TAG_SELECTED_PAGE，0..64；0 只在整本空白时出现）。</summary>
    private int _selected;

    public int Variant { get; private set; }

    public int NumVariants => Variants;

    public void SetVariant(int variant)
    {
        if (!IsSealed) Variant = System.Math.Clamp(variant, 0, Variants - 1);
    }

    /// <summary>原版 getPage(ifEmpty)：整本空白 → ifEmpty；否则选中页（0 当 1）。</summary>
    public int GetPage(int ifEmpty) => _pages.Count == 0 ? ifEmpty : System.Math.Max(1, _selected);

    /// <summary>当前页是否被封住（原版 isSealed：按 getPage(1)）。</summary>
    public bool IsSealed => _sealed.Contains(GetPage(1));

    /// <summary>最高有内容的页码（0 = 整本都是空的）。对应源项目 `highestPage`。</summary>
    public int HighestPage
    {
        get
        {
            int highest = 0;
            foreach (int page in _pages.Keys) highest = System.Math.Max(highest, page);
            return highest;
        }
    }

    public override Iota? Read() => _pages.TryGetValue(GetPage(1), out var value) ? value : null;

    public override bool Writeable => !IsSealed;

    public override bool CanWrite(Iota? datum) => datum == null || !IsSealed;

    protected override void WriteDatum(Iota? datum)
    {
        int page = GetPage(1);
        if (datum == null)
        {
            var pages = new Dictionary<int, Iota>(_pages);
            pages.Remove(page);
            _pages = pages;
            var seals = new HashSet<int>(_sealed);
            seals.Remove(page);
            _sealed = seals;
        }
        else if (!IsSealed)
        {
            _pages = new Dictionary<int, Iota>(_pages) { [page] = datum };
            // 原版 inventoryTick 每刻把 getPage(0) 写回 TAG_SELECTED_PAGE：第一次写进空书时选中页从 0 变 1
            _selected = page;
        }
    }

    /// <summary>密封当前页（原版 setSealed(stack, true)）。</summary>
    public void SealCurrentPage() => _sealed = new HashSet<int>(_sealed) { GetPage(1) };

    /// <summary>原版 rotatePageIdx：空白书停在 0；否则 ±1，最小 1，最大 64（不回卷）。返回新页码。</summary>
    public int RotatePage(bool increase)
    {
        int idx = GetPage(0);
        if (idx != 0)
        {
            idx += increase ? 1 : -1;
            idx = System.Math.Max(1, idx);
        }
        _selected = System.Math.Clamp(idx, 0, MaxPages);
        return _selected;
    }

    /// <summary>原版 tooltip.spellbook.page(.sealed) / empty(.sealed)。</summary>
    public string PageLine()
    {
        int highest = HighestPage;
        string seal = IsSealed ? "（已密封）" : "";
        return highest == 0 ? $"空白{seal}" : $"所选书页 {GetPage(0)}/{highest}{seal}";
    }

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.Pink;
        Item.value = Item.sellPrice(gold: 2);
    }

    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        tooltips.Add(new TooltipLine(Mod, "HexSpellbookPage", PageLine()));
        base.ModifyTooltips(tooltips);
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:119-129，shaped：
        //     NBA        N = 金粒 ×3        → 泰拉没有金粒，折成金锭 ×1
        //     NFA        B = 可写书 ×1
        //     NBA        A = 充能紫水晶 ×2
        //                F = **合唱果 ×1**
        //
        // 阶段：**肉后**。合唱果是末地特产，泰拉最接近的中间阶段是肉后；
        // 用「水晶碎块」代替合唱果：同为异界的结晶，同为肉后魔法材料的代表。
        CreateRecipe()
            .AddIngredient(ItemID.Book, 1)                  // 可写书
            .AddIngredient(ItemID.GoldBar, 1)               // 金粒 ×3
            .AddIngredient<ChargedAmethyst>(2)
            .AddIngredient(ItemID.CrystalShard, 5)          // 合唱果 → 水晶碎块
            .AddTile(TileID.Bookcases)
            .Register();

        // 原版 SealThingsRecipe.SPELLBOOK：当前页有内容且没封
        SealRecipes.Add(this, b => b is Spellbook book && book.Read() != null && !book.IsSealed,
            b => ((Spellbook)b).SealCurrentPage());
    }

    // ── 存档 ───────────────────────────────────────────────────────

    public override void SaveData(TagCompound tag)
    {
        tag["page"] = _selected;
        var pages = new TagCompound();
        foreach (var kv in _pages) pages[kv.Key.ToString()] = Content.Net.IotaTag.ToTag(kv.Value);
        tag["pages"] = pages;
        tag["sealed"] = new List<int>(_sealed);
        tag["variant"] = Variant;
    }

    public override void LoadData(TagCompound tag)
    {
        _selected = System.Math.Clamp(tag.GetInt("page"), 0, MaxPages);
        var loaded = new Dictionary<int, Iota>();
        if (tag.TryGet("pages", out TagCompound pages))
        {
            foreach (var kv in pages)
            {
                // 页码解析失败就跳过这一条，**不要把整本书读废**
                if (!int.TryParse(kv.Key, out int index) || index < 1 || index > MaxPages) continue;
                if (Content.Net.IotaTag.TryFromTag(kv.Value, out var iota))
                {
                    loaded[index] = iota;
                }
            }
        }
        _pages = loaded;
        var seals = new HashSet<int>();
        if (tag.TryGet("sealed", out List<int> sealedPages))
        {
            foreach (int index in sealedPages)
            {
                if (index >= 1 && index <= MaxPages) seals.Add(index);
            }
        }
        _sealed = seals;
        Variant = System.Math.Clamp(tag.GetInt("variant"), 0, Variants - 1);
    }

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        writer.Write((byte)_selected);
        writer.Write((byte)_pages.Count);
        foreach (var kv in _pages)
        {
            writer.Write((byte)kv.Key);
            Net.IotaWire.Write(writer, kv.Value);
        }
        writer.Write((byte)_sealed.Count);
        foreach (int page in _sealed) writer.Write((byte)page);
        writer.Write((byte)Variant);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        _selected = System.Math.Clamp((int)reader.ReadByte(), 0, MaxPages);
        var pages = new Dictionary<int, Iota>();
        int count = reader.ReadByte();
        for (int i = 0; i < count; i++)
        {
            int index = reader.ReadByte();
            var iota = Net.IotaWire.Read(reader);
            if (index >= 1 && index <= MaxPages) pages[index] = iota;
        }
        _pages = pages;
        var seals = new HashSet<int>();
        int sealedCount = reader.ReadByte();
        for (int i = 0; i < sealedCount; i++) seals.Add(reader.ReadByte());
        _sealed = seals;
        Variant = System.Math.Clamp((int)reader.ReadByte(), 0, Variants - 1);
    }
}
