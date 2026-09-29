using System.Globalization;
using System.Text;
using HexCastingTerraria.Core.Registry;
using HexCastingTerraria.Core.Ui;

/// <summary>
/// 咒法学之书：内容完整性、导航状态机、以及**把每一个界面都真的渲染一遍**。
///
/// 渲染用 <see cref="RecordingCanvas"/>：只记录绘制命令（不需要字体与贴图），
/// 设了 DRAWTEST_RENDER=1 时把几个代表性界面写成 JSON，由 _tools/render_book_preview.py 画成 PNG 看图。
/// 文字宽度用近似度量（中文 ≈ 0.78 行高，西文 ≈ 0.42 行高），和泰拉的 MouseText 字体接近 ——
/// 游戏里用的是真字体，所以这里的「溢出 = 0」是必要条件，不是充分条件。
/// </summary>
static class BookTests
{
    public static void Run(CheckFn check)
    {
        Console.WriteLine("\n=== ⑦ 咒法学之书（帕秋莉手册）===");
        var doc = BookContent.Create();
        Content(doc, check);
        Navigation(doc, check);
        RenderAll(doc, check);
        Unlocks(doc, check);
    }

    static void Unlocks(BookDocument doc, CheckFn check)
    {
        var entries = doc.Categories.SelectMany(c => c.Entries).ToList();
        var everything = new BookProgress { Amethyst = true, FailedGreatSpell = true, Overcasted = true, Enlightened = true };
        foreach (var id in BookUnlocks.LoreIds) { everything.FoundLore.Add(id); }
        var stuck = entries.Where(e => !BookUnlocks.IsUnlocked(e.Advancement, everything)).Select(e => $"{e.Id}({e.Advancement})").ToList();
        Check(check, "每个条目的解锁条件在泰拉侧都有对应（全部达成 → 全部解锁）", stuck.Count == 0, string.Join(",", stuck.Take(5)));

        var none = new BookProgress();
        int openAtStart = entries.Count(e => BookUnlocks.IsUnlocked(e.Advancement, none));
        Check(check, $"新角色：只有无条件的条目可读（{openAtStart} 条）", openAtStart == entries.Count(e => e.Advancement.Length == 0) && openAtStart >= 1);

        var amethyst = new BookProgress { Amethyst = true };
        int root = entries.Count(e => BookUnlocks.IsUnlocked(e.Advancement, amethyst));
        Check(check, $"拿到紫水晶：开放 root 条目（{root} 条），大法术 / 传说残页仍锁着",
            root == entries.Count(e => e.Advancement is "" or "hexcasting:root")
            && !BookUnlocks.IsUnlocked("hexcasting:enlightenment", amethyst) && !BookUnlocks.IsUnlocked("hexcasting:lore/cardamom1", amethyst));

        var eye = new BookProgress { Amethyst = true };
        eye.FoundLore.Add("hexcasting:lore/experiment1");
        Check(check, "传说篇章按读过的残卷解锁（原版：残卷随机给一篇，不按顺序）",
            BookUnlocks.IsUnlocked("hexcasting:lore/experiment1", eye) && !BookUnlocks.IsUnlocked("hexcasting:lore/cardamom1", eye));
        var rng = new System.Random(7);
        var got = new HashSet<string>();
        for (int i = 0; i < 8; i++) { got.Add(BookUnlocks.PickUnfoundLore(got, rng)!); }
        Check(check, "故事残卷：读 8 次恰好集齐 8 篇，第 9 次提示已找齐（null）",
            got.Count == 8 && BookUnlocks.PickUnfoundLore(got, rng) is null);
        Check(check, "未知的进度条件默认锁着，开发者全部解锁时打开",
            !BookUnlocks.IsUnlocked("hexcasting:creative_unlocker", everything)
            && BookUnlocks.IsUnlocked("hexcasting:creative_unlocker", new BookProgress { UnlockAll = true }));

        // 渲染：锁住的分类 / 条目画锁、不可点；链接到锁住条目不可点
        var data = new FakeData { Progress = amethyst };
        var r = new PatchouliRenderer(data);
        var canvas = new RecordingCanvas();
        var v = new BookView(doc) { EntryUnlocked = r.IsUnlocked };
        v.Open();
        var landing = r.Render(canvas, v, 1920, 1080, -1, -1);
        var lockedCats = v.TopCategories().Where(c => !r.IsUnlocked(v, c)).Select(c => c.Id).ToList();
        Check(check, $"落地页：锁住的分类（{string.Join(",", lockedCats)}）没有可点的格子",
            lockedCats.Count > 0 && lockedCats.All(id => !landing.Hits.Any(h => h.Kind == BookActionKind.OpenCategory && h.Arg == id))
            && landing.Hits.Count(h => h.Kind == BookActionKind.OpenCategory) == v.TopCategories().Count - lockedCats.Count);

        var great = entries.First(e => e.Advancement == "hexcasting:enlightenment");
        var cat = doc.Categories.First(c => c.Entries.Contains(great));
        v.OpenCategory(cat.Id);
        bool sawLocked = false, lockedHit = false;
        for (int s = 0; s < v.SpreadCount; s++, v.NextSpread())
        {
            canvas.Ops.Clear();
            var f = r.Render(canvas, v, 1920, 1080, -1, -1);
            sawLocked |= canvas.Ops.Any(o => o.Contains("锁定"));
            lockedHit |= f.Hits.Any(h => h.Kind == BookActionKind.OpenEntry && h.Arg == great.Id);
        }
        Check(check, $"分类页：未解锁条目（{great.Id}）显示「锁定」且点不开", sawLocked && !lockedHit);
        Check(check, "BookView：直接打开未解锁条目被拒绝", !v.OpenEntry(great.Id));
    }

    static void Content(BookDocument doc, CheckFn check)
    {
        var view = new BookView(doc);
        var top = view.TopCategories();
        Check(check, $"落地页 {top.Count} 个分类（原版 7 个，去掉只在装了其他 MC 模组时出现的 interop）",
            top.Count == 6, string.Join(",", top.Select(c => c.Id)));
        Check(check, "分类顺序与原版 sortnum 一致", string.Join(",", top.Select(c => c.Id)) == "basics,casting,items,greatwork,lore,patterns",
            string.Join(",", top.Select(c => c.Id)));

        var entries = doc.Categories.SelectMany(c => c.Entries).ToList();
        Check(check, $"条目数 {entries.Count}（原版 82，去掉 2 个 interop 与 1 个 secret）", entries.Count == 79);

        static bool IsChinese(string s) => s.Any(ch => ch >= 0x4E00 && ch <= 0x9FFF);
        var notZh = entries.Where(e => !IsChinese(e.DisplayName)).Select(e => e.Id).ToList();
        Check(check, "所有条目名都是中文（官方简体中文）", notZh.Count == 0, string.Join(",", notZh.Take(5)));

        var emptyText = entries.SelectMany(e => e.Pages.Select((p, i) => (e, p, i)))
            .Where(t => t.p.Kind == BookPageKind.Text && t.p.Text.Length == 0).Select(t => $"{t.e.Id}#{t.i}").ToList();
        Check(check, "正文页都有正文（旧版这里全是空的）", emptyText.Count == 0, string.Join(",", emptyText.Take(5)));

        // 图案页：要么页面上直接写了图案，要么 op_id 能在注册表里查到
        var ids = new HashSet<string>(PatternRegistry.All.Select(d => d.Id));
        var unresolved = entries.SelectMany(e => e.Pages).Where(p => p.Kind == BookPageKind.Pattern)
            .Where(p => p.Patterns.Count == 0 && !ids.Contains(p.PatternId)).Select(p => p.PatternId).Distinct().ToList();
        Check(check, $"图案页都画得出图案（查不到的 {unresolved.Count} 个）", unresolved.Count == 0, string.Join(",", unresolved.Take(8)));

        var links = entries.SelectMany(e => e.Pages).SelectMany(p => BookTextLayout.Parse(p.Text))
            .Select(s => s.LinkTarget).Where(t => t.Length > 0 && !t.StartsWith("http")).Distinct().ToList();
        var dead = links.Where(t =>
        {
            string id = t.Split('#')[0];
            return doc.FindEntry(id) is null && doc.FindCategory(id) is null;
        }).ToList();
        Check(check, $"正文里的 {links.Count} 个站内链接都指向存在的条目/分类", dead.Count == 0, string.Join(",", dead.Take(8)));
    }

    static void Navigation(BookDocument doc, CheckFn check)
    {
        var v = new BookView(doc);
        v.Open();
        Check(check, "打开 → 落地页，没有返回历史时 Back() = false（交给调用方合书）",
            v.Kind == BookViewKind.Landing && !v.Back());
        Check(check, "进分类", v.OpenCategory("items") && v.Kind == BookViewKind.Category && v.Spread == 0);
        int items = doc.FindCategory("items")!.Entries.Count;
        int expectSpreads = 1 + (int)Math.Ceiling(Math.Max(0, items - BookView.EntriesInFirstPage) / 26.0);
        Check(check, $"物品分类 {items} 个条目 → {v.SpreadCount} 个跨页（首页 11，之后每跨页 26）", v.SpreadCount == expectSpreads);
        Check(check, "翻到最后一页后不能再翻", Enumerable.Range(0, 10).Count(_ => v.NextSpread()) == expectSpreads - 1 && !v.NextSpread());
        Check(check, "进条目", v.OpenEntry("items/focus") && v.Kind == BookViewKind.Entry && v.Spread == 0);
        Check(check, "返回 → 回到进条目之前那一页（历史栈，不是固定退一层）",
            v.Back() && v.Kind == BookViewKind.Category && v.Spread == expectSpreads - 1);
        Check(check, "再返回 → 落地页", v.Back() && v.Kind == BookViewKind.Landing);

        v.Open();
        v.OpenEntry("items/focus");
        bool followed = v.FollowLink("patterns/readwrite#hexcasting:write");
        var e = v.CurrentEntry!;
        int anchorPage = e.Pages.FindIndex(p => p.Anchor == "hexcasting:write");
        Check(check, "链接带锚点：跳到锚点那一页所在的跨页", followed && e.Id == "patterns/readwrite" && anchorPage >= 0 && v.Spread == anchorPage / 2,
            $"entry={e.Id} page={anchorPage} spread={v.Spread}");
        Check(check, "从链接返回 → 回到原来的条目", v.Back() && v.CurrentEntry?.Id == "items/focus");
        Check(check, "外部网址不当作站内链接", !v.FollowLink("https://forum.petra-k.at"));
    }

    static void RenderAll(BookDocument doc, CheckFn check)
    {
        var data = new FakeData();
        var r = new PatchouliRenderer(data);
        var canvas = new RecordingCanvas();
        var v = new BookView(doc);
        int views = 0, overflow = 0, errors = 0;
        var overflowAt = new List<string>();

        void Once(string name)
        {
            try
            {
                canvas.Ops.Clear();
                var f = r.Render(canvas, v, 1920, 1080, -1, -1);
                views++;
                if (f.OverflowLines > 0) { overflow += f.OverflowLines; overflowAt.Add($"{name}({f.OverflowLines})"); }
                if (Environment.GetEnvironmentVariable("DRAWTEST_RENDER") == "1" && Previews.Contains(name))
                {
                    canvas.Save(Path.Combine(PreviewDir, name.Replace('/', '_') + ".json"), 1920, 1080);
                }
            }
            catch (Exception ex)
            {
                errors++;
                Console.WriteLine($"     渲染 {name} 抛异常：{ex.GetType().Name} {ex.Message}");
            }
        }

        v.Open();
        Once("landing");
        var landing = r.Render(canvas, v, 1920, 1080, -1, -1);
        Check(check, $"落地页：书按 {r.Unit} 倍整数放大（1080p、界面缩放 100% 应为 5：书高 900，约占屏幕 83%）", r.Unit == 5f);
        Check(check, "书本大小设置：0.8 → 4 倍；1.2 → 放不下 6 倍时缩回能放下的最大半格（5.5）",
            PatchouliRenderer.ChooseUnit(1920, 1080, 0.8f) == 4f && PatchouliRenderer.ChooseUnit(1920, 1080, 1.2f) == 5.5f,
            $"{PatchouliRenderer.ChooseUnit(1920, 1080, 0.8f)} {PatchouliRenderer.ChooseUnit(1920, 1080, 1.2f)}");
        Check(check, "界面缩放 150%（视口 1280×720）→ 3 倍，书仍在视口里", PatchouliRenderer.ChooseUnit(1280, 720) == 3f);
        Check(check, "落地页每个分类都有可点的图标格",
            landing.Hits.Count(h => h.Kind == BookActionKind.OpenCategory) == v.TopCategories().Count);

        foreach (var cat in doc.Categories)
        {
            v.Open();
            v.OpenCategory(cat.Id);
            for (int s = 0; s < v.SpreadCount; s++, v.NextSpread()) { Once($"cat_{cat.Id}_{s}"); }
            foreach (var e in cat.Entries)
            {
                v.OpenEntry(e.Id);
                for (int s = 0; s < v.SpreadCount; s++, v.NextSpread()) { Once($"{e.Id}_{s}"); }
                v.Back();
            }
        }

        Check(check, $"全书 {views} 个界面逐一渲染，无异常", errors == 0, $"{errors} 个异常");
        Check(check, $"没有文字溢出页面（近似字宽下；溢出 {overflow} 行）", overflow == 0, string.Join(" ", overflowAt.Take(8)));

        // 点击落地页上「物品」那一格 → 进物品分类
        v.Open();
        var f0 = r.Render(canvas, v, 1920, 1080, -1, -1);
        var hit = f0.Hits.First(h => h.Kind == BookActionKind.OpenCategory && h.Arg == "items");
        var clicked = f0.HitAt(hit.Rect.X + 2, hit.Rect.Y + 2);
        Check(check, "命中区与绘制同源：点落地页图标格命中那个分类", clicked?.Arg == "items");

        // 悬停图标格：提示分类名，且那一格不再盖半透明书页（原版的「褪色 → 清晰」效果）
        var fh = r.Render(canvas, v, 1920, 1080, hit.Rect.X + 2, hit.Rect.Y + 2);
        Check(check, "悬停分类图标 → 提示分类名", fh.Tooltip == doc.FindCategory("items")!.DisplayName, fh.Tooltip);
    }

    static readonly HashSet<string> Previews = new()
    {
        "landing", "cat_items_0", "cat_patterns_0", "items/focus_0", "items/focus_1", "patterns/math_1", "basics/media_0",
        "casting/101_0", "patterns/basics_0",
    };

    static readonly string PreviewDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "_shots", "book"));

    static void Check(CheckFn check, string name, bool ok, string? detail = null) => check(name, ok, detail);

    sealed class FakeData : IBookData
    {
        public BookProgress Progress { get; set; } = new() { UnlockAll = true };

        public bool IsUnlocked(string advancement) => BookUnlocks.IsUnlocked(advancement, Progress);

        public string ItemName(string itemKey) => itemKey.Length == 0 ? "" : itemKey.Substring(itemKey.IndexOf(':') + 1);

        public BookRecipe? FindRecipe(string resultItemKey)
        {
            if (!resultItemKey.StartsWith("Mod:")) { return null; }
            var r = new BookRecipe { Result = resultItemKey, Station = "Terraria:WorkBench" };
            r.Ingredients.Add(("Mod:AmethystDust", 4));
            r.Ingredients.Add(("Terraria:Wood", 2));
            r.Ingredients.Add(("Mod:ChargedAmethyst", 1));
            return r;
        }
    }

    /// <summary>记录绘制命令；文字宽度用近似度量。</summary>
    sealed class RecordingCanvas : IBookCanvas
    {
        public readonly List<string> Ops = new();

        public float LineHeight => 26f;

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        static string C(Color32 c) => $"[{c.R},{c.G},{c.B},{c.A}]";

        public void DrawImage(string texture, RectF src, RectF dst, Color32 tint)
            => Ops.Add($"{{\"op\":\"img\",\"tex\":\"{texture}\",\"src\":[{F(src.X)},{F(src.Y)},{F(src.W)},{F(src.H)}],\"dst\":[{F(dst.X)},{F(dst.Y)},{F(dst.W)},{F(dst.H)}],\"c\":{C(tint)}}}");

        public void DrawItem(string itemKey, RectF dst, float alpha)
            => Ops.Add($"{{\"op\":\"item\",\"key\":\"{Esc(itemKey)}\",\"dst\":[{F(dst.X)},{F(dst.Y)},{F(dst.W)},{F(dst.H)}]}}");

        public void FillRect(RectF r, Color32 c)
            => Ops.Add($"{{\"op\":\"rect\",\"dst\":[{F(r.X)},{F(r.Y)},{F(r.W)},{F(r.H)}],\"c\":{C(c)}}}");

        public void DrawLine(float x1, float y1, float x2, float y2, float width, Color32 c)
            => Ops.Add($"{{\"op\":\"line\",\"p\":[{F(x1)},{F(y1)},{F(x2)},{F(y2)}],\"w\":{F(width)},\"c\":{C(c)}}}");

        public void FillCircle(float cx, float cy, float radius, Color32 c)
            => Ops.Add($"{{\"op\":\"circle\",\"p\":[{F(cx)},{F(cy)}],\"r\":{F(radius)},\"c\":{C(c)}}}");

        public float DrawText(string text, float x, float y, Color32 color, float scale, bool bold)
        {
            float w = MeasureText(text, scale, bold);
            Ops.Add($"{{\"op\":\"text\",\"t\":\"{Esc(text)}\",\"p\":[{F(x)},{F(y)}],\"s\":{F(LineHeight * scale)},\"w\":{F(w)},\"b\":{(bold ? "true" : "false")},\"c\":{C(color)}}}");
            return w;
        }

        public float MeasureText(string text, float scale, bool bold)
        {
            float w = 0;
            foreach (char ch in text)
            {
                bool wide = ch >= 0x2E80 && ch <= 0xFFEF;
                w += (wide ? 0.78f : 0.42f) * LineHeight * scale;
            }
            return w + (bold ? Math.Max(1f, scale) : 0f);
        }

        public void Save(string path, int w, int h)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var sb = new StringBuilder();
            sb.Append($"{{\"w\":{w},\"h\":{h},\"ops\":[\n");
            sb.Append(string.Join(",\n", Ops));
            sb.Append("\n]}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
