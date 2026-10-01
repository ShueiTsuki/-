using HexCastingTerraria.Content.Prefixes;
using HexCastingTerraria.Core.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 淬灵晶系方块照原版（BlockQuenchedAllay 与掉落表 quenched_allay.json）。放在 GlobalTile 里是因为这几个类由脚本生成
/// （DecoBlocks.Generated.cs），不手改；发光在生成器里（亮度 4）。
///
/// 掉落（只管淬灵晶块）：用「共振」镐挖、或者喝了共振药水再挖，掉方块本身（原版的精准采集，见 <see cref="Resonant.Active"/>）；
/// 否则掉 2~5 片淬灵晶碎片（<see cref="QuenchedLoot.RollShards"/>，时运按镐力换算）。淬灵晶瓦 / 砖照原版掉自己。
/// 这里曾经用 CanDrop 返回 false 再在 Drop 里生成碎片 —— tML 1.4.4 里 CanDrop 返回 false 以后根本不调用 Drop，
/// 淬灵晶块敲掉一片碎片都不掉。现在照晶簇的写法：在 KillTile 里关掉默认掉落、自己生成。
///
/// 粒子见 <see cref="QuenchedAllayParticles"/>。
/// </summary>
public sealed class QuenchedAllayGlobal : GlobalTile
{
    public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (type != ModContent.TileType<QuenchedAllay>() || fail || effectOnly || noItem) return;
        noItem = true;
        // 掉落只在单机 / 服务端生成（联机客户端也跑这个钩子，在那边生成会多掉一份）
        if (Main.netMode == NetmodeID.MultiplayerClient) return;

        var miner = TileMiner.Find(i, j);
        var source = new EntitySource_TileBreak(i, j);
        var position = new Vector2(i * 16, j * 16);
        var size = new Vector2(16, 16);
        if (miner is not null && Resonant.Active(miner))
        {
            Item.NewItem(source, position, size, ModContent.ItemType<QuenchedAllayItem>(), 1);
            return;
        }

        int fortune = miner is null ? 0 : AmethystLoot.FortuneFromPickaxePower(miner.HeldItem.pick);
        int shards = QuenchedLoot.RollShards(fortune, n => Main.rand.Next(n), p => Main.rand.NextDouble() < p);
        Item.NewItem(source, position, size, ModContent.ItemType<Items.QuenchedAllayShard>(), shards);
    }

    internal static bool IsQuenched(int type)
        => type == ModContent.TileType<QuenchedAllay>()
           || type == ModContent.TileType<QuenchedAllayTiles>()
           || type == ModContent.TileType<QuenchedAllayBricks>()
           || type == ModContent.TileType<QuenchedAllayBricksSmall>();
}

/// <summary>
/// 淬灵晶系方块冒紫色（0x8932b8）构筑粒子，照原版 BlockQuenchedAllay.animateTick（MC 百科：「会产生大量粒子，慎用」）。
///
/// 原版不是每个方块每帧都冒：ClientLevel.animateTick 每 tick 在玩家周围随机抽两批各 667 格（半径 16 与 32），
/// 一格每 tick 被抽到约 2.1%；被抽到的格子每个面冒 rand(10)/4 粒。这里照搬「随机抽格子」的做法：
/// 每帧在屏幕范围里按 MC 每 tick 2.1%、换成泰拉每帧 0.7% 的概率抽格子。不挂在画方块的钩子上 ——
/// 泰拉的实心方块先画进缓存、隔几帧才重画，挂在那里冒粒子的频率会忽高忽低。
/// 正面（朝着屏幕外）一直露在外面；上下左右挨着实心方块的那一面看不见，不冒。
/// </summary>
public sealed class QuenchedAllayParticles : ModSystem
{
    /// <summary>原版 ConjureParticleOptions(0x8932b8)。</summary>
    private static readonly Color ParticleColor = new(0x89, 0x32, 0xB8);

    /// <summary>一格每帧被抽到的概率：MC 每 tick 约 2.1%，MC 每秒 20 tick、泰拉每秒 60 帧。</summary>
    private const double PickChancePerFrame = 0.021 / 3;

    public override void PostUpdateDusts()
    {
        if (Main.dedServ || Main.gameMenu) return;

        int x0 = System.Math.Max(1, (int)(Main.screenPosition.X / 16f) - 2);
        int y0 = System.Math.Max(1, (int)(Main.screenPosition.Y / 16f) - 2);
        int x1 = System.Math.Min(Main.maxTilesX - 2, (int)((Main.screenPosition.X + Main.screenWidth) / 16f) + 2);
        int y1 = System.Math.Min(Main.maxTilesY - 2, (int)((Main.screenPosition.Y + Main.screenHeight) / 16f) + 2);
        if (x1 < x0 || y1 < y0) return;

        double expected = (double)(x1 - x0 + 1) * (y1 - y0 + 1) * PickChancePerFrame;
        int picks = (int)expected + (Main.rand.NextDouble() < expected - (int)expected ? 1 : 0);
        for (int k = 0; k < picks; k++)
        {
            int i = Main.rand.Next(x0, x1 + 1);
            int j = Main.rand.Next(y0, y1 + 1);
            var tile = Main.tile[i, j];
            if (!tile.HasTile || !QuenchedAllayGlobal.IsQuenched(tile.TileType)) continue;
            Animate(i, j);
        }
    }

    /// <summary>原版 animateTick：每个面冒 rand(10)/4 粒，贴着面外侧 0.55 格、沿面随机，沿法线很慢地飘出去（0 ~ 0.01 格 / tick）。</summary>
    private static void Animate(int i, int j)
    {
        var center = new Vector2(i * 16 + 8, j * 16 + 8);
        for (int n = Main.rand.Next(10) / 4; n > 0; n--)
        {
            Spawn(center + new Vector2(Main.rand.NextFloat(-8f, 8f), Main.rand.NextFloat(-8f, 8f)), Vector2.Zero);
        }
        Side(i, j - 1, center, new Vector2(0, -1));
        Side(i, j + 1, center, new Vector2(0, 1));
        Side(i - 1, j, center, new Vector2(-1, 0));
        Side(i + 1, j, center, new Vector2(1, 0));
    }

    private static void Side(int nx, int ny, Vector2 center, Vector2 normal)
    {
        int count = Main.rand.Next(10) / 4;
        if (count == 0) return;
        if (Main.tile[nx, ny] is { HasTile: true } t && Main.tileSolid[t.TileType]) return;
        for (; count > 0; count--)
        {
            var along = new Vector2(normal.Y, normal.X) * Main.rand.NextFloat(-8f, 8f);
            Spawn(center + normal * 8.8f + along, normal * Main.rand.NextFloat(0f, 0.16f / 3f));
        }
    }

    private static void Spawn(Vector2 position, Vector2 velocity)
    {
        var dust = Dust.NewDustPerfect(position, DustID.PurpleTorch, velocity, 0, ParticleColor, 0.9f);
        dust.noGravity = true;
    }
}
