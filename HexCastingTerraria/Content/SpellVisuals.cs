using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using Microsoft.Xna.Framework;
using Terraria;

namespace HexCastingTerraria.Content;

/// <summary>
/// 法术粒子的表现层：把 Core 算出来的 <see cref="ParticleSpray"/> 变成真正的 dust。
///
/// ## 为什么需要这一层（这里曾经有个静默的漏洞）
///
/// Core 里的 `ParticlesSideEffect.PerformEffect` 是**空实现** ——
/// 它不能引用 Terraria，注释写着「客户端从 CastResult.SideEffects 里读出来再生成 dust」。
/// 但**客户端从来没有人去读**：`CastOutcome` 甚至没有把副作用带出来。
/// 结果就是传送、爆炸、闪电、哨卫、飞行、脑叶切除这些法术的粒子**一个都不显示**，
/// 而且不报错、不掉帧、日志里什么都没有。
///
/// 现在链路是：`CastingVM.QueueExecute` 收集 → `CastOutcome.Particles` →
/// 本类生成 dust（单人 / 服务端广播后的客户端）。
///
/// ## 联机
///
/// 粒子纯表现，但**生成它的代码在服务端**（权威求值在服务端）——
/// 服务端 `Dust.NewDust` 客户端看不到。所以服务端走广播（见 <see cref="Broadcast"/>），
/// 客户端收到后本地生成。与瞄准点标记（`SpellVisual`）同一套思路。
/// </summary>
internal static class SpellVisuals
{
    /// <summary>单次施法最多生成多少粒子。防止某个法术写错数量把 dust 数组刷爆。</summary>
    private const int MaxDustPerCast = 600;

    /// <summary>在本地生成粒子。**只能在客户端调用**（服务端调用没有意义）。</summary>
    public static void SpawnLocal(IReadOnlyList<ParticleSpray> sprays, Color color, int budget = MaxDustPerCast)
    {
        if (Main.dedServ || sprays == null || sprays.Count == 0) return;

        int spawned = 0;

        for (int i = 0; i < sprays.Count && spawned < budget; i++)
        {
            var spray = sprays[i];
            int count = spray.Count;
            if (count <= 0) continue;
            if (spawned + count > budget) count = budget - spawned;

            // Core 给的是法术坐标（方块、Y 朝上）—— 换成世界像素。
            // ⚠️ 这里曾经直接当像素用：所有法术粒子都画在世界左上角附近，扩散也只有一两个像素。
            var center = HexSpaceWorld.ToWorldPixels(spray.X, spray.Y);
            float spreadPx = spray.Spread * 16f;
            float speedPx = spray.Speed * 16f / 3f;   // 方块/刻 → 像素/帧

            for (int n = 0; n < count; n++)
            {
                // 扩散：以 spread 为半径均匀撒点（spread=0 时就喷在一个点上）
                var offset = spreadPx <= 0f
                    ? Vector2.Zero
                    : new Vector2(
                        Main.rand.NextFloat(-spreadPx, spreadPx),
                        Main.rand.NextFloat(-spreadPx, spreadPx));

                var position = center + offset;

                var dust = Dust.NewDustPerfect(
                    position,
                    Terraria.ID.DustID.PurpleTorch,
                    offset.SafeNormalize(Vector2.Zero) * speedPx,
                    0,
                    color,
                    1.0f);

                dust.noGravity = true;

                // 扩散大的（爆开类）让它飞出去；扩散小的（云雾类）原地停住
                if (spray.Spread > 0.5f)   // 方块
                {
                    dust.velocity = offset * 0.12f;
                }
                else
                {
                    dust.velocity *= 0.4f;
                }

                spawned++;
            }
        }
    }

    /// <summary>
    /// 服务端 → 附近客户端：广播一次施法的粒子。
    ///
    /// 为什么要广播：权威求值在服务端，粒子也是在服务端被「算出来」的，
    /// 而服务端生成 dust 客户端看不见 —— 不广播的话，联机时只有主机看不到自己的法术特效
    /// 这种事会一直没人发现（单机测试永远正常）。
    /// </summary>
    public static void Broadcast(IReadOnlyList<ParticleSpray> sprays, float originX, float originY)
    {
        if (Main.netMode != Terraria.ID.NetmodeID.Server) return;
        if (sprays == null || sprays.Count == 0) return;

        var packet = HexCastingTerraria.Instance?.GetPacket();
        if (packet == null) return;

        // 上限：单个包太大可能被截断，宁可少画几个也不要整包作废
        int count = System.Math.Min(sprays.Count, 24);

        packet.Write((byte)Net.HexMessage.SpellParticles);
        packet.Write((byte)count);

        for (int i = 0; i < count; i++)
        {
            var s = sprays[i];
            packet.Write(s.X);
            packet.Write(s.Y);
            packet.Write(s.Spread);
            packet.Write(s.Speed);
            packet.Write((short)System.Math.Clamp(s.Count, 0, short.MaxValue));
        }

        // 附近广播（法术多半发生在施法者附近）
        const float radiusPx = 120f * Core.Casting.HexUnits.PixelsPerTile;
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var other = Main.player[i];
            if (other is not { active: true }) continue;
            if (System.Math.Abs(other.Center.X - originX) > radiusPx) continue;
            if (System.Math.Abs(other.Center.Y - originY) > radiusPx) continue;

            packet.Send(i);
        }
    }

    /// <summary>客户端：接收广播来的粒子。</summary>
    public static void Receive(System.IO.BinaryReader reader)
    {
        if (Main.dedServ) return;

        int count = reader.ReadByte();
        var sprays = new List<ParticleSpray>(count);

        for (int i = 0; i < count; i++)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float spread = reader.ReadSingle();
            float speed = reader.ReadSingle();
            short n = reader.ReadInt16();

            sprays.Add(new ParticleSpray { X = x, Y = y, Spread = spread, Speed = speed, Count = n });
        }

        // 颜色用本地玩家的配色：对方的法术颜色在他自己那边是对的，
        // 但把「谁施的法」也同步过来只为染色，收益不值这个包大小
        SpawnLocal(sprays, Client.HexPigment.Current);
    }
}
