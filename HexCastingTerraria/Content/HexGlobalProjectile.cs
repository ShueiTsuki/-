using HexCastingTerraria.Core.World;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content;

/// <summary>
/// 为每个弹幕维护一个持久「视线方向」，即飞行方向。
///
/// 弹幕比 NPC 简单：它几乎总是在动，所以第 2 层（速度方向）基本总是命中。
/// 但**停止的弹幕**（例如悬停类）速度为零，仍需要缓存兜底，否则会得到 NaN。
/// </summary>
public sealed class HexGlobalProjectile : GlobalProjectile
{
    /// <summary>
    /// **必须为 true**。理由同 <see cref="HexGlobalNPC.InstancePerEntity"/>：
    /// 默认共享实例 + 实例字段会让模组加载失败并被禁用。
    /// </summary>
    public override bool InstancePerEntity => true;

    /// <summary>上一次解析出的视线（单位向量）。</summary>
    public Vector2 Look { get; private set; } = new(1f, 0f);

    public override void PostAI(Projectile projectile)
    {
        var (x, y) = LookResolver.Resolve(new LookInput
        {
            // 弹幕没有瞄点，也没有 target，所以只有第 2 / 4 层可用
            HasAim = false,
            SelfX = projectile.Center.X,
            SelfY = projectile.Center.Y,
            VelX = projectile.velocity.X,
            VelY = projectile.velocity.Y,
            HasTarget = false,
            PrevX = Look.X,
            PrevY = Look.Y,
            // 弹幕的 direction 是飞行方向而非「朝向」，仅在最终兜底时用
            Direction = projectile.direction,
        });

        Look = new Vector2(x, y);
    }
}
