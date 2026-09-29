using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 被召唤出来的方块/光源的存活管理。
///
/// 源项目的 `conjured_block` / `conjured_light` 是**临时方块**，过一段时间自行消失。
/// 泰拉没有「限时方块」的原生机制，所以这里自己记一份表，
/// 每 tick 递减，到 0 就挖掉。
///
/// 为什么用「活跃列表」而不是每 tick 扫描全图：召唤方块数量很少（玩家造的），
/// 扫全图（大世界 2000 万格）会在每 tick 产生不可接受的开销。
/// </summary>
public static class ConjuredBlocks
{
    /// <summary>召唤方块的默认存活时间（秒）。</summary>
    public const double DefaultLifetimeSeconds = 30.0;

    private static readonly Dictionary<(int X, int Y), int> Timers = new();

    /// <summary>登记一个召唤方块。</summary>
    public static void Track(int x, int y, int ticks)
    {
        if (ticks <= 0) ticks = 1;
        Timers[(x, y)] = ticks;
    }

    /// <summary>该格是否是被召唤出来的。</summary>
    public static bool IsConjured(int x, int y) => Timers.ContainsKey((x, y));

    /// <summary>取消登记（方块被提前挖掉时）。</summary>
    public static void Untrack(int x, int y) => Timers.Remove((x, y));

    public static void Clear() => Timers.Clear();

    /// <summary>每 tick 推进一次倒计时。</summary>
    public static void Update()
    {
        if (Timers.Count == 0) return;

        // 只在一端结算：服务端权威（单机就是本地）
        if (Main.netMode == NetmodeID.MultiplayerClient) return;

        List<(int X, int Y)>? expired = null;

        // 不能边遍历边改，先收集到期的
        foreach (var kv in Timers)
        {
            if (kv.Value > 1) continue;
            (expired ??= new List<(int, int)>()).Add(kv.Key);
        }

        if (expired == null)
        {
            // 递减：Dictionary 的 value 不能直接改，重建一份
            var keys = new List<(int X, int Y)>(Timers.Keys);
            foreach (var k in keys)
            {
                Timers[k] = Timers[k] - 1;
            }
            return;
        }

        foreach (var (x, y) in expired)
        {
            Timers.Remove((x, y));

            if (!WorldGen.InWorld(x, y, 1)) continue;

            var tile = Main.tile[x, y];
            if (!tile.HasTile) continue;

            int t = tile.TileType;
            if (t != ModContent.TileType<ConjuredBlock>() && t != ModContent.TileType<ConjuredLight>())
            {
                continue;   // 已经被换成别的方块了，不动它
            }

            tile.ClearTile();
            if (Main.netMode == NetmodeID.Server)
            {
                NetMessage.SendTileSquare(-1, x, y, 1);
            }
        }
    }
}

/// <summary>
/// 召唤方块：凭空造出来的一块实心方块，一段时间后消失。
/// 对应源项目 `hexcasting:conjured_block`。
/// </summary>
public sealed class ConjuredBlock : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileLighted[Type] = true;
        Main.tileFrameImportant[Type] = false;

        MinPick = 0;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Tink;
        AddMapEntry(new Color(140, 110, 200));
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        r = 0.16f; g = 0.10f; b = 0.26f;
    }

    /// <summary>被挖掉时取消倒计时登记，避免留下悬空条目。</summary>
    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly) return;
        ConjuredBlocks.Untrack(i, j);
    }
}

/// <summary>
/// 召唤光源：凭空造出来的一盏光。对应源项目 `hexcasting:conjured_light`。
/// </summary>
public sealed class ConjuredLight : ModTile
{
    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = false;      // 不挡路
        Main.tileBlockLight[Type] = false;
        Main.tileLighted[Type] = true;
        Main.tileFrameImportant[Type] = false;
        Main.tileNoAttach[Type] = true;

        MinPick = 0;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Shatter;
        AddMapEntry(new Color(210, 180, 255));
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        // 光源要够亮才有意义
        r = 0.85f; g = 0.72f; b = 1.00f;
    }

    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly) return;
        ConjuredBlocks.Untrack(i, j);
    }
}

/// <summary>驱动召唤方块的倒计时。</summary>
public sealed class ConjuredBlockSystem : ModSystem
{
    public override void PostUpdateWorld() => ConjuredBlocks.Update();
}
