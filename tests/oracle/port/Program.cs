using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HexCastingTerraria.Core.Casting.Actions;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

// 和原版对拍（移植版这一侧）。
// 用法：oracleport CASES GOLDEN REPORT
//   CASES  = 用例（tests/oracle/cases/*.json），GOLDEN = 原版跑同一份用例的结果（tests/oracle/original.py run 产出）
//   REPORT = 写出比对报告（JSON）
// 每个用例照原版法杖的方式逐个 iota 执行：每步新建环境、镜像接着用、栈清空就换新镜像（见 mc/src/hexoracle/HexOracle.java）。
// 比对每一步的：结果类型、栈、事故、媒质、操作数、括号层数、转义状态、栈是否清空、渡鸦之思。消息文字不比（两边语言不同）。

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("用法：oracleport CASES GOLDEN REPORT");
            return 2;
        }
        PatternRegistry.Load();
        HexActions.RegisterAll();
        var cases = JsonNode.Parse(ReadText(args[0]))!["cases"]!.AsArray();
        var golden = new Dictionary<string, JsonObject>();
        foreach (var r in JsonNode.Parse(ReadText(args[1]))!["results"]!.AsArray())
            golden[r!["id"]!.GetValue<string>()] = r.AsObject();

        int same = 0, diff = 0, noWorld = 0, missing = 0;
        var diffs = new JsonArray();
        var byAction = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in cases)
        {
            var id = c!["id"]!.GetValue<string>();
            if (!golden.TryGetValue(id, out var want))
            {
                missing++;
                continue;
            }
            var got = Run(c.AsObject());
            var where = Compare(want, got);
            if (where is null)
            {
                same++;
                continue;
            }
            if (UsesNoWorld(got))
            {
                noWorld++;
                continue;
            }
            diff++;
            var action = c["action"]?.GetValue<string>() ?? "(program)";
            byAction[action] = byAction.GetValueOrDefault(action) + 1;
            diffs.Add(new JsonObject
            {
                ["id"] = id,
                ["action"] = action,
                ["where"] = where,
                ["original"] = want.DeepClone(),
                ["port"] = got,
            });
        }

        var report = new JsonObject
        {
            ["cases"] = cases.Count,
            ["same"] = same,
            ["diff"] = diff,
            ["portHasNoWorld"] = noWorld,
            ["missingGolden"] = missing,
            ["diffByAction"] = new JsonObject(byAction.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
            ["diffs"] = diffs,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
        File.WriteAllText(args[2], report.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine($"用例 {cases.Count}：一致 {same}，不一致 {diff}，移植版缺世界 {noWorld}，原版没有结果 {missing}");
        foreach (var kv in byAction.OrderByDescending(kv => kv.Value).Take(40))
            Console.WriteLine($"  {kv.Key}: {kv.Value}");
        return diff == 0 && missing == 0 ? 0 : 1;
    }

    /// <summary>读文本；以 .gz 结尾的先解压（入库的标准答案是压缩的）。</summary>
    static string ReadText(string path)
    {
        if (!path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) return File.ReadAllText(path);
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        return reader.ReadToEnd();
    }

    // ---------------- 执行 ----------------

    sealed class OracleEnv : CastingEnvironment
    {
        private readonly bool _enlightened;
        public readonly JsonArray Mishaps = new();
        public long Media;

        public OracleEnv(bool enlightened) => _enlightened = enlightened;

        protected override long ExtractMediaEnvironment(long cost, bool simulate)
        {
            if (!simulate) Media += cost;
            return 0;
        }

        public override bool IsEnlightened() => _enlightened;

        public override void PostExecution(CastResult result)
        {
            foreach (var e in result.SideEffects)
                if (e is DoMishapSideEffect m) Mishaps.Add(MishapAlias.GetValueOrDefault(m.Mishap.GetType().Name, m.Mishap.GetType().Name));
        }
    }

    static JsonObject Run(JsonObject c)
    {
        var r = new JsonObject { ["id"] = c["id"]!.GetValue<string>() };
        var steps = new JsonArray();
        r["steps"] = steps;
        try
        {
            bool enlightened = c["enlightened"]?.GetValue<bool>() ?? true;
            var image = new CastingImage(c["stack"]!.AsArray().Select(x => Parse(x!)).ToList());
            foreach (var p in c["program"]!.AsArray())
            {
                var env = new OracleEnv(enlightened);
                var vm = new CastingVM(image, env);
                var outcome = vm.QueueExecute(image, new[] { Parse(p!) });
                image = outcome.Image;
                bool clear = image.Stack.Count == 0 && image.ParenCount == 0 && !image.EscapeNext && image.UserData.Ravenmind is null;
                var s = new JsonObject
                {
                    ["res"] = ResName(outcome.ResolutionType),
                    ["stack"] = new JsonArray(image.Stack.Select(Write).ToArray()),
                    ["mishaps"] = env.Mishaps,
                    ["media"] = env.Media,
                    ["ops"] = image.OpsConsumed,
                    ["parens"] = image.ParenCount,
                    ["escape"] = image.EscapeNext,
                    ["clear"] = clear,
                };
                if (image.UserData.Ravenmind is { } raven) s["raven"] = Write(raven);
                steps.Add(s);
                if (clear) image = new CastingImage();
            }
        }
        catch (Exception e)
        {
            r["exception"] = e.ToString();
        }
        return r;
    }

    static string ResName(ResolvedPatternType t) => t switch
    {
        ResolvedPatternType.Unresolved => "UNRESOLVED",
        ResolvedPatternType.Evaluated => "EVALUATED",
        ResolvedPatternType.Escaped => "ESCAPED",
        ResolvedPatternType.Undone => "UNDONE",
        ResolvedPatternType.Errored => "ERRORED",
        ResolvedPatternType.Invalid => "INVALID",
        _ => t.ToString(),
    };

    /// <summary>
    /// 移植版这边的对拍环境还没接世界和施法者（第二轮做），因此报的「没有世界 / 施法者不对」先单独计数，不算差异。
    /// </summary>
    static bool UsesNoWorld(JsonObject got)
        => got["steps"]!.AsArray().Any(s => s!["mishaps"]!.AsArray().Any(m => m!.GetValue<string>() is "MishapNoWorld" or "MishapBadCaster"));

    /// <summary>
    /// 移植版改了名、意思一样的事故：原版「另一只手的物品不对」，泰拉没有副手，移植版叫「手持物品不对」（指快捷栏中手持物品右边一格）。
    /// </summary>
    static readonly Dictionary<string, string> MishapAlias = new()
    {
        ["MishapBadHeldItem"] = "MishapBadOffhandItem",
    };

    // ---------------- iota 与 JSON（格式见 mc/src/hexoracle/IotaJson.java）----------------

    static Iota Parse(JsonNode n)
    {
        var t = n["t"]!.GetValue<string>();
        return t switch
        {
            "num" => new DoubleIota(Num(n["v"]!)),
            "bool" => new BooleanIota(n["v"]!.GetValue<bool>()),
            "null" => NullIota.Instance,
            "garbage" => GarbageIota.Instance,
            "vec" => new VectorIota(Num(n["v"]![0]!), Num(n["v"]![1]!), Num(n["v"]![2]!)),
            "list" => new ListIota(n["v"]!.AsArray().Select(x => Parse(x!)).ToList()),
            "pat" => new PatternIota(Pattern(n)),
            _ => throw new InvalidDataException("不支持的 iota 类型：" + t),
        };
    }

    static HexPattern Pattern(JsonNode n)
    {
        var dir = n["dir"]!.GetValue<string>() switch
        {
            "NORTH_EAST" => HexDir.NorthEast,
            "EAST" => HexDir.East,
            "SOUTH_EAST" => HexDir.SouthEast,
            "SOUTH_WEST" => HexDir.SouthWest,
            "WEST" => HexDir.West,
            "NORTH_WEST" => HexDir.NorthWest,
            var d => throw new InvalidDataException("方向：" + d),
        };
        if (!HexPattern.TryFromAngles(n["angles"]!.GetValue<string>(), dir, out var p, out var err) || p is null)
            throw new InvalidDataException("笔顺不合法：" + err);
        return p;
    }

    static string DirName(HexDir d) => d switch
    {
        HexDir.NorthEast => "NORTH_EAST",
        HexDir.East => "EAST",
        HexDir.SouthEast => "SOUTH_EAST",
        HexDir.SouthWest => "SOUTH_WEST",
        HexDir.West => "WEST",
        _ => "NORTH_WEST",
    };

    static JsonNode Write(Iota i) => i switch
    {
        DoubleIota d => new JsonObject { ["t"] = "num", ["v"] = Num(d.Value) },
        BooleanIota b => new JsonObject { ["t"] = "bool", ["v"] = b.Value },
        NullIota => new JsonObject { ["t"] = "null" },
        GarbageIota => new JsonObject { ["t"] = "garbage" },
        VectorIota v => new JsonObject { ["t"] = "vec", ["v"] = new JsonArray(Num(v.X), Num(v.Y), Num(v.Z)) },
        ListIota l => new JsonObject { ["t"] = "list", ["v"] = new JsonArray(l.Items.Select(Write).ToArray()) },
        PatternIota p => new JsonObject { ["t"] = "pat", ["dir"] = DirName(p.Pattern.StartDir), ["angles"] = p.Pattern.AnglesSignature() },
        _ => new JsonObject { ["t"] = "other", ["class"] = i.GetType().Name },
    };

    static double Num(JsonNode n)
        => n.GetValueKind() == JsonValueKind.String ? double.Parse(n.GetValue<string>(), CultureInfo.InvariantCulture) : n.GetValue<double>();

    static JsonNode Num(double d)
        => double.IsNaN(d) ? "NaN" : double.IsPositiveInfinity(d) ? "Infinity" : double.IsNegativeInfinity(d) ? "-Infinity" : JsonValue.Create(d)!;

    // ---------------- 比对 ----------------

    /// <summary>一致返回 null；不一致返回第一个对不上的位置，如 "step 0 stack"。</summary>
    static string? Compare(JsonObject want, JsonObject got)
    {
        if (want["exception"] is not null || got["exception"] is not null)
            return want["exception"] is not null && got["exception"] is not null ? null : "exception";
        var ws = want["steps"]!.AsArray();
        var gs = got["steps"]!.AsArray();
        if (ws.Count != gs.Count) return "step count";
        for (int k = 0; k < ws.Count; k++)
        {
            foreach (var field in new[] { "res", "stack", "mishaps", "media", "ops", "parens", "escape", "clear", "raven" })
            {
                if (!Same(ws[k]![field], gs[k]![field])) return $"step {k} {field}";
            }
        }
        return null;
    }

    static bool Same(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is JsonArray aa && b is JsonArray ba)
            return aa.Count == ba.Count && aa.Zip(ba).All(z => Same(z.First, z.Second));
        if (a is JsonObject ao && b is JsonObject bo)
        {
            // 其余类型（跳转 iota 等）只比类名：原版附带的存档格式移植版没有
            if (ao["t"]?.GetValue<string>() == "other" && bo["t"]?.GetValue<string>() == "other")
                return Same(ao["class"], bo["class"]);
            return ao.Count == bo.Count && ao.All(kv => bo.ContainsKey(kv.Key) && Same(kv.Value, bo[kv.Key]));
        }
        if (a.GetValueKind() == JsonValueKind.Number && b.GetValueKind() == JsonValueKind.Number)
        {
            // 两边有的是 long（媒质、操作数）有的是 double，统一按文本读成 double 再比
            double x = double.Parse(a.ToJsonString(), CultureInfo.InvariantCulture), y = double.Parse(b.ToJsonString(), CultureInfo.InvariantCulture);
            return System.Math.Abs(x - y) <= 1e-9 * System.Math.Max(1.0, System.Math.Max(System.Math.Abs(x), System.Math.Abs(y)));
        }
        return JsonNode.DeepEquals(a, b);
    }
}
