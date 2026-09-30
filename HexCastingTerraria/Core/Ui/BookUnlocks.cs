using System.Collections.Generic;

namespace HexCastingTerraria.Core.Ui;

/// <summary>玩家在书的解锁上用得到的进度。游戏内由 Client 填，离线测试手填。</summary>
public sealed class BookProgress
{
    /// <summary>拿到过紫水晶（紫晶宝石 / 紫水晶粉 / 紫水晶碎片 / 充能紫水晶；原版 root 进度的条件）。</summary>
    public bool Amethyst { get; set; }

    /// <summary>失败过一次大法术（原版 y_u_no_cast_angy）。</summary>
    public bool FailedGreatSpell { get; set; }

    /// <summary>过载过并活了下来（原版 opened_eyes）。</summary>
    public bool Overcasted { get; set; }

    public bool Enlightened { get; set; }

    /// <summary>读过的传说篇章（原版 lore/* 进度，读「故事残卷」随机获得一篇）。</summary>
    public HashSet<string> FoundLore { get; } = new();

    /// <summary>拿到过媒质立方（原版 creative_unlocker）。</summary>
    public bool MediaCube { get; set; }

    /// <summary>开发者：全部解锁。</summary>
    public bool UnlockAll { get; set; }
}

/// <summary>
/// 条目解锁规则：原版每个条目挂一个 advancement，这里给出泰拉侧的等价条件。
///
/// | 原版进度 | 原版条件 | 泰拉侧 |
/// |---|---|---|
/// | root（57 条） | 背包里有紫水晶粉 / 紫水晶碎片 / 充能紫水晶 | 拿到过紫晶宝石 / 紫水晶粉 / 紫水晶碎片 / 充能紫水晶 |
/// | enlightenment（11） | 启蒙 | 启蒙（Core/Media/Overcast.cs） |
/// | y_u_no_cast_angy（1） | 失败一次大法术 | 同 |
/// | opened_eyes（1） | 过载且活下来 | 同 |
/// | lore/*（8） | 读「故事残卷」（在箱子里找到），**随机**得到一篇没读过的 | 同（残卷放在泰拉的箱子里，见 Content/HexChestLoot.cs） |
///
/// ⚠️ 这里曾经把传说篇章按 Boss 进度逐篇解锁（理由是「泰拉没有遗迹」）—— 泰拉有箱子，按原版改回残卷。
/// </summary>
public static class BookUnlocks
{
    /// <summary>原版 ItemLoreFragment.NAMES。</summary>
    public static readonly string[] LoreIds =
    {
        "hexcasting:lore/cardamom1",
        "hexcasting:lore/cardamom2",
        "hexcasting:lore/cardamom3",
        "hexcasting:lore/cardamom4",
        "hexcasting:lore/cardamom5",
        "hexcasting:lore/experiment1",
        "hexcasting:lore/experiment2",
        "hexcasting:lore/inventory",
    };

    public static bool IsUnlocked(string advancement, BookProgress p)
    {
        if (p.UnlockAll || string.IsNullOrEmpty(advancement)) { return true; }
        switch (advancement)
        {
            case "hexcasting:root": return p.Amethyst;
            case "hexcasting:enlightenment": return p.Enlightened;
            case "hexcasting:y_u_no_cast_angy": return p.FailedGreatSpell;
            case "hexcasting:opened_eyes": return p.Overcasted;
            case "hexcasting:creative_unlocker": return p.MediaCube;
        }
        if (System.Array.IndexOf(LoreIds, advancement) >= 0)
        {
            return p.FoundLore.Contains(advancement);
        }
        // 泰拉侧没有对应物的进度：只有开发者全部解锁时才开
        return false;
    }

    /// <summary>
    /// 原版 ItemLoreFragment.use：打乱顺序，挑第一篇还没读过的。全都读过 → null
    ///（原版提示「似乎我已找齐了此世界上的所有故事。」并给 20 经验，泰拉没有经验）。
    /// </summary>
    public static string? PickUnfoundLore(IReadOnlyCollection<string> found, System.Random rand)
    {
        var shuffled = new List<string>(LoreIds);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rand.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        var set = new HashSet<string>(found);
        foreach (var id in shuffled)
        {
            if (!set.Contains(id)) return id;
        }
        return null;
    }
}
