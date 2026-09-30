using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HexCastingTerraria.Addons.Hexcessible.Core;
using Terraria;

namespace HexCastingTerraria.Addons.Hexcessible.Game;

/// <summary>
/// Hexcessible 自己记的东西（上游放在配置文件里、界面上不显示的 patternAliases / knownWorldPatterns）：
/// 图案别名（id → 别名，全部世界共用）存在 存档目录/Hexcessible/aliases.json；
/// 学会的大法术画法（一行「世界 图案id 签名」）存在 存档目录/Hexcessible/known_world_patterns.json。
/// </summary>
public static class HexcessibleStore
{
    private static Dictionary<string, string>? _aliases;
    private static List<string>? _known;

    private static string AliasPath => Path.Combine(Main.SavePath, "Hexcessible", "aliases.json");

    private static string KnownPath => Path.Combine(Main.SavePath, "Hexcessible", "known_world_patterns.json");

    private static List<string> Known => _known ??= LoadList(KnownPath);

    /// <summary>
    /// 上游 Utils.getWorldContext：单人是存档文件夹名，联机是服务器列表里的名字。这里用世界的唯一 id
    /// （单人与联机客户端都拿得到），同一个世界换名字、换服务器地址也还认得。
    /// </summary>
    public static string WorldContext
        => Main.ActiveWorldFileData is { } w ? "world__" + KnownWorldPatterns.Sanitize(w.UniqueId.ToString("N")) : "unknown__";

    /// <summary>这个世界学会的：图案 id → 签名。</summary>
    public static Dictionary<string, string> KnownInThisWorld() => KnownWorldPatterns.ForWorld(Known, WorldContext);

    public static void LearnInThisWorld(string id, string sig)
    {
        _known = KnownWorldPatterns.Learn(Known, WorldContext, id, sig);
        SaveList(KnownPath, _known);
    }

    private static Dictionary<string, string> Aliases => _aliases ??= Load(AliasPath);

    public static string? AliasOf(string id) => Aliases.TryGetValue(id, out var v) ? v : null;

    public static void SetAlias(string id, string alias)
    {
        Aliases[id] = alias;
        Save(AliasPath, Aliases);
    }

    public static void Reset()
    {
        _aliases = null;
        _known = null;
    }

    private static List<string> LoadList(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
                if (loaded != null) return loaded;
            }
        }
        catch (System.Exception e)
        {
            HexCastingTerraria.Instance?.Logger.Warn($"[Hexcessible] {Path.GetFileName(path)} 读不出来，按空的处理：{e.Message}");
        }
        return new List<string>();
    }

    private static void SaveList(string path, List<string> data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (System.Exception e)
        {
            HexCastingTerraria.Instance?.Logger.Warn($"[Hexcessible] {Path.GetFileName(path)} 写不进去：{e.Message}");
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (loaded != null) return loaded;
            }
        }
        catch (System.Exception e)
        {
            HexCastingTerraria.Instance?.Logger.Warn($"[Hexcessible] {Path.GetFileName(path)} 读不出来，按空的处理：{e.Message}");
        }
        return new Dictionary<string, string>();
    }

    private static void Save(string path, Dictionary<string, string> data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (System.Exception e)
        {
            HexCastingTerraria.Instance?.Logger.Warn($"[Hexcessible] {Path.GetFileName(path)} 写不进去：{e.Message}");
        }
    }
}
