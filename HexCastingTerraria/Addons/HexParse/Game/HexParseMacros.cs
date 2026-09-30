using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Terraria;

namespace HexCastingTerraria.Addons.HexParse.Game;

/// <summary>
/// 宏与别名（上游 macro/MacroClient.java + MacroManager.java）：存在**客户端**，所有世界通用。
/// - 宏：<c>#名</c> -> 一段代码（展开时再切开）
/// - 别名：不以 # 开头 -> 一个符号
/// 文件：<c>Main.SavePath/HexParse/macro.json</c>。上游进服时把宏发给服务端，因为它在服务端解析；
/// 泰拉侧解析就在客户端做，所以不用发。数量上限 1024，单条上限 1024 字符（同上游）。
/// </summary>
public static class HexParseMacros
{
    public const int MaxCount = 1024;
    public const int MaxSingleSize = 1024;

    private static Dictionary<string, string>? _pool;

    private static string FilePath => Path.Combine(Main.SavePath, "HexParse", "macro.json");

    private static Dictionary<string, string> Pool => _pool ??= Load();

    public static bool IsMacro(string key) => key.StartsWith("#", System.StringComparison.Ordinal);

    public static string? Get(string key) => Pool.TryGetValue(key, out var v) ? v : null;

    public static bool Contains(string key) => Pool.ContainsKey(key);

    public static List<KeyValuePair<string, string>> List(bool macros)
        => Pool.Where(kv => IsMacro(kv.Key) == macros).OrderBy(kv => kv.Key, System.StringComparer.Ordinal).ToList();

    /// <summary>上游 willThisExceedLimit。</summary>
    public static bool WouldExceedLimit(string key) => !Pool.ContainsKey(key) && Pool.Count >= MaxCount;

    public static void Define(string key, string value)
    {
        Pool[key] = value;
        Save();
    }

    public static void Remove(string key)
    {
        if (Pool.Remove(key)) Save();
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
                if (loaded != null) return loaded;
            }
        }
        catch (System.Exception e)
        {
            HexCastingTerraria.Instance?.Logger.Warn($"[HexParse] 宏文件读不出来，按空的处理：{e.Message}");
        }
        return new Dictionary<string, string>();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Pool, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (System.Exception e)
        {
            HexCastingTerraria.Instance?.Logger.Warn($"[HexParse] 宏文件写不进去：{e.Message}");
        }
    }
}
