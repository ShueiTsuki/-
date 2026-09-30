using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Core.Registry;
using HexCastingTerraria.Core.Ui;
using Terraria.ModLoader;

namespace HexCastingTerraria.Addons;

/// <summary>
/// 所有附属的登记表 —— 唯一的总入口（ADDONS.md「目录」）。
/// 新增一个附属 = 在 <see cref="All"/> 里加一行 + 在对应的开关配置里加一个开关。
/// </summary>
public static class AddonRegistry
{
    public static readonly IReadOnlyList<HexAddon> All = new HexAddon[]
    {
        new HexParse.Game.HexParseAddon(),
        new Hexcessible.Game.HexcessibleAddon(),
        new HexDebug.Game.HexDebugAddon(),
    };

    /// <summary>这个附属开着吗？不认识的 id 一律当关着。</summary>
    public static bool IsEnabled(string addonId)
        => All.FirstOrDefault(a => a.Id == addonId) is { IsEnabled: true };

    /// <summary>
    /// 模组加载时调用（开关已由 tML 在加载内容之前读好，联机重载时是服务器的值）：
    /// 声明全部附属的图案（开没开都声明），只启用开着的，再调开着的附属的 OnLoad。
    /// </summary>
    internal static void Load(Mod mod)
    {
        foreach (var addon in All)
        {
            PatternRegistry.DeclareAddonPatterns(addon.Id, addon.Patterns);
            PatternRegistry.SetAddonEnabled(addon.Id, addon.IsEnabled);
            if (addon.IsEnabled) addon.OnLoad(mod);
        }
        mod.Logger.Info("[HexCasting] 附属：" + string.Join("，", All.Select(a => $"{a.Name} {(a.IsEnabled ? "开" : "关")}")));
        foreach (var conflict in PatternRegistry.AddonConflicts)
        {
            mod.Logger.Warn($"[HexCasting] 附属图案撞签名：{conflict}");
        }
    }

    internal static void Unload()
    {
        foreach (var addon in All)
        {
            if (addon.IsEnabled) addon.OnUnload();
        }
        PatternRegistry.ClearAddons();
    }

    /// <summary>建咒法学之书时调用：开着的附属往书里加内容（关着的附属在书里不存在）。</summary>
    public static void AddBookContent(BookDocument book)
    {
        foreach (var addon in All)
        {
            if (addon.IsEnabled) addon.AddBookContent(book);
        }
    }
}
