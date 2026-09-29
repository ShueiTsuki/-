// 画布拖拽的端到端验证：
//   对注册表里的**每一个**图案，沿着它自己的折线拖一遍鼠标，
//   用真实的 HexPattern 状态机算最终签名，再看能不能匹配回同一条图案。
//
// 覆盖的是「几何 + 匹配」这条链路（注册表数据 → Positions() → 像素吸附 →
// TryAppendDir → AnglesSignature → Match）。输入层（鼠标事件、帧率）不在这里，
// 但玩家画不出来的图形，这里一定能先抓出来。
using HexCastingTerraria.Core.Casting.Actions;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using HexCastingTerraria.Core.Ui;

// 坐标换算用**真代码** HexGrid —— 以前这里手抄过一份公式（因为 HexGrid 依赖 XNA 被排除在外），
// 那份副本改对了、真代码改错了，测试照样全绿。现在 HexGrid 用自己的 Vec2f，
// 排除表已清空，这里测的就是游戏里跑的那份代码。
static (float X, float Y) Px((int Q, int R) c, float size)
{
    var v = HexGrid.CoordToPx(new HexCoord(c.Q, c.R), size, Vec2f.Zero);
    return (v.X, v.Y);
}

// 同样用真代码的坐标加法（HexCoord + HexDir），不再手写 6 个方向的 switch
static (int Q, int R) Step((int Q, int R) c, HexDir d)
{
    var next = new HexCoord(c.Q, c.R) + d;
    return (next.X, next.Y);
}

/// <summary>
/// 照抄 HexCanvas.DrawMove 的状态机，但只用纯数学 —— 逐行对应，改动时两边要一起改。
/// 返回最终签名的同时，把每一步的判定结果记进 trace 便于定位。
/// </summary>
static string SimulateDrag(List<(float X, float Y)> path, float size, float threshold,
                           List<string>? trace = null)
{
    float snapSq = size * size * 2f * Math.Clamp(threshold, 0.5f, 1f);

    var anchor = (Q: 0, R: 0);
    var start = (Q: 0, R: 0);
    HexPattern? wip = null;
    bool started = false;

    foreach (var (mx, my) in path)
    {
        if (!started)                       // DrawStart：落笔
        {
            started = true;
            continue;
        }

        var (ax, ay) = Px(anchor, size);
        float dx = mx - ax, dy = my - ay;

        if (dx * dx + dy * dy < snapSq) continue;      // 距离不够，本帧不记

        float turns = MathF.Atan2(dy, dx) / (MathF.PI * 2f) * 6f;
        int snapped = ((int)MathF.Round(turns) + 1) % 6;
        if (snapped < 0) snapped += 6;
        var newDir = (HexDir)snapped;

        var idealNext = Step(anchor, newDir);

        if (wip is null)                    // JustStarted -> 第一段
        {
            wip = new HexPattern(newDir);
            anchor = idealNext;
            trace?.Add($"第一段 {newDir}");
            continue;
        }

        var lastDir = wip.FinalDir();
        if (newDir == lastDir.RotatedBy(HexAngle.Back))   // 反方向 -> 回溯
        {
            if (wip.Length == 0)
            {
                anchor = idealNext;
                start = anchor;
                trace?.Add("回溯到起点");
            }
            else
            {
                anchor = idealNext;
                wip.RemoveLastAngle();
                trace?.Add($"回溯（删掉一段）→ {wip.AnglesSignature()}");
            }
            continue;
        }

        if (wip.TryAppendDir(newDir))
        {
            anchor = idealNext;
            trace?.Add($"加一段 {newDir} → {wip.AnglesSignature()}");
        }
        else
        {
            trace?.Add($"拒绝 {newDir}（该方向的线已经画过）");
        }
    }

    return wip?.AnglesSignature() ?? "";
}

/// <summary>
/// 沿一段直线按固定步长采样鼠标位置。
///
/// `wobble` 模拟手抖，用的是**低频摆动**而不是逐点白噪声 ——
/// 真人拖鼠标是连续运动，相邻采样点不会独立乱跳；
/// 用白噪声会把「手抖」模拟得比现实恶劣得多，测出来的失败是假的。
/// </summary>
static void SampleLine(List<(float X, float Y)> into, (float X, float Y) a, (float X, float Y) b,
                       int steps, float wobble = 0f, float phase = 0f)
{
    for (int k = 0; k < steps; k++)
    {
        float t = k / (float)steps;
        float x = a.X + (b.X - a.X) * t;
        float y = a.Y + (b.Y - a.Y) * t;
        if (wobble > 0f)
        {
            float s = phase + t * 3f;
            x += MathF.Sin(s) * wobble;
            y += MathF.Cos(s * 1.37f) * wobble;
        }
        into.Add((x, y));
    }
}

int passed = 0, failed = 0;
void Check(string name, bool ok, string? detail = null)
{
    if (ok) { passed++; Console.WriteLine($"  PASS  {name}"); }
    else { failed++; Console.WriteLine($"  FAIL  {name}   {detail}"); }
}

PatternRegistry.EnsureLoaded();

// ⚠️ 必须显式注册图案行为：注册表只装**图案数据**，
// 「这条图案能干什么」是 HexActions.RegisterAll() 灌进去的
// （模组里由 HexCastingTerraria.Load 调用）。漏了这一步，
// 所有图案都会显示成「未实现」—— 这个坑我在写本工程时踩过一次。
HexActions.RegisterAll();

// 1920x1080 下的真实格距：sqrt(w*h/512)
float size = MathF.Sqrt(1920f * 1080f / 512f);
Console.WriteLine($"格距 size = {size:F1} px，图案总数 = {PatternRegistry.Count}\n");

Console.WriteLine("=== ① 全部 188 个图案：沿自己的折线拖一遍，能否匹配回自己 ===");
{
    var bad = new List<string>();
    foreach (var def in PatternRegistry.All)
    {
        var positions = def.Prototype.Positions();
        var path = new List<(float X, float Y)>();
        var coords = positions.Select(p => (Q: p.X, R: p.Y)).ToList();

        for (int i = 0; i < coords.Count - 1; i++)
        {
            var a = Px(coords[i], size);
            var b = Px(coords[i + 1], size);
            SampleLine(path, a, b, 30);
        }
        path.Add(Px(coords[^1], size));

        string got = SimulateDrag(path, size, 0.5f);
        if (got != def.Angles)
        {
            bad.Add($"{def.Id}: 期望 {def.Angles} 实得 {got}");
            continue;
        }

        // 再走一遍真实的匹配
        var rebuilt = HexPattern.TryFromAngles(got, def.StartDir, out var proto, out var err);
        if (!rebuilt || proto is null)
        {
            bad.Add($"{def.Id}: 签名 {got} 无法重建（{err}）");
            continue;
        }
        var m = PatternRegistry.Match(proto);
        if (m?.Id != def.Id)
        {
            bad.Add($"{def.Id}: 匹配成了 {m?.Id ?? "null"}");
        }
    }

    Check($"188 个图案全部能画出来并匹配回自己（失败 {bad.Count} 条）", bad.Count == 0,
        bad.Count == 0 ? null : string.Join(" | ", bad.Take(8)));
}

Console.WriteLine("\n=== ② 意识之精思（qaq 菱形）单独细查 ===");
{
    var def = PatternRegistry.Match("qaq");
    Check("注册表里 qaq 命中 get_caster", def?.Id == "hexcasting:get_caster", def?.Id ?? "null");
    Check("qaq 已被标记为可实现", def != null && PatternRegistry.HasAction(def),
        def == null ? "def 为 null" : "没有 action");

    var positions = def!.Prototype.Positions();
    Check($"菱形格点数 = 5（含回到起点）", positions.Count == 5, $"实际 {positions.Count}");

    var path = new List<(float X, float Y)>();
    for (int i = 0; i < positions.Count - 1; i++)
    {
        SampleLine(path, Px((positions[i].X, positions[i].Y), size),
                         Px((positions[i + 1].X, positions[i + 1].Y), size), 30);
    }
    path.Add(Px((positions[^1].X, positions[^1].Y), size));

    var trace = new List<string>();
    string got = SimulateDrag(path, size, 0.5f, trace);
    Console.WriteLine("     拖拽过程：" + string.Join(" → ", trace));
    Check($"阈值 0.5 实得 {got}（期望 qaq）", got == "qaq", got);

    var trace10 = new List<string>();
    string got10 = SimulateDrag(path, size, 1.0f, trace10);
    Check($"阈值 1.0 实得 {got10}（期望 qaq）", got10 == "qaq", got10);

    // 起点差一点没回到原位（手抖）—— 闭合图形最容易在这里丢最后一段
    foreach (float shortfall in new[] { 2f, 6f, 12f })
    {
        var p2 = new List<(float X, float Y)>(path);
        var last = p2[^1];
        var prev = p2[^2];
        float d = MathF.Sqrt((last.X - prev.X) * (last.X - prev.X) + (last.Y - prev.Y) * (last.Y - prev.Y));
        if (d > 0)
        {
            p2[^1] = (last.X - (last.X - prev.X) / d * shortfall,
                      last.Y - (last.Y - prev.Y) / d * shortfall);
        }
        string g = SimulateDrag(p2, size, 0.5f);
        Check($"松手差 {shortfall}px 仍识别为 qaq（实得 {g}）", g == "qaq", g);
    }

    // 换一个角起笔。positions() 的最后一项与首项重合（闭合），
    // 所以先取「不重复的一圈」，再整体轮转一位，最后把新的首点补回末尾闭合。
    var cycle = def.Prototype.Positions().Take(def.Prototype.Positions().Count - 1).ToList();
    var rotated = cycle.Skip(1).Concat(cycle.Take(1)).ToList();
    rotated.Add(rotated[0]);

    var rp = new List<(float X, float Y)>();
    var basePx = Px((rotated[0].X, rotated[0].Y), size);
    for (int i = 0; i < rotated.Count - 1; i++)
    {
        var a = Px((rotated[i].X, rotated[i].Y), size);
        var b = Px((rotated[i + 1].X, rotated[i + 1].Y), size);
        SampleLine(rp, (a.X - basePx.X, a.Y - basePx.Y), (b.X - basePx.X, b.Y - basePx.Y), 30);
    }
    var lastPx = Px((rotated[^1].X, rotated[^1].Y), size);
    rp.Add((lastPx.X - basePx.X, lastPx.Y - basePx.Y));

    string rot = SimulateDrag(rp, size, 0.5f);
    var rotDef = PatternRegistry.Match(rot);
    Console.WriteLine($"     换到「右上角」起笔 → 签名 \"{rot}\"（注册表里{(rotDef != null ? "有：" + rotDef.Id : "没有")}）");
    Check($"换角起笔得到**循环移位**后的签名，与原版「必须按图上的起点落笔」一致",
        rot != "qaq", $"实得 {rot}（若与 qaq 相同，说明起点其实无所谓，图上的红点就该去掉）");
}

Console.WriteLine("\n=== ③ 手抖鲁棒性：188 个图案各带平滑摆动重画一遍 ===");
{
    int bad = 0;
    var badNames = new List<string>();
    float phase = 0f;
    foreach (var def in PatternRegistry.All)
    {
        var positions = def.Prototype.Positions();
        var path = new List<(float X, float Y)>();
        for (int i = 0; i < positions.Count - 1; i++)
        {
            SampleLine(path, Px((positions[i].X, positions[i].Y), size),
                             Px((positions[i + 1].X, positions[i + 1].Y), size),
                       30, wobble: 16f, phase: phase);
            phase += 1.7f;
        }
        path.Add(Px((positions[^1].X, positions[^1].Y), size));
        if (SimulateDrag(path, size, 0.5f) != def.Angles) { bad++; badNames.Add(def.Id); }
    }
    Check($"摆动 ±16px（约 25% 格距）时仍有 {188 - bad}/188 正确", bad == 0,
        badNames.Count == 0 ? null : string.Join(", ", badNames.Take(10)));
}

Console.WriteLine("\n=== ④ 坐标换算（HexGrid 真代码，以前是手抄副本）===");
{
    float realSize = HexGrid.HexSize(1920f, 1080f);   // 游戏里 1920x1080 的实际格距

    Check("格距公式 = √(w·h/512)", MathF.Abs(realSize - MathF.Sqrt(1920f * 1080f / 512f)) < 1e-4f,
        $"{realSize}");
    Check("zoom=2 时格距减半",
        MathF.Abs(HexGrid.HexSize(1920f, 1080f, 2f) - realSize / 2f) < 1e-4f);
    Check("zoom<=0 时退回 1（不产生除零/NaN）",
        MathF.Abs(HexGrid.HexSize(1920f, 1080f, 0f) - realSize) < 1e-4f);

    // 往返：格点 → 像素 → 格点，必须回到原格点。
    // 这是画布、射线、瞄准预览共用的底座，错一点就会出现
    // 「预览指着一个地方、真正施法打到另一个地方」而且两边都不报错。
    int bad = 0;
    var fails = new List<string>();
    foreach (float scale in new[] { 0.35f, 1f, 2.6f, 7f })
    {
        float cellSize = realSize * scale;
        var offset = new Vec2f(960f, 540f);        // 非零偏移也要成立（画布是居中的）
        for (int q = -6; q <= 6; q++)
        {
            for (int r = -6; r <= 6; r++)
            {
                var c = new HexCoord(q, r);
                var px = HexGrid.CoordToPx(c, cellSize, offset);
                var back = HexGrid.PxToCoord(px, cellSize, offset);
                if (back.X != q || back.Y != r)
                {
                    bad++;
                    if (fails.Count < 4) fails.Add($"size×{scale} ({q},{r}) → ({back.X},{back.Y})");
                }
            }
        }
    }
    Check("往返：CoordToPx → PxToCoord 回到原格点（4 种缩放 × 169 个格点）", bad == 0,
        string.Join("; ", fails));

    // 格点附近的小偏移仍应吸附回同一个格点（半径取格距的 25%）
    int nearBad = 0;
    foreach (var (dq, dr) in new[] { (0.25f, 0f), (-0.25f, 0f), (0f, 0.25f), (0f, -0.25f) })
    {
        var c = new HexCoord(3, -2);
        var px = HexGrid.CoordToPx(c, realSize, Vec2f.Zero);
        var back = HexGrid.PxToCoord(new Vec2f(px.X + dq * realSize, px.Y + dr * realSize),
                                     realSize, Vec2f.Zero);
        if (back.X != 3 || back.Y != -2) nearBad++;
    }
    Check("格点附近 ±25% 格距的偏移仍吸附回该格点", nearBad == 0, $"{nearBad} 个跑偏");

    // 相邻格点正中间：必须落在两者之一，不允许返回第三个格点
    var a1 = new HexCoord(0, 0);
    var a2 = a1 + HexDir.East;
    var mid = (HexGrid.CoordToPx(a1, realSize, Vec2f.Zero) + HexGrid.CoordToPx(a2, realSize, Vec2f.Zero)) * 0.5f;
    var midBack = HexGrid.PxToCoord(mid, realSize, Vec2f.Zero);
    Check("相邻格点中点 → 落在两端之一（不会返回无关格点）",
        (midBack.X == a1.X && midBack.Y == a1.Y) || (midBack.X == a2.X && midBack.Y == a2.Y),
        $"({midBack.X},{midBack.Y})");

    // RangeAround：格点数必须是 1+3r(r+1)，且都在半径内
    bool rangeOk = true;
    for (int r = 0; r <= 4; r++)
    {
        int count = 0;
        foreach (var c in HexGrid.RangeAround(new HexCoord(2, -1), r))
        {
            count++;
            if (HexGrid.Distance(c, new HexCoord(2, -1)) > r) rangeOk = false;
        }
        if (count != 1 + 3 * r * (r + 1)) rangeOk = false;
    }
    Check("RangeAround 格点数 = 1+3r(r+1) 且距离不超半径（r=0..4）", rangeOk);

    // Distance：自身为 0、对称
    Check("Distance 自身为 0 且对称",
        HexGrid.Distance(new HexCoord(5, 5), new HexCoord(5, 5)) == 0
        && HexGrid.Distance(new HexCoord(1, 2), new HexCoord(-3, 4))
           == HexGrid.Distance(new HexCoord(-3, 4), new HexCoord(1, 2)));

    // PatternLinePoints：点数与 Positions 一致，首点等于起点的像素坐标
    var sample = PatternRegistry.All[0].Prototype;
    var origin = new HexCoord(4, 7);
    var line = HexGrid.PatternLinePoints(sample, origin, realSize, Vec2f.Zero);
    var expectFirst = HexGrid.CoordToPx(origin, realSize, Vec2f.Zero);
    Check("PatternLinePoints 点数 = Positions 点数，且首点 = 起点像素坐标",
        line.Count == sample.Positions(origin).Count
        && MathF.Abs(line[0].X - expectFirst.X) < 1e-4f
        && MathF.Abs(line[0].Y - expectFirst.Y) < 1e-4f,
        $"{line.Count} vs {sample.Positions(origin).Count}");

    // 零向量归一化不得兜底成 (1,0) —— 那会把「方向未知」变成「向右」，调用方再也发现不了
    Check("Vec2f.Normalized 对零向量返回零向量（不偷偷兜底成 (1,0)）",
        Vec2f.Zero.Normalized().LengthSquared == 0f);
    Check("Vec2f.Normalized 对普通向量返回单位长度",
        MathF.Abs((new Vec2f(3f, 4f).Normalized()).Length - 1f) < 1e-5f);
}

Console.WriteLine("\n=== ⑤ 法术序列的栈平衡检查 ===");
{
    // 每个图案的净栈效果 = 1 - Argc（弹出 Argc 个参数、压回 1 个结果）。
    // Argc 取自注册表里真实的 action，不是猜的 —— 这正是画法术图时最容易搞错的地方：
    // 「entity_pos/eye 会把实体换成向量」这种副作用，
    // 只看图案名是看不出来的（我给的第一版法术图就踩了这个坑）。
    // 注意：PatternRegistry.Match(string) 是**按角度签名**匹配的，不是按 id。
    // 传 id 进去永远返回 null（这里第一版就是这么错的，所有图案都显示「无此图案」）。
    PatternDef? DefOf(string id)
    {
        foreach (var d in PatternRegistry.All)
        {
            if (d.Id == id) return d;
        }
        return null;
    }

    // 诊断用：把 action 的真实类型打出来。
    string TypeOf(string id)
    {
        var def = DefOf(id);
        if (def == null) return "无此图案";
        if (!PatternRegistry.TryGetAction(def, out var a) || a == null) return "**没有 action**";
        return a.GetType().Name;
    }


    // 正确的法术序列 —— 必须全部通过
    var goodSpells = new (string Name, string[] Ids)[]
    {
        ("① 读出圆周率 π", new[] { "hexcasting:const/double/pi", "hexcasting:print" }),
        ("② 看自己的坐标", new[] { "hexcasting:get_caster", "hexcasting:entity_pos/eye", "hexcasting:print" }),
        ("③ 向上跳高", new[] { "hexcasting:get_caster", "hexcasting:const/vec/ny", "hexcasting:add_motion" }),
        ("④ 朝准星闪现 3 格", new[] { "hexcasting:get_caster", "(数字字面量 3)", "hexcasting:blink" }),
        ("⑤ 隔空挖方块（取两次施法者）", new[]
            { "hexcasting:get_caster", "hexcasting:entity_pos/eye",
              "hexcasting:get_caster", "hexcasting:get_entity_look",
              "hexcasting:raycast", "hexcasting:break_block" }),
    };

    // 曾经写错、并且**真的在游戏里造成了错误行为**的序列。
    // 断言检查器必须能把它抓出来 —— 否则这套检查等于白加。
    // 历史现场：少取一次施法者 → get_entity_look / raycast 依次报错但栈不变
    //           → break_block 拿到残留的眼位向量 → 挖掉了玩家自己脚下那一格。
    var knownBadSpells = new (string Name, string[] Ids)[]
    {
        ("⑤ 少取一次施法者（历史 bug）", new[]
            { "hexcasting:get_caster", "hexcasting:entity_pos/eye", "hexcasting:get_entity_look",
              "hexcasting:raycast", "hexcasting:break_block" }),
    };

    // ⚠️ 本检查的**能力边界**：它只看栈深度，看不出「类型对不上」。
    //
    // 反例：`get_caster → entity_pos/eye → const/vec/ny → add_motion`
    //   · 深度：1 → 1 → 2 → 需要 2 ✓ 不欠账，检查通过；
    //   · 但 add_motion 的第一个参数要的是**实体**，而这里被 entity_pos/eye 换成了**坐标**，
    //     实际运行会报「参数不是实体」。
    //
    // 要抓这一类，需要给每个图案标注「消耗什么类型 / 产出什么类型」再做轻量类型推断。
    // 现在没做 —— 所以它被单独列出来当**已知盲区**，而不是假装能查。
    // 在游戏里踩到的表现就是：施法"成功"，但干的是另一件事
    // （历史现场：break_block 挖掉了玩家自己脚下那一格）。
    var typeBlindSpots = new (string Name, string[] Ids)[]
    {
        ("③ 多一步 entity_pos/eye 把实体换成坐标", new[]
            { "hexcasting:get_caster", "hexcasting:entity_pos/eye",
              "hexcasting:const/vec/ny", "hexcasting:add_motion" }),
    };

    // ── 带类型推断的检查器 ───────────────────────────────────────────
    //
    // 旧版只算深度，于是漏掉了整整一类问题：**能编译、能过全部现有测试、只在游戏里炸**。
    // 现在每个 action 可以声明 ActionTypes（消耗什么 / 产出什么），检查器据此做轻量类型推断。
    // 没标注的图案按「未知」处理、不参与判定 —— 只查两端都确定的组合，
    // 绝不因为"有人忘了标注"而报假错（假错会让断言变成噪音，最后被关掉）。

    // 净栈效果 —— 这是**读源码写下来的模型**，不是抄来的常数：
    //   ConstMediaAction：弹 Argc、压回 1 个结果 → 1 - Argc
    //   SpellAction     ：弹 Argc、**什么都不压** → -Argc
    //     （SpellAction.cs：RemoveRange 之后直接 WithStack，中间没有 Add）
    // 这里先前对两者都按 1-Argc 算 —— 又是一处"手抄模型"，已修。
    (int Argc, int Delta, int AssumedPushes)? ShapeOf(string id)
    {
        var def = DefOf(id);
        if (def == null || !PatternRegistry.TryGetAction(def, out var a) || a == null) return null;
        return a switch
        {
            HexCastingTerraria.Core.Casting.Castables.ConstMediaAction c => (c.Argc, 1 - c.Argc, 1),
            HexCastingTerraria.Core.Casting.Castables.SpellAction s => (s.Argc, -s.Argc, 0),
            _ => null,
        };
    }

    HexCastingTerraria.Core.Casting.Castables.ActionTypes? TypesOf(string id)
    {
        var def = DefOf(id);
        if (def == null || !PatternRegistry.TryGetAction(def, out var a) || a == null) return null;
        return a.Types;
    }

    const string Any = HexCastingTerraria.Core.Casting.Castables.IotaTypes.Any;

    // 诊断：**默认接口成员不会被派生类的同名成员重新绑定**。
    //
    // 这是本项目踩过的一个真坑：`IAction.Types` 写成默认实现（`=> ActionTypes.Unknown`）之后，
    // 在 `OpGetCaster` 里写 `public ActionTypes Types => ...` 能编译、看着也对，
    // 但**以 IAction 引用调用时永远走默认值** —— 于是类型检查静默失效，一路"全绿"。
    // 修法是在两个基类（ConstMediaAction / SpellAction）上声明 virtual 成员。
    // 这段诊断留着：它一红就说明有人把那个 virtual 改回默认接口实现了。
    {
        var d0 = DefOf("hexcasting:get_caster");
        PatternRegistry.TryGetAction(d0!, out var a0);
        HexCastingTerraria.Core.Casting.Castables.IAction asIface =
            new HexCastingTerraria.Core.Casting.Actions.OpGetCaster();

        bool concrete = asIface.Types.IsKnown;
        Check("类型契约能经 IAction 引用取到（默认接口成员不会自动被派生类覆盖）",
            concrete && a0?.Types.IsKnown == true,
            $"具体类型 IsKnown={concrete}，注册表实例 IsKnown={a0?.Types.IsKnown}");
    }

    // 返回错误描述；null = 通过。trace 记录每一步的栈深与类型。
    string? CheckSpell(string[] ids, List<string> trace, out int typeErrors)
    {
        typeErrors = 0;
        var stack = new List<string?>();     // null = 类型未知
        foreach (var id in ids)
        {
            string shortId = id.Split(':')[^1];
            var shape = ShapeOf(id);

            if (shape is not { } sh)
            {
                // 不在注册表里（数字字面量这类特殊图案）：按"弹 0、压 1 个未知值"处理
                stack.Add(null);
                trace.Add($"{shortId}[特殊图案]→{stack.Count}");
                continue;
            }

            if (stack.Count < sh.Argc)
            {
                trace.Add($"!! {shortId} 需要 {sh.Argc} 个参数，栈里只有 {stack.Count}");
                return $"欠账于 {shortId}";
            }

            int baseIdx = stack.Count - sh.Argc;      // 被弹出那一段里最深的一个
            var types = TypesOf(id);

            if (types.HasValue && types.Value.IsKnown)
            {
                var t = types.Value;
                for (int i = 0; i < sh.Argc && i < t.Consumes.Length; i++)
                {
                    string declared = t.Consumes[i];
                    string? actual = stack[baseIdx + i];
                    if (declared != Any && actual != null && actual != declared)
                    {
                        typeErrors++;
                        trace.Add($"!! {shortId} 第 {i + 1} 个参数要 {declared}，实际是 {actual}");
                        return $"类型不符：{shortId} 要 {declared}，拿到 {actual}";
                    }
                }
            }

            stack.RemoveRange(baseIdx, sh.Argc);

            string mark;
            if (types.HasValue && types.Value.IsKnown)
            {
                foreach (string p in types.Value.Produces) stack.Add(p);
                mark = types.Value.Produces.Length == 0 ? "(不产出)" : "";
            }
            else
            {
                // 没标注：按"这个基类通常的行为"假设，并在 trace 里**显式标出这是假设**。
                // 假设必须看得见 —— 看不见的假设会悄悄变成"事实"，然后在不该信的地方被信。
                for (int i = 0; i < sh.AssumedPushes; i++) stack.Add(null);
                mark = sh.AssumedPushes == 0 ? "(未标注/假设不产出)" : "(未标注/假设压1)";
            }

            trace.Add($"{shortId}[{TypeOf(id)}]{mark}({sh.Delta:+0;-#})→{stack.Count}");
        }
        return null;
    }

    foreach (var (name, ids) in goodSpells)
    {
        var trace = new List<string>();
        string? err = CheckSpell(ids, trace, out _);
        Console.WriteLine($"  {(err == null ? "·" : "✗")} {name}");
        Console.WriteLine($"      {string.Join("  ", trace)}");
        Check($"{name} 栈与类型都成立", err == null, err);
    }

    foreach (var (name, ids) in knownBadSpells)
    {
        var trace = new List<string>();
        string? err = CheckSpell(ids, trace, out _);
        Console.WriteLine($"  （反例）{(err != null ? "已检出" : "**漏过**")}  {name}");
        Console.WriteLine($"      {string.Join("  ", trace)}");
        Check($"反例「{name}」必须被检出", err != null, "检查器漏过了这个错误序列");
    }

    // 类型不符的反例：深度完全合法，只有类型检查能抓到。
    // 这正是问题 #5 那一类 —— 历史上它在游戏里的表现是"施法成功，但挖了自己脚下那一格"。
    var typeBadSpells = new (string Name, string[] Ids)[]
    {
        ("③ 多一步 entity_pos/eye 把实体换成坐标", new[]
            { "hexcasting:get_caster", "hexcasting:entity_pos/eye",
              "hexcasting:const/vec/ny", "hexcasting:add_motion" }),
        ("把坐标当实体传给 get_entity_look", new[]
            { "hexcasting:get_caster", "hexcasting:entity_pos/eye", "hexcasting:get_entity_look" }),
    };

    foreach (var (name, ids) in typeBadSpells)
    {
        var trace = new List<string>();
        string? err = CheckSpell(ids, trace, out int typeErrs);
        Console.WriteLine($"  （类型反例）{(err != null ? "已检出" : "**漏过**")}  {name}");
        Console.WriteLine($"      {string.Join("  ", trace)}");
        Check($"类型反例「{name}」必须被检出", err != null, "类型检查漏过了这个序列");
    }
}

Console.WriteLine("\n=== ⑥ 书的布局数学（格子画在哪 ↔ 点在哪）===");
{
    // 这段数学以前在 HexBook 里**写了两遍**：一遍给命中判定、一遍给绘制。
    // 两份只要有一点不一致，表现就是「点不中/点到隔壁」，而且完全不报错。
    // 现在只有一份（Core/Ui/BookLayout），这里断言那条最关键的性质：互逆。
    var viewSizes = new[] { (1280f, 720f), (1920f, 1080f), (2560f, 1440f), (800f, 600f) };
    var counts = new[] { 1, 7, 9, 10, 11, 40, 188 };

    int mismatches = 0;
    int overlaps = 0;
    string firstFail = "";

    foreach (var (vw, vh) in viewSizes)
    {
        var panel = BookLayout.Panel(vw, vh);
        var grid = BookLayout.Grid(panel);
        int cols = BookLayout.Columns(grid.W);

        foreach (int n in counts)
        {
            float maxScroll = BookLayout.MaxScroll(n, grid);
            foreach (float scrollFactor in new[] { 0f, 0.5f, 1f })
            {
                float scroll = maxScroll * scrollFactor;

                // ① 互逆：**中心落在可视网格内**的格子，点它的中心必须判回同一个下标。
                //
                // 第一版断言的是"所有格子中心"，结果 1280x720 / 40 条 / scroll=0 时红了：
                // 第 30 个格子的中心在网格**下方**（还没滚到），IndexAt 返回 -1 是**对的** ——
                // 错的是断言本身。所以这里限定只查真正可见的格子；
                // "可见的格子是否点得到"由下面第 ⑨ 条单独兜住。
                int visibleChecked = 0;
                for (int i = 0; i < n; i++)
                {
                    var cell = BookLayout.Cell(grid, i, scroll);
                    float cx = cell.X + cell.W * 0.5f;
                    float cy = cell.Y + cell.H * 0.5f;
                    if (!grid.Contains(cx, cy)) continue;
                    visibleChecked++;

                    int back = BookLayout.IndexAt(grid, cx, cy, n, scroll);
                    if (back != i)
                    {
                        mismatches++;
                        if (firstFail == "") firstFail = $"{vw}x{vh} n={n} scroll={scroll:0.#} i={i} → {back}";
                    }
                }
                if (visibleChecked == 0) overlaps += 100000;   // 一个都没查到 = 断言空转，视为失败

                // ② 同一行相邻格子不重叠
                for (int i = 0; i + 1 < n; i++)
                {
                    if (i % cols == cols - 1) continue;      // 换行处不比
                    var a = BookLayout.Cell(grid, i, scroll);
                    var b = BookLayout.Cell(grid, i + 1, scroll);
                    if (a.Overlaps(b)) overlaps++;
                }
            }
        }
    }

    Check($"互逆：可见格子的中心点都判回自己（{viewSizes.Length} 分辨率 × {counts.Length} 数量 × 3 滚动位）",
        mismatches == 0, firstFail);
    Check("同一行相邻格子不重叠", overlaps == 0, $"{overlaps} 对重叠");

    // ③ 极端窄面板：列数不能变成 0（会除零 / 丢内容）
    Check("列数恒 ≥ 1（面板宽 1px 时也一样）",
        BookLayout.Columns(1f) >= 1 && BookLayout.Columns(0f) >= 1 && BookLayout.Columns(-5f) >= 1,
        $"1px→{BookLayout.Columns(1f)} 0px→{BookLayout.Columns(0f)}");

    // ④ 行数足够装下全部条目
    bool rowsOk = true;
    foreach (var (vw, vh) in viewSizes)
    {
        var grid = BookLayout.Grid(BookLayout.Panel(vw, vh));
        foreach (int n in counts)
        {
            if (BookLayout.Rows(n, grid.W) * BookLayout.Columns(grid.W) < n) rowsOk = false;
        }
    }
    Check("行数 × 列数 ≥ 条目数（不会漏掉最后一行）", rowsOk);

    // ⑤ 内容装得下时滚动范围必须为 0，否则会「能滚但滚不动」
    {
        var grid = BookLayout.Grid(BookLayout.Panel(1920f, 1080f));
        int fits = BookLayout.Columns(grid.W) * (int)(grid.H / BookLayout.CellH);
        bool ok = BookLayout.MaxScroll(fits, grid) == 0f && BookLayout.MaxScroll(fits + 100, grid) > 0f;
        Check("内容装得下 → 最大滚动为 0；装不下 → 大于 0", ok,
            $"fits={fits} MaxScroll(fits)={BookLayout.MaxScroll(fits, grid):0.#}");
    }

    // ⑥ 网格外的点必须返回 -1（不能误选）
    {
        var grid = BookLayout.Grid(BookLayout.Panel(1920f, 1080f));
        bool outside = BookLayout.IndexAt(grid, grid.X - 1f, grid.Y + 10f, 40, 0f) == -1
                    && BookLayout.IndexAt(grid, grid.X + 10f, grid.Y - 1f, 40, 0f) == -1
                    && BookLayout.IndexAt(grid, grid.Right + 1f, grid.Y + 10f, 40, 0f) == -1;
        Check("网格外的点返回 -1（不会误选条目）", outside);
    }

    // ⑦ 最后一行的空白格不能选中（数量不是列数整数倍时）
    {
        var grid = BookLayout.Grid(BookLayout.Panel(1920f, 1080f));
        int cols = BookLayout.Columns(grid.W);
        int n = cols + 1;                      // 第二行只有一个格子
        var empty = BookLayout.Cell(grid, n, 0f);   // 第二行第二列（不存在）
        int hit = BookLayout.IndexAt(grid, empty.X + empty.W * 0.5f, empty.Y + empty.H * 0.5f, n, 0f);
        Check("最后一行空白格返回 -1（点空白不会选中越界条目）", hit == -1, $"实得 {hit}");
    }

    // ⑨ 绘制说"可见"的格子必须真的点得到 ——
    // 这条兜住"画出来了但点不中"：DrawGrid 用 CellVisible 决定画不画，
    // 而 IndexAt 用网格边界决定能不能选中，两者必须是同一套标准。
    {
        int unreachable = 0;
        string failDetail = "";
        foreach (var (vw, vh) in viewSizes)
        {
            var grid = BookLayout.Grid(BookLayout.Panel(vw, vh));
            foreach (int n in counts)
            {
                float maxScroll = BookLayout.MaxScroll(n, grid);
                foreach (float sf in new[] { 0f, 0.5f, 1f })
                {
                    float scroll = maxScroll * sf;
                    for (int i = 0; i < n; i++)
                    {
                        var cell = BookLayout.Cell(grid, i, scroll);
                        if (!BookLayout.CellVisible(grid, cell)) continue;

                        // 取格子与网格的可见交集里的一点（避开边界取 1px 内缩）
                        float px = System.Math.Max(cell.X, grid.X) + 1f;
                        float py = System.Math.Max(cell.Y, grid.Y) + 1f;
                        if (!grid.Contains(px, py)) continue;       // 交集为空

                        int hit = BookLayout.IndexAt(grid, px, py, n, scroll);
                        if (hit != i)
                        {
                            unreachable++;
                            if (failDetail == "") failDetail = $"{vw}x{vh} n={n} scroll={scroll:0.#} 画了 {i} 但点到 {hit}";
                        }
                    }
                }
            }
        }
        Check("CellVisible 说可见的格子，真的点得到（不会画出来却点不中）", unreachable == 0, failDetail);
    }

    // ⑧ 面板必须放得进视口（含留白），且网格与详情栏不重叠
    {
        bool ok = true;
        foreach (var (vw, vh) in viewSizes)
        {
            var panel = BookLayout.Panel(vw, vh);
            if (panel.X < 0 || panel.Y < 0 || panel.Right > vw || panel.Bottom > vh) ok = false;
            var grid = BookLayout.Grid(panel);
            var detail = BookLayout.Detail(panel);
            if (grid.Overlaps(detail)) ok = false;
        }
        Check("面板放得进视口，且网格与右侧详情栏不重叠", ok);
    }
}

// ── 书本排版引擎（Core/Ui/BookText.cs）──────────────────────────────
//
// 这一节的意义：排版是整本书里**唯一不看图也能测**的部分。
// 「字被切掉」「最后一页溢出」「某个词把整行撑爆转圈」这些毛病肉眼极难发现，
// 但用一段带标记的文本就能断言出来。宽度靠回调喂 —— 这里用「一个字符 = 1 单位」的假测量函数。
{
    static int Measure1(string s) => s.Length;

    // 把分好页的所有行拼回纯文本，用来验证「一个字都没丢」
    static string JoinAll(List<HexCastingTerraria.Core.Ui.BookTextPage> pages)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var pg in pages)
        {
            foreach (var ln in pg.Lines) { sb.Append(ln.PlainText()); }
        }
        return sb.ToString();
    }

    // ① 常规折行：宽度 10、每页 3 行
    {
        var pages = HexCastingTerraria.Core.Ui.BookTextLayout.Layout(
            "aaa bbb ccc ddd eee fff", 10, 3, Measure1);
        int lines = 0;
        foreach (var pg in pages) { lines += pg.Lines.Count; }
        // 每词 4 字符（含尾随空格），宽 10 → 每行 2 个词；6 个词 = 3 行，正好一页（每页 3 行）
        Check($"折行：6 个词 / 宽 10 → 共 {lines} 行、{pages.Count} 页",
              pages.Count == 1 && lines == 3, $"页 {pages.Count} 行 {lines}");
        Check("折行后拼回原文 = 原文（一个字都没丢）",
              JoinAll(pages) == "aaa bbb ccc ddd eee fff", JoinAll(pages));
    }

    // ② 超长单词：不能死循环，且内容完整
    {
        var longWord = new string('x', 55);
        var pages = HexCastingTerraria.Core.Ui.BookTextLayout.Layout(longWord, 10, 100, Measure1);
        int lines = 0;
        foreach (var pg in pages) { lines += pg.Lines.Count; }
        Check($"超长单词（55 字符 / 行宽 10）被硬切成有限行（实得 {lines} 行，未死循环）",
              lines > 0 && lines < 100, $"{lines}");
        Check("硬切后内容完整", JoinAll(pages) == longWord, $"{JoinAll(pages).Length} vs {longWord.Length}");
    }

    // ③ 宽度传 0 / 负数：夹到 1，不许卡死。
    //    "a b c d" 共 7 个字符，宽 1 时每个字符一行 → 7 行是**正确**结果，
    //    要断言的是「有限行 + 内容不丢」，不是「行数少」。
    foreach (var w in new[] { 0, -5 })
    {
        var pages = HexCastingTerraria.Core.Ui.BookTextLayout.Layout("a b c d", w, 100, Measure1);
        int lines = 0;
        foreach (var pg in pages) { lines += pg.Lines.Count; }
        Check($"宽度 {w} 夹到 1 且不卡死（实得 {lines} 行，内容 {(JoinAll(pages) == "a b c d" ? "完整" : "丢失")}）",
              lines > 0 && lines <= 7 && JoinAll(pages) == "a b c d", $"{lines}");
    }

    // ④ 测量函数恒返回 0：最阴的一种 —— 任何宽度都放得下，容易写出无限循环
    {
        static int Measure0(string s) => 0;
        var pages = HexCastingTerraria.Core.Ui.BookTextLayout.Layout("a b c", 10, 100, Measure0);
        int lines = 0;
        foreach (var pg in pages) { lines += pg.Lines.Count; }
        Check($"测量恒为 0 时不卡死（实得 {lines} 行）", lines > 0 && lines <= 3, $"{lines}");
    }

    // ⑤ 硬换行标记。
    //    ⚠️ 这里有个**分层**要分清：Parse 只认标记（$(br) 等），
    //    裸 `\n` 是**文本里的字符**，由 Wrap 在折行时切开 —— 上一版把两者混为一谈，
    //    断言写成「Parse 出 5 个断行」自然对不上（实得 3）。
    {
        var segs = HexCastingTerraria.Core.Ui.BookTextLayout.Parse("a$(br)b$(br2)c$(p)d\ne");
        int breaks = 0;
        foreach (var s in segs) { if (s.Text == "\n") breaks++; }
        Check($"Parse：3 个标记断行 = 3 个 \\n 段（实得 {breaks}）", breaks == 3, $"{breaks}");
        Check("Parse 后文本没被吞掉（裸 \\n 仍留在文本里）",
              string.Concat(segs.ConvertAll(s => s.Text).ToArray()) == "a\nb\nc\nd\ne",
              string.Concat(segs.ConvertAll(s => s.Text).ToArray()).Replace("\n", "\\n"));

        var wrapped = HexCastingTerraria.Core.Ui.BookTextLayout.Wrap(segs, 100, Measure1);
        Check($"Wrap：裸 \\n 被切成多行（实得 {wrapped.Count} 行）", wrapped.Count > 1, $"{wrapped.Count}");
    }

    // ⑥ 未知标记与未闭合的 $("：按普通文本保留，绝不静默丢字
    {
        var segs = HexCastingTerraria.Core.Ui.BookTextLayout.Parse("x$(nonsense)y");
        var joined = string.Concat(segs.ConvertAll(s => s.Text).ToArray());
        Check("未知标记原样保留（不丢内容）", joined == "x$(nonsense)y", joined);

        var segs2 = HexCastingTerraria.Core.Ui.BookTextLayout.Parse("a$(b");
        var joined2 = string.Concat(segs2.ConvertAll(s => s.Text).ToArray());
        Check("未闭合的 $(\" 原样保留", joined2 == "a$(b", joined2);
    }

    // ⑦ 样式与链接：$(bold) / $(l:目标) 要真的作用到后面的段上
    {
        var segs = HexCastingTerraria.Core.Ui.BookTextLayout.Parse("$(bold)B$(/l)$(l:hexcasting:items)L");
        bool hasBold = false, hasLink = false;
        foreach (var s in segs)
        {
            if (s.Text == "B" && s.Bold) hasBold = true;
            if (s.Text == "L" && s.LinkTarget == "hexcasting:items") hasLink = true;
        }
        Check("$(bold) 生效", hasBold);
        Check("$(l:目标) 生效", hasLink);
    }

    // ⑧ 分页：每页不超过 N 行，且最后一页不空
    {
        var pages = HexCastingTerraria.Core.Ui.BookTextLayout.Layout(
            "w1 w2 w3 w4 w5 w6 w7 w8 w9", 6, 2, Measure1);
        bool anyOver = false;
        foreach (var pg in pages) { if (pg.Lines.Count > 2) anyOver = true; }
        Check($"分页：每页 ≤2 行（共 {pages.Count} 页）", !anyOver, $"{pages.Count}");
        Check("分页：最后一页不空", pages.Count == 0 || pages[pages.Count - 1].Lines.Count > 0);
    }

    // ⑨ 空输入：不许抛异常、不许产生空页
    {
        var pages = HexCastingTerraria.Core.Ui.BookTextLayout.Layout("", 10, 3, Measure1);
        Check("空文本 → 0 页（不产生空页）", pages.Count == 0, $"{pages.Count}");
    }
}

// ── 书本视图状态机（Core/Ui/BookView.cs）────────────────────────────
//
// 「翻页越界」「返回层级错乱」「退到封面之后还在动」这类毛病在真机上要靠手点才发现，
// 在这里用断言就能钉死。全部入口都返回 bool，所以"到边界不许再动"是可以直接断言的。
{
    static HexCastingTerraria.Core.Ui.BookDocument MakeDoc()
    {
        var doc = new HexCastingTerraria.Core.Ui.BookDocument { Id = "t", TitleKey = "t" };
        var cat = new HexCastingTerraria.Core.Ui.BookCategory
        { Id = "items", NameKey = "c", IconItem = "Focus", DescriptionKey = "d", SortNum = 1 };
        for (int i = 0; i < 2; i++)
        {
            var e = new HexCastingTerraria.Core.Ui.BookEntry
            { Id = "e" + i, CategoryId = "items", NameKey = "n", IconItem = "Focus", SortNum = i };
            int pages = i == 0 ? 3 : 5;
            for (int p = 0; p < pages; p++)
            {
                e.Pages.Add(new HexCastingTerraria.Core.Ui.BookPage { Kind = HexCastingTerraria.Core.Ui.BookPageKind.Text, Text = "p" + p });
            }
            cat.Entries.Add(e);
        }
        doc.Categories.Add(cat);
        doc.RebuildIndex();
        return doc;
    }

    var doc = MakeDoc();
    var v = new HexCastingTerraria.Core.Ui.BookView(doc);

    // ① 封面：Open 才进分类页，重复 Open 不许再动
    Check("初始停在封面", v.Kind == HexCastingTerraria.Core.Ui.BookViewKind.Cover);
    Check("封面 → 分类页", v.Open() && v.Kind == HexCastingTerraria.Core.Ui.BookViewKind.Categories);
    Check("已在分类页时 Open() 返回 false（不重复动作）", !v.Open());

    // ② 不存在的分类 / 条目：不动，且返回 false（不许静默跳到空分类）
    Check("进入不存在的分类 → false 且状态不变",
          !v.EnterCategory("nope") && v.CurrentCategoryId.Length == 0);
    Check("打开不存在的条目 → false", !v.OpenEntry("nope") && v.Kind == HexCastingTerraria.Core.Ui.BookViewKind.Categories);

    // ③ 进条目：分类被自动同步（这样 Back 退回去位置才合理）
    Check("打开条目 → 处于条目层且分类被同步",
          v.OpenEntry("e1") && v.Kind == HexCastingTerraria.Core.Ui.BookViewKind.Entry && v.CurrentCategoryId == "items");
    Check("条目页数 = 5", v.PageCount == 5, $"{v.PageCount}");
    Check("跨页数 = ceil(5/2) = 3", v.SpreadCount == 3, $"{v.SpreadCount}");

    // ④ 翻页边界：到头不许再动
    Check("第一页时 PrevPage() → false（不循环到末尾）", !v.PrevPage() && v.PageIndex == 0);
    v.NextPage(); v.NextPage(); v.NextPage(); v.NextPage();
    Check("翻到最后一页停下（PageIndex = 4）", v.PageIndex == 4, $"{v.PageIndex}");
    Check("最后一页时 NextPage() → false（不越界）", !v.NextPage() && v.PageIndex == 4);

    // ⑤ 双页配对：偶数页在左、奇数页在右，越界时右页为 -1
    Check("PageIndex=4 → 左 4 / 右 -1（右页留白）", v.LeftPageIndex == 4 && v.RightPageIndex == -1,
          $"{v.LeftPageIndex}/{v.RightPageIndex}");
    v.GoToSpread(1);
    Check("GoToSpread(1) → PageIndex=2，左 2 / 右 3", v.PageIndex == 2 && v.LeftPageIndex == 2 && v.RightPageIndex == 3,
          $"{v.PageIndex} {v.LeftPageIndex}/{v.RightPageIndex}");
    Check("GoToSpread 越界 → false", !v.GoToSpread(3) && !v.GoToSpread(-1));

    // ⑥ 连续翻页：最后一页再翻 → 顺延到下一个条目；已是最后一个条目 → false
    v.OpenEntry("e1");
    v.GoToSpread(2);
    Check("e1 最后一页再翻 → 顺延入口 false（它是最后一个条目）", !v.NextPageOrEntry() && v.CurrentEntryId == "e1");
    v.OpenEntry("e0");
    v.GoToSpread(1);
    Check("e0 最后一页再翻 → 跳到 e1 且页归零",
          v.NextPageOrEntry() && v.CurrentEntryId == "e1" && v.PageIndex == 0,
          $"{v.CurrentEntryId} {v.PageIndex}");

    // ⑦ 同分类换条目
    v.OpenEntry("e0");
    Check("NextEntry：e0 → e1", v.NextEntry() && v.CurrentEntryId == "e1");
    Check("NextEntry 到末尾 → false", !v.NextEntry() && v.CurrentEntryId == "e1");
    Check("PrevEntry：e1 → e0", v.PrevEntry() && v.CurrentEntryId == "e0");
    Check("PrevEntry 到开头 → false", !v.PrevEntry() && v.CurrentEntryId == "e0");

    // ⑧ 逐级返回
    v.EnterCategory("items"); v.OpenEntry("e1");
    Check("条目 → 分类页", v.Back() && v.Kind == HexCastingTerraria.Core.Ui.BookViewKind.Categories && v.CurrentEntryId.Length == 0);
    Check("分类页 → 封面", v.Back() && v.Kind == HexCastingTerraria.Core.Ui.BookViewKind.Cover);
    Check("已在封面时 Back() → false（是否关书交给调用方决定）", !v.Back());

    // ⑨ 空文档 / 空分类：不许抛异常，不许返回负数跨页
    {
        var empty = new HexCastingTerraria.Core.Ui.BookDocument();
        var ev = new HexCastingTerraria.Core.Ui.BookView(empty);
        empty.RebuildIndex();
        var ok = ev.Open() && !ev.EnterCategory("x") && !ev.OpenEntry("x")
                 && ev.PageCount == 0 && ev.SpreadCount == 0 && ev.RightPageIndex == -1;
        Check("空文档：各入口都安全且 SpreadCount=0", ok, $"页 {ev.PageCount} 跨页 {ev.SpreadCount}");

        var catOnly = new HexCastingTerraria.Core.Ui.BookDocument();
        catOnly.Categories.Add(new HexCastingTerraria.Core.Ui.BookCategory { Id = "c", SortNum = 1 });
        catOnly.RebuildIndex();
        var cv = new HexCastingTerraria.Core.Ui.BookView(catOnly);
        cv.EnterCategory("c");
        Check("空分类：进得去、退得出、不崩", cv.CurrentCategoryId == "c" && cv.Back());
    }

    // ⑩ 真实内容：把生成出来的 82 个条目走一遍，逐个打开并翻到最后一页
    {
        var real = HexCastingTerraria.Core.Ui.BookContent.Create();
        var rv = new HexCastingTerraria.Core.Ui.BookView(real);
        int opened = 0, bad = 0;
        foreach (var cat in real.Categories)
        {
            foreach (var e in cat.Entries)
            {
                if (!rv.OpenEntry(e.Id)) { bad++; continue; }
                opened++;
                int guard = 0;
                while (rv.NextPage() && guard++ < 500) { }
                if (rv.PageIndex != rv.PageCount - 1 && rv.PageCount > 0) { bad++; }
            }
        }
        Check($"真实内容：{opened} 个条目全部打开并翻到最后一页（异常 {bad} 条）",
              opened == 82 && bad == 0, $"打开 {opened} 异常 {bad}");
        Check("真实内容：7 个分类、82 个条目",
              real.Categories.Count == 7 && real.EntryById.Count == 82,
              $"{real.Categories.Count} / {real.EntryById.Count}");
    }
}

// ── 离屏出图探针（BookRender.cs）─────────────────────────────────────
//
// 只在设了 DRAWTEST_RENDER=1 时跑：日常 run_all 不该产生文件。
// 目的是验证「画布 → PNG」这条管线通了 —— 通了之后才谈得上把两套皮肤画出来看图。
if (Environment.GetEnvironmentVariable("DRAWTEST_RENDER") == "1")
{
    var canvas = new OffscreenCanvas(360, 224, Color32.FromHex("#1E1B24"));

    // ① 皮革封面。上一版**直接从纸开始**，结果纸与背景之间只有一条几乎看不见的深线，
    //    书不像一个物体、像贴在墙上的便签。先画一块皮，纸是**嵌**在里面的。
    canvas.FillRect(new RectF(6, 6, 348, 212), Color32.FromHex("#5A3F26"));
    canvas.StrokeRect(new RectF(6, 6, 348, 212), Color32.FromHex("#33220F"), 2);
    canvas.StrokeRect(new RectF(9, 9, 342, 206), Color32.FromHex("#7A5A3C"), 1);
    canvas.HLine(9, 9, 342, Color32.FromHex("#8A6844"));

    // ② 纸：比封面内缩 10px 形成皮边；内侧两道 1px 暗边当厚度，多了就脏
    var paper = new RectF(16, 16, 328, 192);
    canvas.FillRect(paper, Color32.FromHex("#E8DCC0"));
    canvas.StrokeRect(paper, Color32.FromHex("#C4B08A"), 1);
    canvas.HLine(paper.X + 1, paper.Y + 1, paper.W - 2, Color32.FromHex("#D6C7A4"));
    canvas.FillRect(new RectF(paper.X + 1, paper.Y + 1, 1, paper.H - 2), Color32.FromHex("#D6C7A4"));

    // ③ 页眉带：比正文纸略深 + 一条 1px 规则线，标题区与正文区因此分开。
    //    上一版标题和正文同底色，整页是一个"亮度"，没有焦点。
    canvas.FillRect(new RectF(paper.X + 1, paper.Y + 1, paper.W - 2, 26), Color32.FromHex("#DFD1AF"));
    canvas.HLine(paper.X + 1, paper.Y + 27, paper.W - 2, Color32.FromHex("#C4B08A"));

    // ④ 中缝：1px 深 + 两侧各 1px 亮，两页才读得出是两页
    canvas.FillRect(new RectF(179, paper.Y + 1, 2, paper.H - 2), Color32.FromHex("#B9A683"));
    canvas.FillRect(new RectF(178, paper.Y + 1, 1, paper.H - 2), Color32.FromHex("#F2E9D4"));
    canvas.FillRect(new RectF(181, paper.Y + 1, 1, paper.H - 2), Color32.FromHex("#F2E9D4"));

    canvas.DrawText("The Hex Book", paper.X + 14, paper.Y + 8, Color32.FromHex("#2E2A24"), BookTextAlign.Left, bold: true, scale: 2);

    // ⑤ 正文
    canvas.DrawText("amethyst  charged", paper.X + 14, paper.Y + 40, Color32.FromHex("#2E2A24"));
    canvas.DrawText("It begins with a gem.", paper.X + 14, paper.Y + 56, Color32.FromHex("#6A6152"));

    // ⑥ 选中行：淡紫底 + 左侧 3px 亮条（亮条从 2px 加到 3px —— 2px 在这个尺寸下看不出来）。
    //    下面留一行**未选中**作对照：只画一条选中态时，无从判断对比够不够。
    var sel = new RectF(paper.X + 12, paper.Y + 78, 140, 18);
    canvas.FillRect(sel, Color32.FromHex("#6E5A9E").WithAlpha(58));
    canvas.FillRect(new RectF(sel.X, sel.Y, 3, sel.H), Color32.FromHex("#A98FD9"));
    canvas.DrawText("selected row", sel.X + 10, sel.Y + 6, Color32.FromHex("#2E2A24"));
    canvas.DrawText("another row", sel.X + 10, sel.Y + 24, Color32.FromHex("#3A342B"));

    // ⑦ 分类格：内顶加一道高光当斜面，格子才有厚度；副标题用次级色拉开层级。
    //    ⚠️ 只放 **3** 格：上一版放 4 格，第 4 格底边算到 y=210，而纸的下边界是 208 ——
    //    于是它压过了纸边框、画到了皮革上。这种 2px 的溢出，代码里看不出来，
    //    出图一眼就看出来了 —— 这正是"先出图再看"的意义。
    for (int i = 0; i < 3; i++)
    {
        var cell = new RectF(paper.X + 186, paper.Y + 44 + (i * 40), 124, 34);
        canvas.FillRect(cell, Color32.FromHex("#E3D6B8"));
        canvas.StrokeRect(cell, Color32.FromHex("#C4B08A"), 1);
        canvas.HLine(cell.X + 1, cell.Y + 1, cell.W - 2, Color32.FromHex("#F2E9D4"));
        canvas.FillRect(new RectF(cell.X + 6, cell.Y + 4, 8, 8), Color32.FromHex("#6E5A9E"));
        canvas.DrawText("category " + (i + 1), cell.X + 20, cell.Y + 4, Color32.FromHex("#2E2A24"));
        canvas.DrawText("items", cell.X + 20, cell.Y + 18, Color32.FromHex("#6A6152"));
    }

    // ⑧ 翻页箭头：放在**两页各自的底角**，而不是居中排一排页码点。
    //    上一版把 5 个页码点居中，结果正好横跨中缝 —— 有一个点压在装订线上，
    //    看起来像脏点。原版 Patchouli 也是底角箭头，没有页码点，这样更贴。
    //    （页码信息改用文本放在页脚，不占视觉重量。）
    canvas.FillRect(new RectF(paper.X + 10, paper.Bottom - 16, 6, 6), Color32.FromHex("#B9A683"));
    canvas.FillRect(new RectF(paper.X + 18, paper.Bottom - 16, 6, 6), Color32.FromHex("#B9A683"));
    canvas.FillRect(new RectF(paper.Right - 24, paper.Bottom - 16, 6, 6), Color32.FromHex("#8A7350"));
    canvas.FillRect(new RectF(paper.Right - 16, paper.Bottom - 16, 6, 6), Color32.FromHex("#8A7350"));
    canvas.DrawText("2 / 5", paper.X + 14, paper.Bottom - 34, Color32.FromHex("#6A6152"), BookTextAlign.Left);

    var outPng = Environment.GetEnvironmentVariable("DRAWTEST_RENDER_OUT")
                 ?? @"D:\DeepSeekHarness\tmod\_tools\book_probe.png";
    canvas.SavePng(outPng);
    Console.WriteLine($"离屏出图：{outPng}（{canvas.Width}x{canvas.Height}，绘制调用 {canvas.PrimitiveCount} 次）");

    Check("离屏出图：画布真的收到了绘制调用（>20 次）", canvas.PrimitiveCount > 20, $"{canvas.PrimitiveCount}");
    Check("离屏出图：PNG 文件已写出且非空", File.Exists(outPng) && new FileInfo(outPng).Length > 1000,
          File.Exists(outPng) ? new FileInfo(outPng).Length.ToString() : "缺失");
}

// ── 离屏出图 · 用 Patchouli 原图集 ───────────────────────────────────
//
// 前一个探针是用 FillRect 拼皮革/纸/中缝的，得到的是一坨米色 ——
// 缝线、纸纹、卷边这些**质地调色调不出来**。这里直接用原图集。
// 图集由 _tools/prep_book_atlas.ps1 解成裸 RGBA（离屏环境没有 PNG 解码器）。
if (Environment.GetEnvironmentVariable("DRAWTEST_RENDER") == "1")
{
    var canvas = new OffscreenCanvas(340, 240, Color32.FromHex("#1E1B24"));

    var rawPath = @"D:\DeepSeekHarness\tmod\_tools\book_brown.raw";
    if (File.Exists(@"D:\DeepSeekHarness\tmod\_tools\ui_atlas.txt")) { canvas.Atlas = UiAtlas.Load(@"D:\DeepSeekHarness\tmod\_tools\ui_atlas.txt", @"D:\DeepSeekHarness\tmod\_tools\ui_atlas.raw"); }

    // 真字模（比例字体）。有了它折行/分页才是按真实前进宽度算的。
    var fontTxt = @"D:\DeepSeekHarness\tmod\_tools\book_font.txt";
    var fontRaw = @"D:\DeepSeekHarness\tmod\_tools\book_font.raw";
    if (File.Exists(fontTxt) && File.Exists(fontRaw)) { canvas.Font = BookFont.Load(fontTxt, fontRaw); }

    // 书体切片：坐标是用 _tools/zoom_atlas.ps1 放大叠网格**读**出来的。
    // 上一版取 0,0,290,190 → 右边越过 277 那条线，把描金构件的左边缘切了进来；
    // 收在 276 才是干净的书体。放在 (4,4) 让画布坐标 = 图集坐标，后面排版就不用换算。
    canvas.DrawImage("book", new RectF(4, 4, 272, 181), new RectF(4, 4, 272, 181), new Color32(255, 255, 255));

    // 纸面里排内容。原图集的纸是空白的，文字与格子都要自己排 ——
    // 图集给的是**质感**，排版仍然是我们的事。
    // 纸面（图集坐标 22..262 × 18..170，中缝 145）—— 画布坐标与图集一致，直接用。
    var L = 26f;    // 左页内容起点（纸边 22 + 4 留白）
    var R = 150f;   // 右页内容起点（中缝 145 + 5 留白）
    var T = 24f;    // 顶部内容起点（纸边 18 + 6 留白）

    // 标题在 y=10（字高 19 → 占 10..29）；规则线必须在**标题之下**。
    // ⚠️ 上一版把规则线放在 T-4=20，正好从标题中间穿过去 —— 代码里两个数字都"看着合理"。
    canvas.DrawText("Hex Book", L, 10, Color32.FromHex("#2E2A24"), BookTextAlign.Left, bold: true, scale: 1);
    canvas.HLine(L, 32, 108, Color32.FromHex("#C0A87E"));

    // 行距用**真行高 19**，不是拍脑袋的 16：上一版 16 会让带降部的字母（g/y/p）贴到下一行上。
    // 这就是"字体必须先做"的理由 —— 行高、折行位置都得按真字形来。
    // 正文走**排版引擎**，不是手写死坐标。
    // ⚠️ 上一版把长句直接写死，结果 "and ends with the world." 横穿到中缝以外 ——
    //    正是排版引擎该拦下的东西。测量函数直接接到画布上（用的是字模的真前进宽度）。
    HexCastingTerraria.Core.Ui.MeasureWidth measure = s => canvas.MeasureText(s);
    var leftPages = HexCastingTerraria.Core.Ui.BookTextLayout.Layout(
        "amethyst$(br)It begins with a gem, and ends with the world.",
        114, 12, measure);

    float ty = 38;
    foreach (var pg in leftPages)
    {
        foreach (var ln in pg.Lines)
        {
            canvas.DrawText(ln.PlainText(), L, ty, Color32.FromHex("#5E5647"));
            ty += canvas.Font?.LineHeight ?? 19;   // 真行高，不拍脑袋
        }
    }

    // 选中行：淡紫底 + 左侧亮条（不反白）。高度按真行高给 20，正好包住一行。
    var sel = new RectF(L - 4, 100, 108, 20);
    canvas.FillRect(sel, Color32.FromHex("#6E5A9E").WithAlpha(52));
    canvas.FillRect(new RectF(sel.X, sel.Y, 3, sel.H), Color32.FromHex("#8A73BE"));
    canvas.DrawText("jeweler hammer", sel.X + 8, sel.Y + 1, Color32.FromHex("#2E2A24"));

    // 右页：分类格。宽度 116 → **108**（上一版 150+116=266 越过纸右边界 262）；
    // 高度 32 → **24**：上一版把两行 19px 的字塞进 32 高的格子，第二行 "items"
    // 直接垂到格子外面 —— 出图才看见。改成单行：图标 + 名称 + 右侧条目数。
    for (int i = 0; i < 3; i++)
    {
        var cell = new RectF(R, 38 + (i * 44), 108, 24);
        canvas.FillRect(cell, Color32.FromHex("#EFE4C8").WithAlpha(180));
        canvas.StrokeRect(cell, Color32.FromHex("#C0A87E"), 1);
        canvas.FillRect(new RectF(cell.X + 5, cell.Y + 8, 8, 8), Color32.FromHex("#6E5A9E"));
        canvas.DrawText("category " + (i + 1), cell.X + 18, cell.Y + 3, Color32.FromHex("#2E2A24"));
        canvas.DrawText("18", cell.Right - 22, cell.Y + 3, Color32.FromHex("#5E5647"));
    }

    // 书签飘带与圆点按钮：这次按**读出来**的位置贴。
    // 上一版估的 (140,180,…)/(0,178,…) 是错的 —— 网格图显示底下那两样在 y≥188：
    // 书签 0..135、圆点按钮 135..175，而且**根本没有"底角箭头"这一项**（是我凭印象编的）。
    canvas.DrawImage("bookmark", new RectF(0, 188, 135, 18), new RectF(0, 188, 135, 18), new Color32(255, 255, 255));
    canvas.DrawImage("buttons", new RectF(135, 195, 40, 10), new RectF(135, 195, 40, 10), new Color32(255, 255, 255));

    var outPng = Environment.GetEnvironmentVariable("DRAWTEST_RENDER_OUT2")
                 ?? @"D:\DeepSeekHarness\tmod\_tools\book_real.png";
    canvas.SavePng(outPng);
    Console.WriteLine($"离屏出图（原图集）：{outPng}（绘制调用 {canvas.PrimitiveCount} 次，图集 {(canvas.Atlas is null ? "未加载" : "已加载")}）");

    Check("原图集已加载", canvas.Atlas != null, rawPath);
    Check("字模已加载（比例字体宽度表）", canvas.Font != null, fontTxt);
    Check("字模宽度确实不均（比例而非等宽）",
          canvas.Font != null && canvas.Font.Advance('i') != canvas.Font.Advance('W'),
          canvas.Font is null ? "无字模" : $"i={canvas.Font.Advance('i')} W={canvas.Font.Advance('W')}");
    Check("原图集出图写出成功", File.Exists(outPng) && new FileInfo(outPng).Length > 1000,
          File.Exists(outPng) ? new FileInfo(outPng).Length.ToString() : "缺失");
}

// ── 离屏出图 · 两个真实视图（对应 BookView 的 Categories 与 Entry）────
//
// 上一个探针把「正文页」和「条目列表」挤进同一张图，于是正文第 4 行压到选中行上。
// 那不是引擎的问题 —— 是**两个不同视图被画到了同一页**。BookView 的状态机里本来就是分开的。
// 另外两处也一并修掉：分类格的名称与条目数粘成 "category 118"、以及格高装不下两行。
if (Environment.GetEnvironmentVariable("DRAWTEST_RENDER") == "1")
{
    const string AtlasRaw = @"D:\DeepSeekHarness\tmod\_tools\book_brown.raw";
    const string FontTxt = @"D:\DeepSeekHarness\tmod\_tools\book_font.txt";
    const string FontRaw = @"D:\DeepSeekHarness\tmod\_tools\book_font.raw";

    // 纸面与中缝：图集坐标（由 _tools/zoom_atlas.ps1 放大叠网格读出来的），再乘缩放。
    // ⚠️ 关于 S=2：不是"把书拉大"，而是**还原原版的显示尺度** ——
    //    Patchouli 在 MC 里就是在 GUI scale 2 下画这张图集的，物理上就是 2 倍最近邻放大。
    //    之前的 1 倍让字显得过大：行高/页面高 12.5%，而原版是 7.7%（约 1.6 倍差），
    //    表现就是"7 个分类填满一整页""标题要折两行"。
    //    游戏里字体是固定的 FontAssets.MouseText，改不了字号，所以杠杆只能是书的大小。
    const float S = 2f;
    const float PX0 = 22f * S, PY0 = 18f * S, PX1 = 262f * S, PY1 = 170f * S, SpineX = 145f * S;

    var ink = Color32.FromHex("#2E2A24");
    var ink2 = Color32.FromHex("#5E5647");
    var accent = Color32.FromHex("#6E5A9E");
    var accentLt = Color32.FromHex("#8A73BE");
    var rule = Color32.FromHex("#C0A87E");

    OffscreenCanvas NewBook()
    {
        var c = new OffscreenCanvas((int)(340f * S) + 8, (int)(240f * S) + 8, Color32.FromHex("#1E1B24"));
        if (File.Exists(@"D:\DeepSeekHarness\tmod\_tools\ui_atlas.txt")) { c.Atlas = UiAtlas.Load(@"D:\DeepSeekHarness\tmod\_tools\ui_atlas.txt", @"D:\DeepSeekHarness\tmod\_tools\ui_atlas.raw"); }
        if (File.Exists(FontTxt) && File.Exists(FontRaw)) { c.Font = BookFont.Load(FontTxt, FontRaw); }

        // 书体放在 (4,4) 再整体 S 倍；图集内切 272x181（读出来的书体边界）
        c.DrawImage("book", new RectF(4, 4, 272, 181), new RectF(4, 4, 272 * S, 181 * S), new Color32(255, 255, 255));
        c.DrawImage("bookmark", new RectF(0, 188, 135, 18), new RectF(0, 188 * S, 135 * S, 18 * S), new Color32(255, 255, 255));
        c.DrawImage("buttons", new RectF(135, 195, 40, 10), new RectF(135 * S, 195 * S, 40 * S, 10 * S), new Color32(255, 255, 255));
        return c;
    }

    int LineH(OffscreenCanvas c) => c.Font?.LineHeight ?? 19;

    // ── 视图一：分类页（BookViewKind.Categories）─────────────────────
    //
    // 上一版把 7 个分类排成 2 列 × 4 行铺满整本书，两个问题：
    //   ① 4 行 × 38 = 152，而纸面高度正好只有 152 —— 第 4 行掉到书外去了；
    //   ② 第二列正好压在中缝（145）上，看起来像把两页当一页用。
    // 改成符合"翻书"语义的分工：**左页放分类列表，右页放所选分类的预览**。
    // 也因此去掉大标题 —— 书名在封面上，落地页不需要再占 32px。
    {
        var c = NewBook();
        int lh = LineH(c);
        float pad = 6f * S;
        float lw = (SpineX - 8f * S) - (PX0 + pad);
        float rw = (PX1 - pad) - (SpineX + 8f * S);

        string[] cats = { "basics", "casting", "greatwork", "interop", "items", "lore", "patterns" };
        int[] counts = { 4, 7, 8, 2, 18, 8, 18 };
        int selected = 4;

        // 行高也按 S 缩放，但**字高不缩**（字模是固定 14px）—— 行里的字垂直居中。
        // 7 × 42 = 294 ≤ 纸高 304 ✓
        float rowH = 21f * S;
        for (int i = 0; i < cats.Length; i++)
        {
            float rowY = PY0 + 2f * S + (i * rowH);
            bool sel = i == selected;
            float ty2 = rowY + ((rowH - lh) / 2f);

            // 选中带**贴着文字**（高 = 行高 + 8），不是铺满整行。
            // 上一版用整行高（38px）去衬 19px 的字，紫条厚得像块砖。
            if (sel)
            {
                var band = new RectF(PX0 + 4f * S, ty2 - 4f * S, lw + 4f * S, lh + 8f * S);
                c.FillRect(band, accent.WithAlpha(46));
                c.FillRect(new RectF(band.X, band.Y, 3f * S, band.H), accentLt);
            }

            c.DrawText(cats[i], PX0 + 4f * S + (8f * S), ty2, sel ? ink : ink2);
            // 条目数右对齐，且**离中缝再远一点**：上一版贴在 x=266，紧挨装订线，
            // 出图看着像被切掉了半个字。
            c.DrawText(counts[i].ToString(), SpineX - 26f * S, ty2, ink2, BookTextAlign.Right);
        }

        // 右页：所选分类的预览
        c.DrawText(cats[selected], SpineX + 8f * S, PY0 + 4f * S, ink, BookTextAlign.Left, bold: true);
        c.HLine(SpineX + 8f * S, PY0 + 4f * S + lh + 2f * S, rw, rule);

        HexCastingTerraria.Core.Ui.MeasureWidth m = s => c.MeasureText(s);
        var desc = HexCastingTerraria.Core.Ui.BookTextLayout.Layout(
            "Tools, storage and trinkets. Everything you can hold, and what it is for.",
            (int)rw, 8, m);

        float dy = PY0 + 4f * S + lh + 8f * S;
        foreach (var pg in desc)
        {
            foreach (var ln in pg.Lines)
            {
                c.DrawText(ln.PlainText(), SpineX + 8f * S, dy, ink2);
                dy += lh;
            }
        }

        var p1 = @"D:\DeepSeekHarness\tmod\_tools\book_view_categories.png";
        c.SavePng(p1);
        Console.WriteLine($"视图·分类页 -> {p1}（{c.Width}x{c.Height}，绘制 {c.PrimitiveCount} 次，行高 {lh}）");
        Check("分类页视图出图成功", File.Exists(p1) && new FileInfo(p1).Length > 1000,
              File.Exists(p1) ? new FileInfo(p1).Length.ToString() : "缺失");
    }

    // ── 视图二：条目双页（BookViewKind.Entry）────────────────────────
    //
    // 上一版写死了「画 7 行」，结果 7 × 19 = 133，从 y=47 起就**溢出纸底**（170），
    // 最后一行的 "amethyst dust" 压住了页码；标题也没折行，'Jeweler's Hammer'
    // 一直画到中缝外面。两处都改成由**排版引擎按纸面高度算**，不写死。
    {
        var c = NewBook();
        int lh = LineH(c);
        float pad = 6f * S;

        float lw = (SpineX - 8f * S) - (PX0 + pad);
        float rw = (PX1 - pad) - (SpineX + 8f * S);
        HexCastingTerraria.Core.Ui.MeasureWidth m = s => c.MeasureText(s);

        // 标题也走排版：太长就自己折行，不许横穿中缝
        float ty = PY0 + 2f * S;
        var titlePg = HexCastingTerraria.Core.Ui.BookTextLayout.Layout("Jeweler's Hammer", (int)lw, 2, m);
        foreach (var ln in titlePg[0].Lines)
        {
            c.DrawText(ln.PlainText(), PX0 + pad, ty, ink, BookTextAlign.Left, bold: true);
            ty += lh;
        }

        ty += 2f * S;
        c.HLine(PX0 + pad, ty, lw, rule);
        ty += 6f * S;

        // 每页能放几行 —— **算出来**，不是写死
        int maxLines = (int)((PY1 - ty) / lh);
        if (maxLines < 1) { maxLines = 1; }

        var body = HexCastingTerraria.Core.Ui.BookTextLayout.Layout(
            "A tool for cracking geodes. Hold it and strike a cluster to knock loose " +
            "amethyst dust, the rawstuff of media. Every hex begins with a gem.",
            (int)lw, maxLines, m);

        int drawn = 0;
        foreach (var ln in body[0].Lines)
        {
            c.DrawText(ln.PlainText(), PX0 + pad, ty, ink2);
            ty += lh;
            drawn++;
        }
        Check($"正文行数不超过算出来的容量（{drawn} ≤ {maxLines}）", drawn <= maxLines, $"{drawn}/{maxLines}");
        Check("正文底边没有越过纸面下边界", ty <= PY1 + lh, $"ty={ty:0} 纸底={PY1}");
        Check($"正文分了 {body.Count} 页（一页装不下自动分页）", body.Count >= 1, $"{body.Count}");

        // 右页：配方框（示意）
        var box = new RectF(SpineX + 8f * S, PY0 + 10f * S, rw, 52f * S);
        c.FillRect(box, Color32.FromHex("#EFE4C8").WithAlpha(150));
        c.StrokeRect(box, rule, 1);
        c.FillRect(new RectF(box.X + 8f * S, box.Y + 12f * S, 16f * S, 16f * S), accent);
        c.DrawText("crafting", box.X + 32f * S, box.Y + 8f * S, ink);
        c.DrawText("iron bar x1", box.X + 32f * S, box.Y + 30f * S, ink2);

        // 页脚：页码在左页底，翻页箭头在右页底角
        c.DrawText("1 / " + body.Count, PX0 + pad, PY1 - 16f * S, ink2);
        c.FillRect(new RectF(PX1 - 30f * S, PY1 - 16f * S, 7f * S, 7f * S), rule);
        c.FillRect(new RectF(PX1 - 20f * S, PY1 - 16f * S, 7f * S, 7f * S), Color32.FromHex("#8A7350"));

        var p2 = @"D:\DeepSeekHarness\tmod\_tools\book_view_entry.png";
        c.SavePng(p2);
        Console.WriteLine($"视图·条目双页 -> {p2}（{c.Width}x{c.Height}，绘制 {c.PrimitiveCount} 次，正文 {drawn} 行 / {body.Count} 页）");
        Check("条目双页视图出图成功", File.Exists(p2) && new FileInfo(p2).Length > 1000,
              File.Exists(p2) ? new FileInfo(p2).Length.ToString() : "缺失");
        Check("排版引擎在真字宽下确实折了行（>1 行）", drawn > 1, $"{drawn} 行");
    }
}

// ── 离屏出图 · 走**真正的皮肤**（Core/Ui/PatchouliSkin.cs）────────────
//
// 上面两个视图是手搓几何的"已定稿参考"。这里换成**真实的 BookView + BookSkin** 走一遍 ——
// 意义在于验证「同一份皮肤代码同时驱动游戏内渲染与离屏出图」这条架构假设成立：
// 皮肤只依赖 IBookCanvas（无 XNA），所以能放 Core/，也就能被本工程编译。
// 如果这里渲出来的和参考图一致，就说明进游戏后的几何与配色和"我看过的图"是同一套。
if (Environment.GetEnvironmentVariable("DRAWTEST_RENDER") == "1")
{
    // 这几个路径在上一个 if 块里是局部 const，作用域不跨块 —— 这里重新声明。
    const string AtlasRaw = @"D:\DeepSeekHarness\tmod\_tools\book_brown.raw";
    const string FontTxt = @"D:\DeepSeekHarness\tmod\_tools\book_font.txt";
    const string FontRaw = @"D:\DeepSeekHarness\tmod\_tools\book_font.raw";

    // 合成一份带文字的内容 —— 生成的内容骨架里正文是空的（源项目只有本地化键），
    // 所以这里自己填，用于验证皮肤的画法。真机上的正文以后补。
    var doc = new HexCastingTerraria.Core.Ui.BookDocument { Id = "hexbook" };
    doc.DisplayTitle = "The Hex Book";
    string[] catIds = { "basics", "casting", "greatwork", "interop", "items", "lore", "patterns" };
    string[] catNames = { "Basics", "Casting", "The Great Work", "Interop", "Items", "Lore", "Patterns" };
    string[] catDesc =
    {
        "How to hold a staff and what happens when you do.",
        "Patterns, stacks and the shape of a hex.",
        "The long road, and what waits at the end of it.",
        "Talking to other mods, politely.",
        "Tools, storage and trinkets. Everything you can hold, and what it is for.",
        "Fragments of a story someone left behind.",
        "Every glyph, catalogued.",
    };

    for (int i = 0; i < catIds.Length; i++)
    {
        var cat = new HexCastingTerraria.Core.Ui.BookCategory
        {
            Id = catIds[i], NameKey = "k", DisplayName = catNames[i],
            DescriptionKey = "k", DisplayDescription = catDesc[i], SortNum = i,
        };
        doc.Categories.Add(cat);
    }

    var entry = new HexCastingTerraria.Core.Ui.BookEntry
    {
        Id = "jeweler_hammer", CategoryId = "items", NameKey = "k",
        DisplayName = "Jeweler's Hammer", IconItem = "JewelerHammer", SortNum = 0,
    };
    entry.Pages.Add(new HexCastingTerraria.Core.Ui.BookPage
    {
        Kind = HexCastingTerraria.Core.Ui.BookPageKind.Text,
        Text = "A tool for cracking geodes. Hold it and strike a cluster to knock loose " +
               "amethyst dust, the rawstuff of media. Every hex begins with a gem.",
    });
    entry.Pages.Add(new HexCastingTerraria.Core.Ui.BookPage
    {
        Kind = HexCastingTerraria.Core.Ui.BookPageKind.Crafting, RecipeItem = "iron bar x1",
    });
    doc.Categories[4].Entries.Add(entry);
    doc.RebuildIndex();

    var view = new HexCastingTerraria.Core.Ui.BookView(doc);
    var skin = new HexCastingTerraria.Core.Ui.PatchouliSkin { Scale = 2f };

    OffscreenCanvas SkinCanvas()
    {
        var c = new OffscreenCanvas(640, 440, Color32.FromHex("#1E1B24"));
        if (File.Exists(@"D:\DeepSeekHarness\tmod\_tools\ui_atlas.txt")) { c.Atlas = UiAtlas.Load(@"D:\DeepSeekHarness\tmod\_tools\ui_atlas.txt", @"D:\DeepSeekHarness\tmod\_tools\ui_atlas.raw"); }
        if (File.Exists(FontTxt) && File.Exists(FontRaw)) { c.Font = BookFont.Load(FontTxt, FontRaw); }
        return c;
    }

    var vp = new RectF(0, 0, 640, 440);

    // 分类页
    {
        view.Reset();
        view.Open();
        view.EnterCategory("items");
        var c = SkinCanvas();
        var m = skin.Measure(view, vp);
        skin.Draw(c, view, m);
        var p = @"D:\DeepSeekHarness\tmod\_tools\book_skin_categories.png";
        c.SavePng(p);
        Console.WriteLine($"皮肤·分类页 -> {p}（书 {m.Book.W:0}x{m.Book.H:0}，左页 {m.LeftPage.W:0}x{m.LeftPage.H:0}）");
        Check("皮肤渲染·分类页出图成功", File.Exists(p) && new FileInfo(p).Length > 1000);
        Check("皮肤几何：书体落在视口内",
              m.Book.X >= 0 && m.Book.Y >= 0 && m.Book.Right <= vp.Right && m.Book.Bottom <= vp.Bottom,
              m.Book.ToString());
        Check("皮肤几何：左页右边界不越过中缝",
              m.LeftPage.Right <= m.Book.X + (145f * skin.Scale), $"{m.LeftPage.Right:0}");
    }

    // 条目页
    {
        view.OpenEntry("jeweler_hammer");
        var c = SkinCanvas();
        var m = skin.Measure(view, vp);
        skin.Draw(c, view, m);
        var p = @"D:\DeepSeekHarness\tmod\_tools\book_skin_entry.png";
        c.SavePng(p);
        Console.WriteLine($"皮肤·条目页 -> {p}（跨页 {view.SpreadNumber}/{view.SpreadCount}）");
        Check("皮肤渲染·条目页出图成功", File.Exists(p) && new FileInfo(p).Length > 1000);
        Check("皮肤几何：右页在左页右侧且不越出书体",
              m.RightPage.X > m.LeftPage.Right && m.RightPage.Right <= m.Book.Right,
              $"{m.RightPage}");
    }

    // ── 另一套皮肤：泰拉原版观感（VanillaSkin）───────────────────────
    // 同一份内容 / 同一个状态机 / 同一份契约，只换几何与配色 —— 这是"两套皮肤"能同时维护的前提。
    {
        var vskin = new HexCastingTerraria.Core.Ui.VanillaSkin { Scale = 1f };

        view.Reset();
        view.Open();
        view.EnterCategory("items");
        var c1 = SkinCanvas();
        var m1 = vskin.Measure(view, vp);
        vskin.Draw(c1, view, m1);
        var q1 = @"D:\DeepSeekHarness\tmod\_tools\book_vanilla_categories.png";
        c1.SavePng(q1);
        Console.WriteLine($"原版皮肤·目录 -> {q1}（窗口 {m1.Book.W:0}x{m1.Book.H:0}）");
        Check("VanillaSkin·目录出图成功", File.Exists(q1) && new FileInfo(q1).Length > 1000);
        Check("VanillaSkin 几何：左右分栏不重叠",
              m1.LeftPage.Right <= m1.RightPage.X + 0.01f, $"{m1.LeftPage.Right:0} / {m1.RightPage.X:0}");

        view.OpenEntry("jeweler_hammer");
        var c2 = SkinCanvas();
        var m2 = vskin.Measure(view, vp);
        vskin.Draw(c2, view, m2);
        var q2 = @"D:\DeepSeekHarness\tmod\_tools\book_vanilla_entry.png";
        c2.SavePng(q2);
        Console.WriteLine($"原版皮肤·条目 -> {q2}");
        Check("VanillaSkin·条目出图成功", File.Exists(q2) && new FileInfo(q2).Length > 1000);

        // 两套皮肤的关键差异必须真的存在（否则"双 UI"就是摆设）
        var pskin = new HexCastingTerraria.Core.Ui.PatchouliSkin { Scale = 2f };
        var mp = pskin.Measure(view, vp);
        Check("两套皮肤的几何确实不同（书 vs 矩形窗口）",
              System.Math.Abs(mp.Book.W - m2.Book.W) > 1f || System.Math.Abs(mp.Book.H - m2.Book.H) > 1f,
              $"书 {mp.Book.W:0}x{mp.Book.H:0} / 窗口 {m2.Book.W:0}x{m2.Book.H:0}");
    }
}

// ── 列表几何（Core/Ui/ListLayout.cs）────────────────────────────────
//
// 换到 tModLoader 的 UIElement 之后离线验证闭环断了（元素树编译不了），
// 我因此连着出了三个"编译通过、实机是空的/不动"的 bug。
// 但**列表几何本身是纯数学** —— 抽到 Core 就重新可测了，
// 而"列表溢出""滚动偏移错"恰恰是几何问题。
{
    const int Count = 19;      // BookUiState 里真实的分类数
    const float RowH = 24f;
    const float ViewH = 346f;

    float max = HexCastingTerraria.Core.Ui.ListLayout.MaxScroll(Count, RowH, ViewH);
    Check($"19 行 × 24 在 346 高的视口里要能滚（最大滚动 = {max:0}）", max > 0f, $"{max}");
    Check("内容装得下时最大滚动为 0（不会'能滚却没东西'）",
          HexCastingTerraria.Core.Ui.ListLayout.MaxScroll(3, RowH, ViewH) == 0f);

    Check("滚动位置夹到 [0, max]",
          HexCastingTerraria.Core.Ui.ListLayout.ClampOffset(-50f, Count, RowH, ViewH) == 0f
          && HexCastingTerraria.Core.Ui.ListLayout.ClampOffset(9999f, Count, RowH, ViewH) == max);

    float top = HexCastingTerraria.Core.Ui.ListLayout.RowTop(Count - 1, RowH, max);
    Check($"滚到底时最后一行完整可见（top={top:0}，底={top + RowH:0} ≤ {ViewH}）",
          top + RowH <= ViewH + 0.01f, $"{top:0}");

    bool overlap = false;
    for (int i = 0; i + 1 < Count; i++)
    {
        float a = HexCastingTerraria.Core.Ui.ListLayout.RowTop(i, RowH, 0f);
        float b = HexCastingTerraria.Core.Ui.ListLayout.RowTop(i + 1, RowH, 0f);
        if (b < a + RowH - 0.01f) { overlap = true; }
    }
    Check("相邻行不重叠", !overlap);

    var (first, last) = HexCastingTerraria.Core.Ui.ListLayout.VisibleRange(Count, RowH, ViewH, max);
    bool consistent = true;
    for (int i = 0; i < Count; i++)
    {
        bool byRange = i >= first && i < last;
        bool byTest = HexCastingTerraria.Core.Ui.ListLayout.RowVisible(i, Count, RowH, ViewH, max);
        if (byRange && !byTest) { consistent = false; }
    }
    Check($"可见区间 [{first}, {last}) 里的行都真的可见", consistent, $"{first}..{last}");
    Check("可见区间不越界", first >= 0 && last <= Count && first <= last, $"{first}..{last}");
}
Console.WriteLine($"\n================ 通过 {passed} / 失败 {failed} ================");

// ── 把实测数字吐成机器可读文件 ──────────────────────────────────────
//
// 这是「文档↔代码一致性断言」的输入端：_tools\check_arch.ps1 会拿这份数据
// 去对拍 STATUS.generated.md 里写的数字。文档再写「184/188」这种话，
// 脚本就去数一遍 —— 约束从「文档里的规矩」变成「会失败的测试」。
//
// 为什么由本工程产出而不是让 check_arch 自己数源码：
// 「已注册行为数」在源码里是散在十几个文件 + 若干循环/辅助函数里注册的，
// 纯文本统计一定数错（MathActions 用 "hexcasting:" + op、PotionActions 用辅助函数）。
// 只有真正跑过 HexActions.RegisterAll() 的进程才知道准数。
{
    var measured = new System.Text.StringBuilder();
    measured.AppendLine("{");
    measured.AppendLine($"  \"patterns\": {PatternRegistry.Count},");
    measured.AppendLine($"  \"actions\": {PatternRegistry.RegisteredActionCount},");

    int notApplicable = 0;
    var naKeys = new HashSet<string>(
        HexCastingTerraria.Core.Casting.Actions.NotApplicablePatterns.ZAxisVectors.Concat(
        HexCastingTerraria.Core.Casting.Actions.NotApplicablePatterns.PehkuiInterop));
    foreach (var def in PatternRegistry.All)
    {
        if (naKeys.Contains(def.Id)) notApplicable++;
    }
    measured.AppendLine($"  \"notApplicable\": {notApplicable},");
    measured.AppendLine($"  \"unimplemented\": {PatternRegistry.Count - PatternRegistry.RegisteredActionCount - notApplicable},");
    // 已标注类型契约的图案数 —— check_arch 会拿它做**棘轮**：只许多、不许少。
    // 少了就说明有人把标注删了，类型检查会静默退化成"什么都查不出"。
    int typed = 0;
    foreach (var def in PatternRegistry.All)
    {
        if (PatternRegistry.TryGetAction(def, out var act) && act != null && act.Types.IsKnown) typed++;
    }
    measured.AppendLine($"  \"typedActions\": {typed},");
    measured.AppendLine($"  \"checksPassed\": {passed},");
    measured.AppendLine($"  \"checksFailed\": {failed},");
    measured.AppendLine($"  \"generatedAtUtc\": \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\"");
    measured.AppendLine("}");

    var outPath = Environment.GetEnvironmentVariable("DRAWTEST_MEASURED")
                  ?? @"D:\DeepSeekHarness\tmod\_tools\measured.json";
    try
    {
        File.WriteAllText(outPath, measured.ToString(), new System.Text.UTF8Encoding(false));
        Console.WriteLine($"实测数据已写出：{outPath}");
    }
    catch (Exception ex)
    {
        // 写不出不算测试失败（可能只是目录只读），但要吭声
        Console.WriteLine($"[警告] 实测数据写出失败：{ex.GetType().Name} {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;
