namespace HexCastingTerraria.Core.World;

/// <summary>
/// 视线解析的输入。
///
/// 为什么用原始 float 而不是 <c>Microsoft.Xna.Framework.Vector2</c>：
/// 这一层刻意**不依赖 XNA**，于是能被离线测试覆盖 ——
/// 四层优先级与「绝不产生 NaN」是容易写错、又极难在游戏里观察的逻辑。
/// 游戏侧（`Content/HexGlobal*.cs`）负责把实体字段填进这里。
/// </summary>
public readonly struct LookInput
{
    /// <summary>是否有「瞄点」（本地玩家的鼠标世界坐标）。</summary>
    public bool HasAim { get; init; }

    public float AimX { get; init; }
    public float AimY { get; init; }

    /// <summary>实体自身中心（射线/方向的起点）。</summary>
    public float SelfX { get; init; }
    public float SelfY { get; init; }

    public float VelX { get; init; }
    public float VelY { get; init; }

    /// <summary>是否有有效攻击目标（NPC 的 target）。</summary>
    public bool HasTarget { get; init; }

    public float TargetX { get; init; }
    public float TargetY { get; init; }

    /// <summary>上一次解析出的有效视线（缓存兜底用）。</summary>
    public float PrevX { get; init; }
    public float PrevY { get; init; }

    /// <summary>实体原始朝向 ±1；最后的兜底。0 视作正方向。</summary>
    public int Direction { get; init; }
}

/// <summary>
/// 「视线方向」在 2D 泰拉瑞亚下的四层解析策略。
///
/// 背景：MC 里每个实体都有持久的 yaw/pitch，原作大量图案依赖它
/// （`get_entity_look`、`entity_pos/eye`、`raycast*`、`blink`、`teleport`…）。
/// 泰拉只有 <c>NPC.direction</c>（±1，仅水平），**没有持久朝向**。
///
/// 空缺如果直接归一化零向量就会得到 NaN，而 NaN 是**静默故障**：
/// 进 VM 后 `isTruthy` 为 false、所有比较为 false，不抛异常、不留日志。
/// 所以这里的原则是：**任何一条路径都不允许返回 NaN 或零向量**。
///
/// 详见 LOOK_DIRECTION_DESIGN.md。
/// </summary>
public static class LookResolver
{
    /// <summary>
    /// 速度平方低于此值 → 认为「没有在明显移动」，不采信速度方向。
    ///
    /// 不能设为 0：缓慢飘动的 NPC / 粒子会给出**噪声方向**，
    /// 那种朝向每帧乱转，比「保持上次朝向」糟糕得多。
    /// </summary>
    public const float VelocityThresholdSq = 0.1f * 0.1f;

    /// <summary>长度小于此值视为零向量，不参与归一化。</summary>
    private const float MinLength = 1e-6f;

    /// <summary>
    /// 按优先级解析视线方向，返回单位向量。
    /// **保证**：返回值一定有限、长度为 1（除非 <paramref name="input"/> 的
    /// <c>Direction</c> 与 <c>Prev</c> 同时异常，此时也只会退化成轴向单位向量）。
    /// </summary>
    public static (float X, float Y) Resolve(in LookInput input)
    {
        // ── 第 1 层：瞄点方向（本地玩家的鼠标）────────────────────────
        // 泰拉的「瞄准」就是鼠标，比 MC 的准星更精确，也符合泰拉玩家直觉。
        // 鼠标总在某个方向，所以这一层只要 HasAim 就必有解。
        if (input.HasAim
            && TryNormalize(input.AimX - input.SelfX, input.AimY - input.SelfY, out var result))
        {
            return result;
        }

        // ── 第 2 层：速度方向（仅在明显移动时）────────────────────────
        float velSq = input.VelX * input.VelX + input.VelY * input.VelY;
        if (velSq > VelocityThresholdSq && TryNormalize(input.VelX, input.VelY, out result))
        {
            return result;
        }

        // ── 第 3 层：指向目标（保留「怪物盯着你」的玩法语义）──────────
        if (input.HasTarget
            && TryNormalize(input.TargetX - input.SelfX, input.TargetY - input.SelfY, out result))
        {
            return result;
        }

        // ── 第 4 层：缓存兜底 ────────────────────────────────────────
        // 这一层是「保留」的关键：它让每个实体拥有一个**持久朝向**，
        // 等价于 MC 的 yaw。静止的 NPC 仍然朝着上次朝的方向，
        // 而不是变成 NaN（静默故障）或每帧乱转。
        if (TryNormalize(input.PrevX, input.PrevY, out result))
        {
            return result;
        }

        // ── 最终兜底：实体原始朝向轴向 ───────────────────────────────
        // 与 HexMathUtil.SafeNormalize 的兜底 (1,0) 保持一致。
        return input.Direction < 0 ? (-1f, 0f) : (1f, 0f);
    }

    /// <summary>
    /// 归一化。长度过小、为零、NaN、无穷 → 返回 false（**绝不产出 NaN**）。
    /// </summary>
    private static bool TryNormalize(float x, float y, out (float X, float Y) result)
    {
        result = default;

        if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y))
        {
            return false;
        }

        float lenSq = x * x + y * y;
        if (float.IsNaN(lenSq) || float.IsInfinity(lenSq) || lenSq < MinLength * MinLength)
        {
            return false;
        }

        // 注意用 double 开方再转回：float 下极大值的平方可能溢出，
        // 这里 lenSq 已排除非有限值，但 double 更稳妥且成本可忽略。
        float len = (float)System.Math.Sqrt(lenSq);
        if (len < MinLength || float.IsNaN(len))
        {
            return false;
        }

        result = (x / len, y / len);
        return true;
    }
}
