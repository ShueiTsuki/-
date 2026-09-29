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
///   - **不可挖取**（与原版一致：母岩只能靠后期手段获得，破坏性采掘拿不到）
///   - 每次随机刻有 <see cref="AmethystLoot.TickChanceDenominator"/> 分之一的概率
///     让**相邻的**一个晶簇生长一级（空 → 小芽 → 中芽 → 大芽 → 成熟晶簇）
///
/// ⚠️ 2D 适配说明：源项目向**六个**方向生长，泰拉侧向**四个**方向（上下左右）。
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

        MinPick = 1000;                            // 高到挖不动
        MineResist = 5f;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Tink;
        AddMapEntry(new Color(126, 78, 178));
    }

    /// <summary>
    /// 母岩**不可获得**（源项目 `budding_amethyst` 挖掉什么都不掉）。
    /// 直接在破坏阶段否决，比「掉了但掉空气」更清晰，也挡得住爆炸。
    /// </summary>
    public override bool CanKillTile(int i, int j, ref bool blockDamaged) => false;

    public override bool CanExplode(int i, int j) => false;

    /// <summary>
    /// 随机刻：尝试让一个相邻位置生长一级。
    ///
    /// 移植自 MC 的 `BuddingAmethystBlock.randomTick`：
    ///   1. `rand.nextInt(5) == 0` 才继续（1/5 概率）
    ///   2. 随机挑一个方向
    ///   3. 该方向若是空位 → 长出小芽；若已是某一级晶簇 → 升一级；已成熟 → 不动
    ///
    /// ⚠️ 签名是 `(int i, int j, bool wall)` —— 不是两参数版本，
    /// tModLoader 的 `ModBlockType.RandomUpdate` 是三个参数。
    /// </summary>
    public override void RandomUpdate(int i, int j, bool wall)
    {
        // 墙上的随机刻不处理：晶洞长在物块上，不长在背景墙上
        if (wall)
        {
            return;
        }

        // 调试开关：晶簇立即长成（跳过随机判定）
        bool forceGrow = HexClientConfig.Instance.InstantCrystalGrowth;

        if (!forceGrow && !AmethystLoot.RollGrowth(Main.rand.Next))
        {
            return;
        }

        // 随机挑一个方向（上/下/左/右）
        int dir = Main.rand.Next(4);
        int ti = i, tj = j;
        switch (dir)
        {
            case 0: tj -= 1; break;   // 上
            case 1: tj += 1; break;   // 下
            case 2: ti -= 1; break;   // 左
            default: ti += 1; break;  // 右
        }

        if (!WorldGen.InWorld(ti, tj, 1))
        {
            return;
        }

        var tile = Main.tile[ti, tj];

        // 空位 → 长出小芽
        if (!tile.HasTile)
        {
            WorldGen.PlaceTile(ti, tj, ModContent.TileType<AmethystBudSmall>(), mute: true);
            return;
        }

        // 已是某一级 → 升一级
        int next = StageToTile(AmethystLoot.NextStage(TileToStage(tile.TileType)));
        if (next > 0 && next != tile.TileType)
        {
            tile.TileType = (ushort)next;
            tile.HasTile = true;
            // 通知客户端这一格变了；不置位的话联机下只有服务端看得见生长
            if (Main.netMode == NetmodeID.Server)
            {
                NetMessage.SendTileSquare(-1, ti, tj, 1);
            }
        }
    }

    /// <summary>方块类型 → 生长阶段。</summary>
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
        Main.tileCut[Type] = true;      // 允许被任意方式打掉（不必用镐）

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

    /// <summary>
    /// 掉落。**芽阶段不掉任何东西** —— 提前敲掉就白等一轮生长。
    /// 成熟晶簇的掉落严格按 <see cref="AmethystLoot.RollCluster"/> 的四个池。
    /// </summary>
    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly || noItem)
        {
            return;
        }

        // 芽阶段：直接什么都不掉
        if (!AmethystLoot.DropsLoot(Stage))
        {
            noItem = true;
            return;
        }

        var player = Main.LocalPlayer;

        // 泰拉没有「精准采集」，永远走非精准采集分支（见 TODO_PLAN.md 的说明）。
        // 工具是否「合格」映射为：手上是否拿着镐类工具。
        bool properTool = IsPickaxe(player.HeldItem);

        int fortune = properTool
            ? AmethystLoot.FortuneFromPickaxePower(player.HeldItem.pick)
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
            Item.NewItem(source, i * 16, j * 16, 16, 16,
                ModContent.ItemType<Items.AmethystDust>(), loot.Dust);
        }

        if (loot.ChargedCrystal > 0)
        {
            Item.NewItem(source, i * 16, j * 16, 16, 16,
                ModContent.ItemType<Items.ChargedAmethyst>(), loot.ChargedCrystal);
        }

        if (loot.Shards > 0)
        {
            Item.NewItem(source, i * 16, j * 16, 16, 16,
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
