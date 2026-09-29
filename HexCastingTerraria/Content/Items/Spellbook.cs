using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Iotas;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 法术书。对应源项目 `hexcasting:spellbook`（**不是**那本引导书 `thehexbook`）。
///
/// ## 它是什么：一本 64 页的咒术笔记本
///
/// ⚠️ 关键事实（读源码得到的，与直觉不同）：
/// **法术书不能直接施放**。`ItemSpellbook` 只实现了 `IotaHolderItem`，
/// **没有 `use` 覆写** —— 它不是法器，是**存储**。
/// 用法是：用 `read` 把某一页的图案列表读出来，再 `eval` 它。
///
/// 这一点很容易做错：想当然地给它加个「右键施放」，
/// 就等于凭空造了一个原版没有的机制。
///
/// ## 页与封印
///
/// - 最多 **64 页**（`MAX_PAGES`），页码 1 起；**0 表示书是空的**（源项目用 0/0 表达"空书"）
/// - 每页存**一串图案**（我们限制成 `PatternListOnly`，与源项目一致：
///   写进去的东西要不是图案列表就不收）
/// - **封印**（sealed）：封住的页拒绝 `write` 与 `erase`，防止手滑覆盖掉存好的法术
///
/// ## 与源项目的交互差异（必须有）
///
/// 源项目里「翻页」和「封印」都是**键位**（`rotatePageIdx` / `setSealed` 由按键处理调用）。
/// 泰拉这边物品的「使用」是右键，所以映射成：
///   - **右键** = 翻到下一页（到 64 后回到 1）
///   - **潜行 + 右键** = 封 / 解封当前页
/// 语义一一对应，只是触发方式换成了泰拉的习惯。
///
/// 源项目还会把「物品的自定义名字」存成页名，泰拉的物品没有自定义名，这一条不做（不影响机制）。
/// </summary>
public sealed class Spellbook : ItemIotaStorage
{
    /// <summary>最大页数。源项目 `MAX_PAGES = 64`。</summary>
    public const int MaxPages = 64;

    // ── 为什么这两个集合是「写时复制」而不是就地修改 ──────────────
    //
    // tModLoader 在克隆物品时会**共享引用字段**（并且会对带引用字段的 ModItem 发警告），
    // 而 1.4.5 的 `ModItem` **没有** Clone 钩子让我们深拷贝。
    // 如果 `_pages` 被就地修改（`_pages[page] = x`），两个共享它的书会互相串页 ——
    // 而且完全不报错。
    //
    // 所以这里的约定是：**任何修改都整体换成新集合**，绝不就地改。
    // 这样共享一个引用就永远安全（对方看到的是旧快照），
    // 于是可以正大光明地标 `[CloneByReference]` 说明「我们想过这件事」。

    /// <summary>页码 -> 该页的图案列表。缺键 = 空页。**只整体替换，不就地修改**。</summary>
    [CloneByReference]
    private Dictionary<int, Iota> _pages = new();

    /// <summary>被封住的页码。同样只整体替换。</summary>
    [CloneByReference]
    private HashSet<int> _sealed = new();

    /// <summary>当前页（1..MaxPages）。</summary>
    public int CurrentPage { get; private set; } = 1;

    /// <summary>页里只能放图案列表。对应源项目 `writeable` / `canWrite` 的约束。</summary>
    public override StorageKind StorageKind => StorageKind.PatternListOnly;

    /// <summary>当前页是否被封住。</summary>
    public bool IsSealed => _sealed.Contains(CurrentPage);

    /// <summary>最高有内容的页码（0 = 整本都是空的）。对应源项目 `highestPage`。</summary>
    public int HighestPage
    {
        get
        {
            int highest = 0;
            foreach (int page in _pages.Keys)
            {
                if (page > highest) highest = page;
            }
            return highest;
        }
    }

    /// <summary>当前页的内容（读不出来就是 null）。</summary>
    public override Iota? Read()
        => _pages.TryGetValue(CurrentPage, out var value) ? value : null;

    /// <summary>写入当前页。封住的页拒绝写入 —— 这是封印的全部意义。</summary>
    public override bool TryStore(Iota value)
    {
        if (IsSealed) return false;
        if (!CanStore(value)) return false;

        // 写时复制：整体换一份新字典（理由见字段处的注释）
        _pages = new Dictionary<int, Iota>(_pages) { [CurrentPage] = value };
        return true;
    }

    /// <summary>清空当前页。封住的页同样拒绝。</summary>
    public override bool Clear()
    {
        if (IsSealed) return false;
        if (!_pages.ContainsKey(CurrentPage)) return false;

        var copy = new Dictionary<int, Iota>(_pages);
        copy.Remove(CurrentPage);
        _pages = copy;
        return true;
    }

    // ── 翻页与封印 ─────────────────────────────────────────────────

    /// <summary>翻到下一页，到顶回到 1。对应源项目 `rotatePageIdx(increase: true)` 的钳制语义。</summary>
    public void NextPage()
    {
        CurrentPage = CurrentPage >= MaxPages ? 1 : CurrentPage + 1;
    }

    /// <summary>封 / 解封当前页。对应源项目 `setSealed`。</summary>
    public bool ToggleSeal()
    {
        var copy = new HashSet<int>(_sealed);

        if (copy.Contains(CurrentPage))
        {
            copy.Remove(CurrentPage);
            _sealed = copy;
            return false;
        }

        copy.Add(CurrentPage);
        _sealed = copy;
        return true;
    }

    // ── 物品行为 ───────────────────────────────────────────────────

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 15;
        Item.useAnimation = 15;
        Item.UseSound = null;
        Item.rare = ItemRarityID.Pink;
        Item.value = Item.sellPrice(gold: 2);
    }

    public override bool? UseItem(Player player)
    {
        if (player.whoAmI != Main.myPlayer) return true;

        bool shift =
            Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) ||
            Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift);

        if (shift)
        {
            bool sealedNow = ToggleSeal();
            Client.HexCanvasState.SetMessage(sealedNow
                ? $"第 {CurrentPage} 页已封印（write / erase 会被拒绝）"
                : $"第 {CurrentPage} 页已解封");
            return true;
        }

        NextPage();

        Iota? content = Read();
        string summary = content is ListIota list ? $"{list.Count} 个图案" : "空页";
        Client.HexCanvasState.SetMessage($"法术书 第 {CurrentPage} / {MaxPages} 页 · {summary}");

        return true;
    }

    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        int highest = HighestPage;

        tooltips.Add(new TooltipLine(Mod, "HexSpellbookPage",
            $"第 {CurrentPage} / {MaxPages} 页{(highest > 0 ? $"（已写到第 {highest} 页）" : "（整本还是空的）")}"));

        if (IsSealed)
        {
            tooltips.Add(new TooltipLine(Mod, "HexSpellbookSealed", "已封印：本页不能被 write / erase 改动"));
        }

        Iota? content = Read();
        tooltips.Add(new TooltipLine(Mod, "HexSpellbookContent", content is ListIota list
            ? $"本页存有 {list.Count} 个图案"
            : "本页是空的：用 write 写入一串图案"));

        tooltips.Add(new TooltipLine(Mod, "HexSpellbookHint", "右键翻页 · 潜行右键 封/解封"));
        tooltips.Add(new TooltipLine(Mod, "HexSpellbookReadHint", "读取用法：read 读出本页 → eval 执行"));
    }

    public override void AddRecipes()
    {
        // 源 HexplatRecipes.java:119-129，shaped：
        //     NBA        N = 金粒 ×3        → 泰拉没有金粒，折成金锭 ×1
        //     NFA        B = 可写书 ×1
        //     NBA        A = 充能紫水晶 ×2
        //                F = **合唱果 ×1**
        //
        // 阶段：**肉后**。源项目那个合唱果是末地特产（代码里那句
        // "i wanna gate this behind the end SOMEHOW" 就是本人在承认这个门槛），
        // 但泰拉没有「末地」这一档，最接近的中间阶段是**肉后**；
        // 而泰拉的**魔法书**（水球术→黄金雨→水晶风暴）恰好也是这个走向 ——
        // 所以用「水晶碎块」代替合唱果：同为异界的结晶，同为肉后魔法材料的代表。
        CreateRecipe()
            .AddIngredient(ItemID.Book, 1)                  // 可写书
            .AddIngredient(ItemID.GoldBar, 1)               // 金粒 ×3
            .AddIngredient<ChargedAmethyst>(2)
            .AddIngredient(ItemID.CrystalShard, 5)          // 合唱果 → 水晶碎块
            .AddTile(TileID.Bookcases)
            .Register();
    }

    // ── 存档 ───────────────────────────────────────────────────────

    public override void SaveData(TagCompound tag)
    {
        tag["page"] = CurrentPage;

        // 页内容：key 是页码字符串 —— 与源项目的 TAG_PAGES 结构一致
        var pages = new TagCompound();
        foreach (var kv in _pages)
        {
            pages[kv.Key.ToString()] = kv.Value.Serialize();
        }
        tag["pages"] = pages;

        var sealedList = new List<int>(_sealed);
        tag["sealed"] = sealedList;
    }

    public override void LoadData(TagCompound tag)
    {
        _pages = new Dictionary<int, Iota>();
        _sealed = new HashSet<int>();
        CurrentPage = 1;

        if (tag.TryGet("page", out int page))
        {
            CurrentPage = System.Math.Clamp(page, 1, MaxPages);
        }

        // 读档时先在局部变量里攒，最后一次性赋值 —— 保持「只整体替换」的约定
        var loaded = new Dictionary<int, Iota>();

        if (tag.TryGet("pages", out TagCompound pages))
        {
            foreach (var kv in pages)
            {
                // 页码解析失败就跳过这一条，**不要把整本书读废**
                if (!int.TryParse(kv.Key, out int index)) continue;
                if (index < 1 || index > MaxPages) continue;
                if (kv.Value is not TagCompound envelope) continue;

                if (IotaSerializer.TryDeserialize(envelope, out var iota))
                {
                    loaded[index] = iota;
                }
            }
        }

        _pages = loaded;

        var loadedSealed = new HashSet<int>();
        if (tag.TryGet("sealed", out List<int> sealedPages))
        {
            foreach (int index in sealedPages)
            {
                if (index >= 1 && index <= MaxPages) loadedSealed.Add(index);
            }
        }
        _sealed = loadedSealed;
    }
}
