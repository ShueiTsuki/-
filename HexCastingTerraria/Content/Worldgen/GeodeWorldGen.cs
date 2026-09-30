using System.Collections.Generic;
using HexCastingTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace HexCastingTerraria.Content.Worldgen;

/// <summary>
/// 紫水晶晶洞的世界生成。
///
/// 对应 MC 原生结构 `minecraft:amethyst_geode`（源项目直接用它，没有自己写生成）。
/// 泰拉没有紫水晶晶洞，必须自己生成 —— 否则「挖到粉」这件事无从谈起。
///
/// 结构（2D 简化）：
/// ```
///   ████████████        █ = 母岩（GeodeCore，挖不动、会长的那个）
///   ██        ██        · = 空气（你要挖进来的空腔）
///   █   ◆  ◆   █        ◆ = 成熟晶簇（挖开就能拿）
///   █     ◆    █
///   ██        ██
///   ████████████
/// ```
///
/// 与原版的差异：MC 的晶洞外层还有平滑玄武岩与方解石两层，
/// 那两种石头在泰拉没有对应物，直接省略 —— 不影响「挖开紫色空腔拿水晶」的核心体验。
/// </summary>
public sealed class GeodeWorldGen : ModSystem
{
    /// <summary>每次生成的晶洞数量。</summary>
    private const int GeodesPerWorld = 90;

    /// <summary>晶洞半径范围（图格）。</summary>
    private const int MinRadius = 5;
    private const int MaxRadius = 10;

    /// <summary>母岩外壳厚度。</summary>
    private const int ShellThickness = 2;

    /// <summary>
    /// 插入晶洞生成 pass。
    ///
    /// </summary>
    public override void ModifyWorldGenTasks(List<GenPass> tasks, ref double totalWeight)
    {
        // 插在「Shinies」（矿石）之后 —— 那时地形已经定型，
        // 晶洞不会把已经挖好的洞穴结构再切一遍
        int idx = tasks.FindIndex(t => t.Name == "Shinies");
        if (idx < 0)
        {
            // 找不到就退到列表末尾，宁可放得晚也别不生成
            idx = tasks.Count - 1;
        }

        tasks.Insert(idx + 1, new GeodePass(PlaceGeodes));
    }

    /// <summary>
    /// 自建的生成 pass。
    ///
    /// 为什么不用 `PassLegacy`：它在 1.4.5 的程序集里存在，但**不是公开类型**
    /// （编译器报「找不到类型或命名空间」，而二进制里有这个字符串）。
    /// 直接派生 `GenPass` 更稳妥，也不依赖内部 API。
    /// </summary>
    private sealed class GeodePass : GenPass
    {
        private readonly System.Action<GenerationProgress, GameConfiguration> _apply;

        public GeodePass(System.Action<GenerationProgress, GameConfiguration> apply)
            : base("紫水晶晶洞", 1.0f)
        {
            _apply = apply;
        }

        protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration)
            => _apply(progress, configuration);
    }

    private void PlaceGeodes(GenerationProgress progress, GameConfiguration config)
    {
        progress.Message = "孕育紫水晶晶洞";

        int coreType = ModContent.TileType<GeodeCore>();
        int clusterType = ModContent.TileType<AmethystCluster>();

        // 洞穴层范围：从岩石层底到地狱上方，避开地表与地狱
        int top = (int)Main.rockLayer + 20;
        int bottom = Main.maxTilesY - 260;

        if (bottom <= top)
        {
            return;   // 小世界理论上不会发生，但别让生成崩掉
        }

        for (int n = 0; n < GeodesPerWorld; n++)
        {
            progress.Set(n / (double)GeodesPerWorld);

            int cx = WorldGen.genRand.Next(120, Main.maxTilesX - 120);
            int cy = WorldGen.genRand.Next(top, bottom);

            // 避开已有的大量空腔：晶洞贴着大洞穴会变成一堵悬空的墙
            if (!IsMostlySolid(cx, cy, 3))
            {
                continue;
            }

            int radius = WorldGen.genRand.Next(MinRadius, MaxRadius + 1);
            CarveGeode(cx, cy, radius, coreType, clusterType);
        }
    }

    /// <summary>中心附近是否足够实心（避免晶洞挂在已有洞穴边缘）。</summary>
    private static bool IsMostlySolid(int cx, int cy, int half)
    {
        int solid = 0, total = 0;
        for (int x = cx - half; x <= cx + half; x++)
        {
            for (int y = cy - half; y <= cy + half; y++)
            {
                if (!WorldGen.InWorld(x, y, 1)) continue;
                total++;
                if (Main.tile[x, y].HasTile) solid++;
            }
        }
        // 至少 85% 是实心才在这里放
        return total > 0 && solid >= total * 0.85;
    }

    /// <summary>挖出一个晶洞：外壳是母岩，内部是空腔，内壁挂若干成熟晶簇。</summary>
    private static void CarveGeode(int cx, int cy, int radius, int coreType, int clusterType)
    {
        // 稍微压扁一点，看起来更像「洞」而不是完美的球
        double squash = 0.75 + WorldGen.genRand.NextDouble() * 0.2;

        for (int x = cx - radius - ShellThickness; x <= cx + radius + ShellThickness; x++)
        {
            for (int y = cy - radius - ShellThickness; y <= cy + radius + ShellThickness; y++)
            {
                if (!WorldGen.InWorld(x, y, 1)) continue;

                double dx = (x - cx) / (double)radius;
                double dy = (y - cy) / (double)(radius * squash);
                double d = System.Math.Sqrt(dx * dx + dy * dy);

                if (d > 1.0 + ShellThickness / (double)radius)
                {
                    continue;
                }

                if (d <= 1.0)
                {
                    // 内部：清空
                    Main.tile[x, y].ClearTile();
                }
                else
                {
                    // 外壳：铺母岩。这是这个结构最重要的部分 ——
                    // 母岩会持续长出新的晶簇，所以晶洞是**可再生**的资源点。
                    var tile = Main.tile[x, y];
                    tile.HasTile = true;
                    tile.TileType = (ushort)coreType;
                    tile.LiquidAmount = 0;
                }
            }
        }

        // 内壁挂成熟晶簇：让第一个发现晶洞的玩家立刻有收获，
        // 而不是要等上几十次随机刻。之后新长出来的靠母岩。
        //
        // 做法：**先枚举所有合法位置，再随机挑 N 个**。
        // 早先的写法是「随机取角度 → 算坐标 → 校验」，命中率只有约 30%
        // （算出来的点常常落在壳里或已有方块上），
        // 结果每个晶洞只有 3 个晶簇，玩家挖进来会觉得空。
        var candidates = new List<(int X, int Y)>();

        for (int x = cx - radius - 1; x <= cx + radius + 1; x++)
        {
            for (int y = cy - radius - 1; y <= cy + radius + 1; y++)
            {
                if (!WorldGen.InWorld(x, y, 1)) continue;
                if (Main.tile[x, y].HasTile) continue;                 // 必须是空位
                if (!TouchesGeodeCore(x, y, coreType)) continue;       // 必须贴着母岩
                candidates.Add((x, y));
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        int clusterCount = System.Math.Min(
            WorldGen.genRand.Next(6, 13),
            candidates.Count);

        // 洗牌后取前 N 个（Fisher–Yates），避免全部挤在一侧
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = WorldGen.genRand.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        for (int k = 0; k < clusterCount; k++)
        {
            var (px, py) = candidates[k];
            var tile = Main.tile[px, py];
            tile.HasTile = true;
            tile.TileType = (ushort)clusterType;
        }
    }

    /// <summary>四邻中是否有母岩。</summary>
    private static bool TouchesGeodeCore(int x, int y, int coreType)
    {
        return IsType(x - 1, y, coreType)
            || IsType(x + 1, y, coreType)
            || IsType(x, y - 1, coreType)
            || IsType(x, y + 1, coreType);
    }

    private static bool IsType(int x, int y, int type)
        => WorldGen.InWorld(x, y, 1) && Main.tile[x, y].HasTile && Main.tile[x, y].TileType == type;

    /// <summary>
    /// 生成后自检：把实际放置的图格数写进日志。
    ///
    /// 为什么需要它：世界生成**没有任何界面反馈**，
    /// 光看「没报错」无法区分「生成了 90 个晶洞」和「一个都没放」。
    /// 这条日志让「晶洞确实生成了」成为**可自动验证**的事实，
    /// 而不必开游戏去找（`verify_server.ps1 -autocreate=1` 就能看到）。
    /// </summary>
    public override void PostWorldGen()
    {
        int coreType = ModContent.TileType<GeodeCore>();
        int clusterType = ModContent.TileType<AmethystCluster>();
        int smallType = ModContent.TileType<AmethystBudSmall>();

        int cores = 0, clusters = 0, other = 0;
        int minX = int.MaxValue, maxX = 0, minY = int.MaxValue, maxY = 0;

        for (int x = 0; x < Main.maxTilesX; x++)
        {
            for (int y = 0; y < Main.maxTilesY; y++)
            {
                var tile = Main.tile[x, y];
                if (!tile.HasTile) continue;

                if (tile.TileType == coreType)
                {
                    cores++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                else if (tile.TileType == clusterType)
                {
                    clusters++;
                }
                else if (tile.TileType == smallType)
                {
                    other++;
                }
            }
        }

        // 粗略反推：一个半径 r 的晶洞外壳约 2πr×2 格
        int approxGeodes = cores > 0 ? cores / 90 : 0;

        HexCastingTerraria.Instance?.Logger.Info(
            $"[HexCasting] 晶洞生成自检：母岩 {cores} 格、成熟晶簇 {clusters} 格、小芽 {other} 格"
            + (cores > 0
                ? $"；分布 X[{minX}..{maxX}] Y[{minY}..{maxY}]（约 {approxGeodes} 个晶洞）"
                : "；**未生成任何晶洞**"));
    }
}
