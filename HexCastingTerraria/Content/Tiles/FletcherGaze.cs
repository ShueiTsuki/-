using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 制箭师促动石的「被盯着」计数（原版 BlockEntityLookingImpetus.serverTick）：
///
///   - 每刻（MC 20 刻 / 秒 = 泰拉 3 帧）看一次：玩家从眼睛沿视线打一条射线，长 20 / 1.5 格（原版 `range / 1.5f`），
///     第一个挡住的方块就是这块促动石 → 计数 +1，否则 −1，夹在 0..30
///   - 到 30：清零并启动，施法者 = 盯着它的人；每逢计数 % 5 == 1 咔哒一声
///   - 戴南瓜头的人不算（原版 carved pumpkin；泰拉用南瓜灯面具）
///
/// 泰拉的「视线」就是鼠标方向（HexPlayer.Look），只有本人客户端知道 —— 所以在客户端数，到了发启动请求。
/// 与原版的差别：原版一块促动石一个计数、谁看都算；这里每个客户端数自己的（多人一起盯不会更快）。
/// </summary>
public static class FletcherGaze
{
    public const int MaxLook = 30;
    private const float Range = 20f / 1.5f;

    private static readonly Dictionary<Point16, int> Looks = new();
    private static int _frame;

    public static void Clear() => Looks.Clear();

    /// <summary>本地玩家每帧调一次。</summary>
    public static void Update(Player player)
    {
        if (++_frame < 3) return;
        _frame = 0;

        Point16? target = null;
        if (player is { active: true, dead: false } && player.armor[0].type != ItemID.JackOLanternMask)
        {
            var look = HexPlayer.Get(player).Look;
            float ex = player.Bottom.X / 16f, ey = (player.Bottom.Y - player.height * 0.9f) / 16f;
            var hit = Core.World.TileRaycast.Cast(TerrariaCastingWorld.SolidAt, ex, ey, look.X, look.Y, Range);
            if (hit is { } h
                && TileLoader.GetTile(Main.tile[h.TileX, h.TileY].TileType) is HexImpetusBase { Kind: ImpetusKind.Look }
                && HexImpetusEntity.FindAt(h.TileX, h.TileY) is { IsRunning: false })
            {
                target = new Point16(h.TileX, h.TileY);
            }
        }

        if (target is { } t && !Looks.ContainsKey(t)) Looks[t] = 0;

        foreach (var pos in new List<Point16>(Looks.Keys))
        {
            int prev = Looks[pos];
            int now = System.Math.Clamp(prev + (target == pos ? 1 : -1), 0, MaxLook);
            if (now == prev) continue;
            if (now == MaxLook)
            {
                Looks[pos] = 0;
                HexImpetusEntity.Request(pos.X, pos.Y, ImpetusAction.Start, 0);
                continue;
            }
            if (now % 5 == 1) SpellSounds.Play("impetus.fletcher.tick", new Microsoft.Xna.Framework.Vector2(pos.X * 16 + 8, pos.Y * 16 + 8));
            if (now == 0) Looks.Remove(pos); else Looks[pos] = now;
        }
    }
}
