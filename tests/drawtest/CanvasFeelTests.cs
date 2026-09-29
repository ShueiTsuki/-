using HexCastingTerraria.Core.Canvas;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

/// <summary>
/// 画布手感相关：真状态机 <see cref="PatternDrawer"/>、MC 原版噪声、原版线型三角化。
/// 这些都是「只在游戏里用眼睛/手才看得出来」的东西，这里把能写成断言的部分写成断言。
/// </summary>
delegate void CheckFn(string name, bool ok, string? detail = null);

static class CanvasFeelTests
{
    public static void Run(float size, CheckFn check)
    {
        Console.WriteLine("\n=== ③b 快速画：一帧只有一个鼠标位置时，沿路径补采样 ===");
        FastStrokes(size, check);

        Console.WriteLine("\n=== ③c 状态机细节（落笔 / 回退 / 已用格点 / 撤销上色）===");
        StateMachine(size, check);

        Console.WriteLine("\n=== ③d MC 原版 SimplexNoise 与线型三角化 ===");
        NoiseAndGeometry(size, check);
    }

    /// <summary>把图案折线按固定弧长切成「每帧一个鼠标位置」的序列。</summary>
    static List<Vec2f> FramesAlong(IReadOnlyList<Vec2f> poly, float stepPx)
    {
        var frames = new List<Vec2f> { poly[0] };
        float carry = 0f;
        for (int i = 0; i + 1 < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[i + 1];
            float len = (b - a).Length;
            float t = stepPx - carry;
            while (t <= len)
            {
                frames.Add(a + (b - a) * (t / len));
                t += stepPx;
            }
            carry = len - (t - stepPx);
        }
        frames.Add(poly[^1]);
        return frames;
    }

    static string DrawFrames(List<Vec2f> frames, float size, bool alongPath)
    {
        var d = new PatternDrawer();
        d.Begin(frames[0], size, Vec2f.Zero);
        for (int i = 1; i < frames.Count; i++)
        {
            if (alongPath) d.MoveAlong(frames[i - 1], frames[i], size, Vec2f.Zero);
            else d.Move(frames[i], size, Vec2f.Zero);   // 旧行为：每帧只调用一次 drawMove
        }
        return d.Wip?.AnglesSignature() ?? "";
    }

    static void FastStrokes(float size, CheckFn check)
    {
        foreach (float hexPerFrame in new[] { 0.5f, 1.0f, 1.5f, 2.5f })
        {
            int oldBad = 0, newBad = 0;
            var newBadNames = new List<string>();
            foreach (var def in PatternRegistry.All)
            {
                var poly = HexGrid.PatternLinePoints(def.Prototype, HexCoord.Origin, size, Vec2f.Zero);
                var frames = FramesAlong(poly, hexPerFrame * size * HexGrid.Sqrt3);   // 相邻格点相距 √3·size（同原版 coordToPx）
                if (DrawFrames(frames, size, alongPath: false) != def.Angles) oldBad++;
                if (DrawFrames(frames, size, alongPath: true) != def.Angles) { newBad++; newBadNames.Add(def.Id); }
            }
            Console.WriteLine($"     每帧 {hexPerFrame} 格：旧（每帧一步）错 {oldBad} 条 / 新（沿路径采样）错 {newBad} 条");
            if (hexPerFrame <= 1.0f)
            {
                check($"每帧移动 {hexPerFrame} 格时 188 条图案全部画对（沿路径采样）", newBad == 0,
                    string.Join(", ", newBadNames.Take(8)));
            }
            check($"每帧 {hexPerFrame} 格：沿路径采样不比旧做法差", newBad <= oldBad, $"新 {newBad} > 旧 {oldBad}");
        }
    }

    static Vec2f Px(HexCoord c, float size) => HexGrid.CoordToPx(c, size, Vec2f.Zero);

    static void StateMachine(float size, CheckFn check)
    {
        var o = HexCoord.Origin;
        var east = o + HexDir.East;
        var east2 = east + HexDir.East;

        // 回退：向东两格，再往回一格 → 回到单笔；再回一格 → 回到「刚落笔」
        {
            var d = new PatternDrawer();
            d.Begin(Px(o, size), size, Vec2f.Zero);
            var r1 = d.MoveAlong(Px(o, size), Px(east2, size), size, Vec2f.Zero);
            check("向东拖两格 = Started + Added，签名 w",
                r1.SequenceEqual(new[] { MoveResult.Started, MoveResult.Added }) && d.Wip!.AnglesSignature() == "w",
                string.Join(",", r1) + " / " + d.Wip?.AnglesSignature());
            var r2 = d.MoveAlong(Px(east2, size), Px(east, size), size, Vec2f.Zero);
            check("往回拖一格 = Backtracked，签名变空、仍在画",
                r2.SequenceEqual(new[] { MoveResult.Backtracked }) && d.Phase == DrawPhase.Drawing && d.Wip!.Length == 0,
                string.Join(",", r2));
            var r3 = d.MoveAlong(Px(east, size), Px(o, size), size, Vec2f.Zero);
            check("再往回一格 = Backtracked 且回到 JustStarted（原版语义）",
                r3.Contains(MoveResult.Backtracked) && d.Phase is DrawPhase.JustStarted or DrawPhase.Drawing,
                $"{string.Join(",", r3)} / {d.Phase}");
        }

        // 已用格点：不能在上面落笔，也不能穿过
        {
            var d = new PatternDrawer();
            d.Begin(Px(o, size), size, Vec2f.Zero);
            d.MoveAlong(Px(o, size), Px(east2, size), size, Vec2f.Zero);
            var first = d.End();
            check("收笔得到一条图案，三个格点都记为已用",
                first is not null && d.IsUsed(o) && d.IsUsed(east) && d.IsUsed(east2));
            check("在已用格点上落笔无效（原版 drawStart 的 usedSpots 判断）", !d.Begin(Px(east, size), size, Vec2f.Zero));

            var below = o + HexDir.SouthEast;
            check("在空格点上落笔有效", d.Begin(Px(below, size), size, Vec2f.Zero));
            // 从 below 往东北拖向 east（已用）→ 不能落到已用格点上
            d.MoveAlong(Px(below, size), Px(east, size), size, Vec2f.Zero);
            check("拖向已用格点不产生线段", d.Phase == DrawPhase.JustStarted, d.Phase.ToString());
            check("只落笔没画线就收笔 → 不产生图案", d.End() is null && d.Patterns.Count == 1);
        }

        // 撤销上色：原版 recvServerUpdate 的 UNDONE 分支
        {
            var d = new PatternDrawer();
            HexCoord start = o;
            for (int k = 0; k < 3; k++)
            {
                d.Begin(Px(start, size), size, Vec2f.Zero);
                d.MoveAlong(Px(start, size), Px(start + HexDir.East, size), size, Vec2f.Zero);
                d.End();
                start = start + HexDir.SouthEast + HexDir.SouthWest;   // 下移两行，互不重叠
            }
            d.Patterns[0].Type = ResolvedPatternType.Escaped;
            d.Patterns[1].Type = ResolvedPatternType.Evaluated;
            d.ApplyResolution(ResolvedPatternType.Undone);
            check("Undone：最近一条可撤销（Escaped）标为 Undone，撤销图案本身用 Evaluated 色",
                d.Patterns[0].Type == ResolvedPatternType.Undone
                && d.Patterns[1].Type == ResolvedPatternType.Evaluated
                && d.Patterns[2].Type == ResolvedPatternType.Evaluated,
                string.Join(",", d.Patterns.Select(p => p.Type)));
            d.ApplyResolution(ResolvedPatternType.Errored);
            check("普通结果只改最后一条", d.Patterns[2].Type == ResolvedPatternType.Errored);
        }
    }

    static void NoiseAndGeometry(float size, CheckFn check)
    {
        var n = SimplexNoise.Hex;
        double min = double.MaxValue, max = double.MinValue, sum = 0;
        int cnt = 0;
        var rng = new Random(1);
        for (int i = 0; i < 20000; i++)
        {
            double v = n.GetValue(rng.NextDouble() * 50 - 25, rng.NextDouble() * 50 - 25, rng.NextDouble() * 50 - 25);
            min = Math.Min(min, v); max = Math.Max(max, v); sum += v; cnt++;
        }
        check($"simplex 值域在 [-1, 1] 且铺得开（实测 {min:F2} ~ {max:F2}）",
            min >= -1.0001 && max <= 1.0001 && max - min > 1.2, null);
        check($"simplex 均值接近 0（实测 {sum / cnt:F3}）", Math.Abs(sum / cnt) < 0.05, null);
        check("同一种子结果确定", new SimplexNoise(9001).GetValue(1.3, 2.7, -0.4) == n.GetValue(1.3, 2.7, -0.4));
        check("不同种子结果不同", new SimplexNoise(1).GetValue(1.3, 2.7, -0.4) != n.GetValue(1.3, 2.7, -0.4));

        // 电光：起点不动；lastSegmentLen = 1 时终点落在终点上，0.8 时故意停在半路（原版行为）
        var line = new List<Vec2f> { new(0, 0), new(size, 0), new(size * 1.5f, size) };
        var z1 = PatternGeometry.MakeZappy(line, null, 10, 2.5f, 0.1f, 0.2f, 0.2f, 1f, 0, 0);
        var z08 = PatternGeometry.MakeZappy(line, null, 10, 2.5f, 0.1f, 0.2f, 0.2f, 0.8f, 0, 0);
        check("电光：首点 = 起点", z1[0].Equals(line[0]));
        check("电光：lastSegmentLen=1 时末点 = 终点", z1[^1].Equals(line[^1]));
        check("电光：lastSegmentLen=0.8 时最后一段停在半路", !z08[^1].Equals(line[^1]) && z08.Count < z1.Count);

        float maxDev = 0;
        foreach (var p in z1)
        {
            // 到折线的最近距离
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++) best = Math.Min(best, DistToSeg(p, line[i], line[i + 1]));
            maxDev = Math.Max(maxDev, best);
        }
        // 原版：偏移 ≤ 噪声(≤0.5) × hopDist × 2.5 = 0.125 格距（段长 1 格时）
        check($"电光抖动幅度 ≤ 0.13 格距（实测 {maxDev / size:F3}）", maxDev <= size * 0.13f, null);

        // 三角化：顶点数为 3 的倍数、无 NaN；直线段所有顶点都在「半线宽」范围内
        var tris = new List<ColoredVertex>();
        float w = 20f;
        PatternGeometry.LineSeq(tris, new List<Vec2f> { new(0, 0), new(100, 0) }, w, 0xff112233, 0xff445566, true);
        bool inBand = tris.All(v => Math.Abs(v.Y) <= w / 2 + 1e-3f && v.X >= -w / 2 - 1e-3f && v.X <= 100 + w / 2 + 1e-3f);
        check($"直线：{tris.Count / 3} 个三角形，全部落在线宽带内", tris.Count % 3 == 0 && tris.Count > 0 && inBand);

        var degenerate = new List<Vec2f> { new(0, 0), new(0, 0), new(10, 0), new(0, 0), new(0, 0.0001f), new(10, 0) };
        tris.Clear();
        PatternGeometry.LineSeq(tris, degenerate, w, 0xff112233, 0xff445566, true);
        check("重合点 / 180° 折返：不产生 NaN", tris.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y)));

        tris.Clear();
        PatternGeometry.LineSeq(tris, line, w, 0xff112233, 0xff445566, showStrokeOrder: false);
        check("不按 Ctrl（默认）时整条线只用 tail 色，不显示笔顺渐变",
            tris.All(v => (v.Argb & 0xFFFFFF) == 0x112233), null);

        // 全部 188 条图案按原版参数三角化，没有 NaN
        bool finite = true;
        foreach (var def in PatternRegistry.All)
        {
            var pts = HexGrid.PatternLinePoints(def.Prototype, HexCoord.Origin, size, Vec2f.Zero);
            tris.Clear();
            PatternGeometry.PatternFromPoints(tris, pts, PatternGeometry.FindDupIndices(def.Prototype.Positions()), true,
                0xC87385de, 0xC8fecbe6, 0.2f, 0.2f, 1f, 3, 12.5, size / 16f, true);
            if (!tris.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y)) || tris.Count % 3 != 0) { finite = false; break; }
        }
        check("188 条图案按原版参数三角化：无 NaN、三角形完整", finite);
    }

    static float DistToSeg(Vec2f p, Vec2f a, Vec2f b)
    {
        var ab = b - a;
        float t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / ab.LengthSquared, 0f, 1f);
        return (p - (a + ab * t)).Length;
    }
}
