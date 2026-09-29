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

    /// <summary>已达成的泰拉里程碑（见 <see cref="BookUnlocks.LoreMilestones"/>）。</summary>
    public HashSet<string> Milestones { get; } = new();

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
/// | lore/*（8） | 在遗迹箱子里捡到传说残页 | **泰拉没有这些遗迹** → 按 Boss 进度逐篇解锁（见下表） |
///
/// 传说残页是一段按顺序读的故事（卡达蒙的日记 → 实验记录 → 遗物清单），
/// 所以把它们按顺序挂在泰拉的 Boss 里程碑上：肉前 4 篇、肉后 3 篇、月后 1 篇。
/// </summary>
public static class BookUnlocks
{
    public static readonly (string Advancement, string Milestone, string Description)[] LoreMilestones =
    {
        ("hexcasting:lore/cardamom1", "boss1", "击败克苏鲁之眼"),
        ("hexcasting:lore/cardamom2", "boss2", "击败世界吞噬怪或克苏鲁之脑"),
        ("hexcasting:lore/cardamom3", "boss3", "击败骷髅王"),
        ("hexcasting:lore/cardamom4", "hardmode", "击败血肉墙（进入肉后）"),
        ("hexcasting:lore/cardamom5", "mech", "击败任意一个机械 Boss"),
        ("hexcasting:lore/experiment1", "plantera", "击败世纪之花"),
        ("hexcasting:lore/experiment2", "golem", "击败石巨人"),
        ("hexcasting:lore/inventory", "moonlord", "击败月亮领主"),
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
        }
        foreach (var (adv, milestone, _) in LoreMilestones)
        {
            if (adv == advancement) { return p.Milestones.Contains(milestone); }
        }
        // 泰拉侧没有对应物的进度（如创造模式物品）：只有开发者全部解锁时才开
        return false;
    }
}
