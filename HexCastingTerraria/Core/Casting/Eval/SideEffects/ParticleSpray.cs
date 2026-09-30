namespace HexCastingTerraria.Core.Casting.Eval.SideEffects;

/// <summary>
/// 一次粒子喷发。移植自源项目 `ParticleSpray`。
///
/// 注意：坐标用原始 float 而**不是** `Microsoft.Xna.Framework.Vector2`。
/// 原因：本文件位于 `Core/Casting/` 下，而离线测试工程会整目录拷贝 `Casting` ——
/// 只要这里出现一个 XNA 类型，**全部 153 项离线测试立刻编译不过**。
/// 表现层需要 Vector2 时在渲染处转换即可（两行代码）。
///
/// 坐标为**世界像素**（不是图格）：粒子的扩散半径、速度都按像素写更自然。
/// </summary>
public sealed class ParticleSpray
{
    /// <summary>喷发位置的 X（法术坐标：方块）。</summary>
    public required float X { get; init; }

    /// <summary>喷发位置的 Y（法术坐标：方块，Y 朝上）。表现层（SpellVisuals）换成世界像素。</summary>
    public required float Y { get; init; }

    /// <summary>扩散半径（方块）。</summary>
    public float Spread { get; init; }

    /// <summary>初速度（方块/刻）。</summary>
    public float Speed { get; init; }

    /// <summary>粒子数量。</summary>
    public int Count { get; init; } = 1;

    /// <summary>向四周散开的爆开粒子。</summary>
    public static ParticleSpray Burst(double x, double y, float spread, int count)
        => new() { X = (float)x, Y = (float)y, Spread = spread, Speed = 0f, Count = count };

    /// <summary>原地聚成一团的云雾粒子。</summary>
    public static ParticleSpray Cloud(double x, double y, float spread, int count)
        => new() { X = (float)x, Y = (float)y, Spread = spread, Speed = 0f, Count = count };
}

/// <summary>
/// 播放一次粒子效果。对应源项目 `OperatorSideEffect.Particles`。
///
/// 纯表现，不影响世界状态，因此不需要联机同步 ——
/// 每个客户端各自在被要求的位置生成 dust 即可。
/// </summary>
public sealed class ParticlesSideEffect : OperatorSideEffect
{
    public ParticleSpray Spray { get; }

    public ParticlesSideEffect(ParticleSpray spray) => Spray = spray;

    public override void PerformEffect(Vm.CastingVM harness)
    {
        // 本方法在 Core 层，**不能**引用 Terraria 的 Main.dust。
        // 这里只把「该在哪喷、喷多少」记录下来；
        // 客户端渲染时从 CastResult.SideEffects 里读出 ParticlesSideEffect 再生成 dust。
        // 留空是刻意的，不是遗漏。
    }
}
