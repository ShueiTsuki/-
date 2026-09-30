using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Terraria;

namespace HexCastingTerraria.Addons.Hexcessible.Game;

/// <summary>
/// Hexcessible 自己记的东西（上游放在配置文件里、界面上不显示的 patternAliases）：图案别名，id → 别名。
/// 存在 存档目录/Hexcessible/aliases.json，全部世界共用（上游也是全局的）。
/// </summary>
public static class HexcessibleStore
{
    private static Dictionary<string, string>? _aliases;

    private static string AliasPath => Path.Combine(Main.SavePath, "Hexcessible", "aliases.json");

    private static Dictionary<string, string> Aliases => _aliases ??= Load(AliasPath);

    public static string? AliasOf(string id) => Aliases.TryGetValue(id, out var v) ? v : null;

    public static void SetAlias(string id, string alias)
    {
        Aliases[id] = alias;
        Save(AliasPath, Aliases);
    }

    public static void Reset() => _aliases = null;

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
