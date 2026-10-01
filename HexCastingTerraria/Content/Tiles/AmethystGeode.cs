using HexCastingTerraria.Core.World;
using Microsoft.Xna.Framework;
using HexCastingTerraria.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using HexCastingTerraria.Config;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 晶洞母岩。对应 MC 的 `budding_amethyst`。
///
/// 行为：
///   - **能挖坏，但什么都不掉**（原版 budding_amethyst：任何镐都能挖、炸药能炸，精准采集也不掉，所以没法搬走）。
///     2026-10-01 之前移植版做成挖不动，用户定照原版改回来
///   - 每次随机刻有 <see cref="AmethystLoot.TickChanceDenominator"/> 分之一的概率
///     让**相邻的**一个晶簇生长一级（空 → 小芽 → 中芽 → 大芽 → 成熟晶簇）
///
/// 注意：2D 适配说明：源项目向**六个**方向生长，泰拉侧向**四个**方向（上下左右）。
/// 晶簇的贴图不区分朝向（统一画成向上的晶体）—— 这是有意的简化，
/// 换来的是不必碰 `TileFrameX` 分帧（那会让方块脱离随机刻体系）。
/// </summary>
public sealed class GeodeCore : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileLighted[Type] = true;

        // 必须是 false，否则不会被随机刻更新 —— 生长逻辑就永远不会触发。
        // 这也是为什么晶簇另开方块类型：晶簇需要 tileFrameImportant，
        // 两者不能是同一个类型。
        Main.tileFrameImportant[Type] = false;

        Main.tileMergeDirt[Type] = false;
        Main.tileOreFinderPriority[Type] = 410;   //  spelunker 之类的探测优先级

        MinPick = 0;                               // 原版硬度 1.5，和石头一样：任何镐都能挖
        MineResist = 1f;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Tink;
        AddMapEntry(new Color(126, 78, 178));
    }

    /// <summary>原版 budding_amethyst 挖掉什么都不掉（精准采集也不掉）。</summary>
    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem) => noItem = true;

    /// <summary>
    /// 随机刻：尝试让一个相邻位置生长一级。
    ///
    /// 移植自 MC 的 `BuddingAmethystBlock.randomTick`：
    ///   1. `rand.nextInt(5) == 0` 才继续（1/5 概率）
    ///   2. 随机挑一个方向
    ///   3. 该方向若是空位 → 长出小芽；若已是某一级芽 → 升一级；已成熟或是别的方块 → 不动
    ///      （第 3 步与紫水晶种植盆共用，见 <see cref="AmethystGrowth.GrowAt"/>）
    /// </summary>
    public override void RandomUpdate(int i, int j)
    {
        // 调试开关：晶簇立即长成（跳过随机判定）
        if (!AmethystGrowth.InstantGrowth && !AmethystLoot.RollGrowth(Main.rand.Next))
        {
            return;
        }

        // 随机挑一个方向（上/下/左/右）
        int dir = Main.rand.Next(AmethystLoot.GrowthDirections);
        int ti = i, tj = j;
        switch (dir)
        {
            case AmethystLoot.DirectionUp: tj -= 1; break;   // 上
            case 1: tj += 1; break;   // 下
            case 2: ti -= 1; break;   // 左
            default: ti += 1; break;  // 右
        }

        AmethystGrowth.GrowAt(ti, tj);
    }
}

/// <summary>
/// 晶簇的公共基类（小芽 / 中芽 / 大芽 / 成熟晶簇）。
///
/// 四个阶段共用一套行为，区别只在**掉落**：
/// 只有成熟晶簇掉东西，三个芽阶段**什么都不掉**
/// （源项目的掉落注入只挂在 `minecraft:blocks/amethyst_cluster` 上）。
/// </summary>
public abstract class AmethystGrowth : ModTile
{
    /// <summary>本阶段。</summary>
    public abstract AmethystStage Stage { get; }

    public override void SetStaticDefaults()
    {
        // 晶簇不是实心方块：可以直接走过去，否则晶洞里会被自己的晶簇堵住
        Main.tileSolid[Type] = false;
        Main.tileBlockLight[Type] = false;
        Main.tileLighted[Type] = true;
        Main.tileFrameImportant[Type] = false;
        Main.tileNoAttach[Type] = true;
        // 原版要对着晶簇本身挖（用镐掉得多）。之前用了泰拉「草」的机制（tileCut），挥一下就能隔着方块打掉，
        // 没长成的芽也会被顺手打掉白等一轮 —— 2026-10-01 用户定照原版：要用镐挖它本身
        Main.tileCut[Type] = false;
        MinPick = 0;
        MineResist = 0.5f;

        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Shatter;
        AddMapEntry(new Color(168, 120, 220));
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        // 晶簇自发光，洞穴里一眼能看到 —— 原版晶洞也是靠这个辨认的
        float strength = Stage == AmethystStage.Cluster ? 0.55f : 0.25f;
        r = 0.62f * strength;
        g = 0.42f * strength;
        b = 0.95f * strength;
    }

    /// <summary>调试开关「晶簇立即长成」：跳过随机判定，每次随机刻都长一级（母岩与种植盆都认）。</summary>
    internal static bool InstantGrowth => HexClientConfig.Instance.InstantCrystalGrowth;

    /// <summary>
    /// 让 (x, y) 这一格长一级。母岩（<see cref="GeodeCore"/>）和紫水晶种植盆（<see cref="AmethystPlanter"/>）共用，
    /// 规则在 <see cref="AmethystLoot.GrowInto"/>：空位长出小芽、芽升一级，成熟晶簇和别的方块不动。
    /// 服务端改完要发 SendTileSquare，否则联机下只有服务端看得见生长 ——
    /// 2026-10-01 之前「空位长出小芽」这一步没发，客户端要等它长到中芽才看得见。
    /// </summary>
    internal static void GrowAt(int x, int y)
    {
        if (!WorldGen.InWorld(x, y, 1))
        {
            return;
        }

        var tile = Main.tile[x, y];
        var next = AmethystLoot.GrowInto(!tile.HasTile, tile.HasTile ? TileToStage(tile.TileType) : AmethystStage.None);
        if (next is not { } stage)
        {
            return;
        }

        int type = StageToTile(stage);
        if (!tile.HasTile)
        {
            // 新芽走一遍放置流程（取帧、查支撑）；放不下就算了
            if (!WorldGen.PlaceTile(x, y, type, mute: true))
            {
                return;
            }
        }
        else
        {
            tile.TileType = (ushort)type;
        }

        if (Main.netMode == NetmodeID.Server)
        {
            NetMessage.SendTileSquare(-1, x, y, 1);
        }
    }

    /// <summary>方块类型 → 生长阶段。不是晶簇的方块返回 <see cref="AmethystStage.None"/>。</summary>
    internal static AmethystStage TileToStage(int tileType)
    {
        if (tileType == ModContent.TileType<AmethystBudSmall>()) return AmethystStage.SmallBud;
        if (tileType == ModContent.TileType<AmethystBudMedium>()) return AmethystStage.MediumBud;
        if (tileType == ModContent.TileType<AmethystBudLarge>()) return AmethystStage.LargeBud;
        if (tileType == ModContent.TileType<AmethystCluster>()) return AmethystStage.Cluster;
        return AmethystStage.None;
    }

    /// <summary>生长阶段 → 方块类型。0 表示该阶段没有对应方块。</summary>
    internal static int StageToTile(AmethystStage stage) => stage switch
    {
        AmethystStage.SmallBud => ModContent.TileType<AmethystBudSmall>(),
        AmethystStage.MediumBud => ModContent.TileType<AmethystBudMedium>(),
        AmethystStage.LargeBud => ModContent.TileType<AmethystBudLarge>(),
        AmethystStage.Cluster => ModContent.TileType<AmethystCluster>(),
        _ => 0,
    };

    /// <summary>
    /// 原版：晶簇贴着长出它的那块方块，那块没了它就碎掉（按「没用对工具」掉落）。
    /// 移植版不记朝向（贴图统一朝上），所以只要上下左右还有一块能长出它的方块就算贴着。
    /// 紫水晶种植盆只长朝上那一面，所以只有盆的**正上方**算贴着（盆的旁边、下面都不算）。
    /// </summary>
    public override bool TileFrame(int i, int j, ref bool resetFrame, ref bool noBreak)
    {
        if (!IsSupported(i, j))
        {
            _brokenBySupportLoss = true;
            try { WorldGen.KillTile(i, j); }
            finally { _brokenBySupportLoss = false; }
            return false;
        }
        return true;
    }

    /// <summary>能向四面长出晶簇的方块：母岩。</summary>
    internal static bool IsGrower(int i, int j) => IsTileOf<GeodeCore>(i, j);

    /// <summary>只向上长出晶簇的方块：紫水晶种植盆。</summary>
    internal static bool IsPlanter(int i, int j) => IsTileOf<AmethystPlanter>(i, j);

    private static bool IsTileOf<T>(int i, int j) where T : ModTile
        => WorldGen.InWorld(i, j) && Main.tile[i, j] is { HasTile: true } t && t.TileType == ModContent.TileType<T>();

    private static bool IsSupported(int i, int j)
        => IsGrower(i, j + 1) || IsGrower(i, j - 1) || IsGrower(i - 1, j) || IsGrower(i + 1, j)
           || IsPlanter(i, j + 1);

    [System.ThreadStatic] private static bool _brokenBySupportLoss;

    /// <summary>
    /// 是谁拿着镐在挖这一格（原版的「工具合格」= 用镐挖它本身）。爆炸、法术、贴着的方块没了 → null（按没用对工具掉）。
    /// 单机 / 本地客户端看本人瞄着的格子；服务端不知道别人瞄着哪儿，就找附近正在挥镐的玩家。
    /// </summary>
    private static Player? Miner(int i, int j)
    {
        if (_brokenBySupportLoss) return null;
        var center = new Vector2(i * 16 + 8, j * 16 + 8);
        if (Main.netMode != NetmodeID.Server)
        {
            var p = Main.LocalPlayer;
            return p is { active: true, dead: false } && p.itemAnimation > 0 && IsPickaxe(p.HeldItem)
                   && Player.tileTargetX == i && Player.tileTargetY == j ? p : null;
        }
        Player? best = null;
        float bestD = 16f * 12f;
        foreach (var p in Main.ActivePlayers)
        {
            if (p.dead || p.itemAnimation <= 0 || !IsPickaxe(p.HeldItem)) continue;
            float d = Vector2.Distance(p.Center, center);
            if (d < bestD) { best = p; bestD = d; }
        }
        return best;
    }

    /// <summary>
    /// 掉落。**芽阶段不掉任何东西** —— 提前敲掉就白等一轮生长。
    /// 成熟晶簇的掉落严格按 <see cref="AmethystLoot.RollCluster"/> 的四个池。
    /// 掉落只在单机 / 服务端生成（联机客户端也会跑这个钩子，在那边生成会多掉一份）。
    /// </summary>
    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly || noItem)
        {
            return;
        }

        // 芽阶段：直接什么都不掉
        if (!AmethystLoot.DropsLoot(Stage) || Main.netMode == NetmodeID.MultiplayerClient)
        {
            noItem = true;
            return;
        }

        // 泰拉没有「精准采集」，永远走非精准采集分支（见 TODO_PLAN.md 的说明）。
        // 工具是否「合格」= 有人拿着镐在挖它本身。
        var miner = Miner(i, j);
        bool properTool = miner is not null;

        int fortune = properTool
            ? AmethystLoot.FortuneFromPickaxePower(miner!.HeldItem.pick)
            : 0;

        var loot = AmethystLoot.RollCluster(
            properTool, fortune,
            n => Main.rand.Next(n),
            p => Main.rand.NextDouble() < p,
            vanillaShardBase: 1);

        noItem = true;   // 关掉默认掉落，全部自己生成

        var source = new Terraria.DataStructures.EntitySource_TileBreak(i, j);

        if (loot.Dust > 0)
        {
            Item.NewItem(source, new Microsoft.Xna.Framework.Vector2(i * 16, j * 16), new Microsoft.Xna.Framework.Vector2(16, 16),
                ModContent.ItemType<Items.AmethystDust>(), loot.Dust);
        }

        if (loot.ChargedCrystal > 0)
        {
            Item.NewItem(source, new Microsoft.Xna.Framework.Vector2(i * 16, j * 16), new Microsoft.Xna.Framework.Vector2(16, 16),
                ModContent.ItemType<Items.ChargedAmethyst>(), loot.ChargedCrystal);
        }

        if (loot.Shards > 0)
        {
            Item.NewItem(source, new Microsoft.Xna.Framework.Vector2(i * 16, j * 16), new Microsoft.Xna.Framework.Vector2(16, 16),
                ModContent.ItemType<Items.AmethystShard>(), loot.Shards);
        }
    }

    /// <summary>是否镐类工具。源项目的「工具合格」判定即 `cluster_max_harvestables` 标签。</summary>
    internal static bool IsPickaxe(Item item)
        => !item.IsAir && item.pick > 0;
}

/// <summary>小芽。</summary>
public sealed class AmethystBudSmall : AmethystGrowth
{
    public override AmethystStage Stage => AmethystStage.SmallBud;
}

/// <summary>中芽。</summary>
public sealed class AmethystBudMedium : AmethystGrowth
{
    public override AmethystStage Stage => AmethystStage.MediumBud;
}

/// <summary>大芽。</summary>
public sealed class AmethystBudLarge : AmethystGrowth
{
    public override AmethystStage Stage => AmethystStage.LargeBud;
}

/// <summary>成熟晶簇 —— 只有它掉落东西。</summary>
public sealed class AmethystCluster : AmethystGrowth
{
    public override AmethystStage Stage => AmethystStage.Cluster;
}
