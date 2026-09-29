// 画布拖拽的端到端验证：
//   对注册表里的**每一个**图案，沿着它自己的折线拖一遍鼠标，
//   用真实的 HexPattern 状态机算最终签名，再看能不能匹配回同一条图案。
//
// 覆盖的是「几何 + 匹配」这条链路（注册表数据 → Positions() → 像素吸附 →
// TryAppendDir → AnglesSignature → Match）。输入层（鼠标事件、帧率）不在这里，
// 但玩家画不出来的图形，这里一定能先抓出来。
using HexCastingTerraria.Core.Canvas;
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


/// <summary>
/// 用**模组里的真状态机** <see cref="PatternDrawer"/> 模拟一次拖拽（每个采样点 = 一个鼠标事件，同原版）。
/// 以前这里是「照抄 HexCanvas.DrawMove」的副本，真代码改坏了这里照样全绿。
/// 起点取 path[0]，网格原点在像素 (0,0)。
/// </summary>
static string SimulateDrag(List<(float X, float Y)> path, float size, float threshold,
                           List<string>? trace = null)
{
    var d = new PatternDrawer { SnapThreshold = threshold };
    d.Begin(new Vec2f(path[0].X, path[0].Y), size, Vec2f.Zero);
    for (int i = 1; i < path.Count; i++)
    {
        var r = d.Move(new Vec2f(path[i].X, path[i].Y), size, Vec2f.Zero);
        if (r != MoveResult.None) trace?.Add($"{r} → {d.Wip?.AnglesSignature() ?? "(起点)"}");
    }
    return d.Wip?.AnglesSignature() ?? "";
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

CanvasFeelTests.Run(size, Check);

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
        // Patchouli：$(br) = 换行；$(br2) 与 $(p) = 段落（换行 + 空一行）→ 1 + 2 + 2
        Check($"Parse：br=1、br2=2、p=2 → 共 5 个 \\n 段（实得 {breaks}）", breaks == 5, $"{breaks}");
        Check("Parse 后文本没被吞掉（裸 \\n 仍留在文本里）",
              string.Concat(segs.ConvertAll(s => s.Text).ToArray()) == "a\nb\n\nc\n\nd\ne",
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

BookTests.Run(Check);
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
        HexCastingTerraria.Core.Casting.Actions.NotApplicablePatterns.PehkuiInterop));   // ZAxisVectors 已清空
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
