using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

// 为新符号寻找「合法 + 不与现有 188 条冲突」的短图案
// 合法性标准与源项目一致：TryFromAngles 的完整校验（不能重走边、不能上溯）

var existing = new HashSet<string>(GeneratedPatternData.All.Select(d => d.Angles));

int maxLen = args.Length > 0 ? int.Parse(args[0]) : 4;

Console.WriteLine($"现有图案签名数: {existing.Count}");
Console.WriteLine($"搜索长度 1..{maxLen} 的合法且未占用的签名");
Console.WriteLine();

string[] chars = { "w", "e", "d", "s", "a", "q" };

void Search(string prefix, int depth, List<string> found, int limit)
{
    if (found.Count >= limit) return;

    if (depth > 0 && IsValid(prefix) && !existing.Contains(prefix))
    {
        found.Add(prefix);
    }
    if (depth >= maxLen) return;

    foreach (var c in chars)
    {
        Search(prefix + c, depth + 1, found, limit);
        if (found.Count >= limit) return;
    }
}

static bool IsValid(string sig)
{
    // 起始方向对合法性有影响，这里对所有方向都试；任一方向合法即认为该签名可用
    foreach (HexDir d in Enum.GetValues<HexDir>())
    {
        if (HexPattern.TryFromAngles(sig, d, out _, out _)) return true;
    }
    return false;
}

var results = new List<string>();
Search("", 0, results, 40);

Console.WriteLine($"找到 {results.Count} 个候选（按长度分组）：");
foreach (var len in new[] { 1, 2, 3, 4 })
{
    var group = results.Where(r => r.Length == len).ToList();
    if (group.Count == 0) continue;
    Console.WriteLine($"  长度 {len}:");
    foreach (var g in group.Take(12))
    {
        // 打印一个合法起始方向
        foreach (HexDir d in Enum.GetValues<HexDir>())
        {
            if (HexPattern.TryFromAngles(g, d, out _, out _))
            {
                Console.WriteLine($"     签名 {g,-8} 起始方向 {d}");
                break;
            }
        }
    }
}
