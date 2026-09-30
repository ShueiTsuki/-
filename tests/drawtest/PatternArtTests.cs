using HexCastingTerraria.Core.Canvas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

/// <summary>
/// 静态图案（卷轴、石板、提示框）按原版 PatternRenderer / HexPatternPoints 的摆放：
/// 画在给定方格里、留出边距、起笔点在第一个格点上、会动的样式才会动。
/// </summary>
static class PatternArtTests
{
    public static void Run(CheckFn check)
    {
        Console.WriteLine("\n=== ⑧ 静态图案（原版 PatternRenderer）===");
        PatternRegistry.EnsureLoaded();
        const float size = 96f;
        int outside = 0, empty = 0, checkedCount = 0;
        foreach (var def in PatternRegistry.All)
        {
            var verts = new List<ColoredVertex>();
            StaticPatternArt.Render(verts, def.Prototype, PatternStyle.Readable, PatternPalette.Default.WithDots(true, true),
                0, 0, new Vec2f(0, 0), size);
            checkedCount++;
            if (verts.Count == 0) { empty++; continue; }
            // 允许线宽 / 点半径的一点点溢出（原版也只是「尽量」把线宽算进去）
            foreach (var v in verts)
            {
                if (v.X < -2 || v.Y < -2 || v.X > size + 2 || v.Y > size + 2) { outside++; break; }
            }
        }
        check($"全部 {checkedCount} 个图案都画得出来、而且都在方格里（边距 2/16）", empty == 0 && outside == 0,
            $"空 {empty}、出界 {outside}");

        // 起笔点：第一个格点，映射后应落在方格里、且离边至少 2/16 − 线宽
        var p = PatternRegistry.All.First(d => d.Angles.Length >= 3).Prototype;
        var bare = StaticPatternArt.BarePoints(p);
        var dups = PatternGeometry.FindDupIndices(p.Positions());
        var zappy = PatternGeometry.MakeZappy(bare, dups, 10, 0.5f, 0f, 0.2f, 0.2f, 0.8f, 0, 0);
        var layout = StaticPatternArt.Place(zappy, PatternStyle.Readable);
        var start = layout.Map(bare[0]);
        check("起笔点在方格内部（留了边距）", start.X >= 0.07f && start.X <= 0.93f && start.Y >= 0.07f && start.Y <= 0.93f,
            $"({start.X:0.00}, {start.Y:0.00})");

        // 静态样式与时间无关；抖动样式（法术环走到的石板）随时间变
        var a = new List<ColoredVertex>();
        var b = new List<ColoredVertex>();
        StaticPatternArt.Render(a, p, PatternStyle.Worldly, PatternPalette.Default, 7, 0, new Vec2f(0, 0), 16);
        StaticPatternArt.Render(b, p, PatternStyle.Worldly, PatternPalette.Default, 7, 123, new Vec2f(0, 0), 16);
        bool staticSame = a.Count == b.Count && a.Zip(b).All(t => t.First.X == t.Second.X && t.First.Y == t.Second.Y);
        a.Clear(); b.Clear();
        StaticPatternArt.Render(a, p, PatternStyle.Wobbly, PatternPalette.SlatePurple, 7, 0, new Vec2f(0, 0), 16);
        StaticPatternArt.Render(b, p, PatternStyle.Wobbly, PatternPalette.SlatePurple, 7, 123, new Vec2f(0, 0), 16);
        bool wobblyMoves = a.Count != b.Count || a.Zip(b).Any(t => t.First.X != t.Second.X || t.First.Y != t.Second.Y);
        check("WORLDLY 静止、WOBBLY 随时间抖动", staticSame && wobblyMoves, $"{staticSame} {wobblyMoves}");

        // DRAWTEST_RENDER=1：把几个图案的三角形写成 JSON，_tools/render_pattern_preview.py 画成 PNG 看图
        if (Environment.GetEnvironmentVariable("DRAWTEST_RENDER") == "1")
        {
            var sbj = new System.Text.StringBuilder("[");
            string[] ids = { "hexcasting:add", "hexcasting:get_caster", "hexcasting:raycast", "hexcasting:teleport/great", "hexcasting:eval", "hexcasting:craft/cypher" };
            for (int k = 0; k < ids.Length; k++)
            {
                var d = PatternRegistry.FindById(ids[k]);
                if (d is null) continue;
                var verts = new List<ColoredVertex>();
                var (st, pal) = (k % 3) switch
                {
                    0 => (PatternStyle.Readable, PatternPalette.Default.WithDots(true, true)),
                    1 => (PatternStyle.Worldly, PatternPalette.Default),
                    _ => (PatternStyle.Wobbly, PatternPalette.SlatePurple),
                };
                StaticPatternArt.Render(verts, d.Prototype, st, pal, k, 40, new Vec2f(k * 130 + 10, 10), 120);
                foreach (var v in verts) sbj.Append($"[{v.X:0.##},{v.Y:0.##},{v.Argb}],");
            }
            if (sbj[^1] == ',') sbj.Length--;
            sbj.Append(']');
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "pattern_art.json"), sbj.ToString());
        }

        // 原版配色：外线浅 0xd2c8c8、内线深 0x554d54；石板充能紫 = glowy(0xcfa0f3)
        check("原版配色（DEFAULT_PATTERN_COLOR / SLATE_WOBBLY_PURPLE_COLOR）",
            PatternPalette.Default.OuterStart == 0xff_d2c8c8 && PatternPalette.Default.InnerStart == 0xff_554d54
            && PatternPalette.SlatePurple.OuterStart == 0xff_cfa0f3 && PatternPalette.SlatePurple.InnerStart == 0xff_e7cff9);
    }
}
