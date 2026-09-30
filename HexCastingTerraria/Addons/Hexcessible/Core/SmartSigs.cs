using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Addons.Hexcessible.Core;

/// <summary>智能签名用到的文字（格式串里的 {0} 对应上游的 %s）。</summary>
public sealed record SmartSigText(
    string NumberName,
    string MaskName,
    string BookkeeperPrefix,
    string Drop,
    string Drop1,
    string Keep,
    string Keep1,
    string Join,
    string Suffix);

/// <summary>
/// 智能签名（上游 smartsig/SmartSig.java 的登记表 + Number.java + Bookkeeper.java）：按查询串或签名现算出来的「图案」——
/// 打一个数字就给出画这个数的笔顺（大数拆成几条图案再用幂 / 乘 / 加 / 除拼起来），打 v 和 - 就给出簿记员之策略。
/// 上游的 Escape（考察 / 内省 / 反思 / 消解）在移植版里本来就是普通图案，见 <see cref="PatternEntries"/>。
/// 其余附属（Hexical、HexThings、Overevaluate 等）的智能签名随那些附属，不在这里。
/// </summary>
public sealed class SmartSigs
{
    private readonly SmartSigText _text;

    public SmartSigs(SmartSigText text) => _text = text;

    /// <summary>上游 SmartSigRegistry.get(query)：按登记顺序（数字、簿记员）收集。</summary>
    public List<PatternEntries.Entry> FromQuery(string query)
    {
        var list = new List<PatternEntries.Entry>();
        if (NumberFromQuery(query) is { } n) list.Add(n);
        if (BookkeeperFromQuery(query) is { } b) list.Add(b);
        return list;
    }

    /// <summary>上游 SmartSigRegistry.get(sig)：第一个认得的。</summary>
    public PatternEntries.Entry? FromSig(IReadOnlyList<HexAngle> sig) => NumberFromSig(sig) ?? BookkeeperFromSig(sig);

    // ==================== 数字（上游 Number.java） ====================

    private static readonly List<HexAngle> Aqaa = Angles("aqaa");

    public PatternEntries.Entry? NumberFromQuery(string query)
    {
        if (!JavaNum.TryParseFloat(query, out float num)) return null;
        if (num >= int.MaxValue || num <= int.MinValue) return null;
        return NumberSigs(num) is null ? null : NumberEntry(num);
    }

    public PatternEntries.Entry? NumberFromSig(IReadOnlyList<HexAngle> sig)
    {
        var s = Letters(sig);
        if (!s.StartsWith("aqaa", StringComparison.Ordinal) && !s.StartsWith("dedd", StringComparison.Ordinal)) return null;
        float num = Evaluate(sig.Skip(4));
        if (s.StartsWith("dedd", StringComparison.Ordinal)) num = -num;
        return NumberEntry(num);
    }

    private PatternEntries.Entry? NumberEntry(float target)
    {
        var sigs = NumberSigs(target);
        if (sigs is null) return null;
        string shown = JavaNum.FloatToString(target);
        var doc = new PatternEntries.Impl("hexcessible:number", "", "(experimental)", "", shown, 0);
        return new PatternEntries.Entry("hexcessible:number/" + JavaNum.TwoDecimals(target), string.Format(_text.NumberName, shown),
            HexDir.SouthEast, sigs, sigs, new[] { doc }, 1);
    }

    /// <summary>上游 getFor(List)：数字之精思前缀后面的笔顺算出的值（w +1、q +5、e +10、a ×2、d ÷2）。</summary>
    public static float Evaluate(IEnumerable<HexAngle> angles)
    {
        float v = 0f;
        foreach (var a in angles)
        {
            v = a switch
            {
                HexAngle.Left => v + 5,
                HexAngle.Forward => v + 1,
                HexAngle.Right => v + 10,
                HexAngle.LeftBack => v * 2,
                HexAngle.RightBack => v / 2f,
                _ => v,
            };
        }
        return v;
    }

    /// <summary>上游 getFor(float)：一条就能画出来的直接给；否则拆开。</summary>
    public static IReadOnlyList<IReadOnlyList<HexAngle>>? NumberSigs(float target)
    {
        if (target == 0) return new[] { Aqaa };
        string prefix = target >= 0 ? "aqaa" : "dedd";
        if (target == JavaNum.ToInt(target))
        {
            var simple = TrySimple(Math.Abs((long)JavaNum.ToInt(target)), prefix);
            if (simple is not null) return new[] { simple };
        }
        return DecomposeNumber(target);
    }

    /// <summary>上游 trySimplePattern：2000 以内查表。（上游对 int 最小值取绝对值会越界崩溃，这里用 long 取绝对值。）</summary>
    private static IReadOnlyList<HexAngle>? TrySimple(long target, string prefix)
    {
        if (target > 2000) return null;
        if (target == 0) return Aqaa;
        return Angles(prefix + NumberTable.Suffix[target - 1]);
    }

    private static List<IReadOnlyList<HexAngle>> DecomposeNumber(float target)
    {
        if (Math.Abs(target - (float)JavaNum.Round(target)) < 0.001) return DecomposeInt(JavaNum.Round(target));

        // 非整数：试着写成分数（分母 2..32），分子分母各自拆开再除
        for (int denom = 2; denom <= 32; denom++)
        {
            int numer = JavaNum.Round(target * denom);
            if (Math.Abs(target - (float)numer / denom) < 0.001)
            {
                var r = new List<IReadOnlyList<HexAngle>>();
                r.AddRange(DecomposeInt(numer));
                r.AddRange(DecomposeInt(denom));
                r.Add(Angles("wdedw"));   // 除
                return r;
            }
        }
        return DecomposeInt(JavaNum.Round(target));
    }

    /// <summary>上游 decomposeInt：a^b × c + d 或 a^b + g，取条数少的。</summary>
    private static List<IReadOnlyList<HexAngle>> DecomposeInt(int target)
    {
        long absTarget = Math.Abs((long)target);
        if (absTarget <= 2000)
        {
            var p = TrySimple(absTarget, target >= 0 ? "aqaa" : "dedd");
            if (p is not null) return new List<IReadOnlyList<HexAngle>> { p };
        }
        int abs = (int)Math.Min(absTarget, int.MaxValue);

        int bestA = 2, bestE = 2;
        var d1 = Inner(abs, bestA);
        int bestB = d1[0], bestC = d1[1], bestD = d1[2], bestF = d1[3], bestG = d1[4];
        for (int a = 3; a <= Math.Sqrt(abs); a++)
        {
            var d = Inner(abs, a);
            if (d[2] < bestD)
            {
                bestA = a;
                bestB = d[0];
                bestC = d[1];
                bestD = d[2];
            }
            if (d[4] < bestG)
            {
                bestE = a;
                bestF = d[3];
                bestG = d[4];
            }
        }

        var opt1 = Build(bestA, bestB, bestC, bestD, true);
        var opt2 = Build(bestE, bestF, 1, bestG, false);
        var result = opt1.Count <= opt2.Count ? opt1 : opt2;
        if (target < 0) result = Negate(result);
        return result;
    }

    private static int[] Inner(int num, int a)
    {
        int b = JavaNum.ToInt(Math.Floor(Math.Log(num) / Math.Log(a)));
        int aPowB = JavaNum.ToInt(Math.Pow(a, b));
        int c = num / aPowB;
        int d = num - aPowB * c;
        int g = num - aPowB;
        return new[] { b, c, d, b, g };
    }

    private static List<IReadOnlyList<HexAngle>> Build(int a, int b, int c, int d, bool includeC)
    {
        var r = new List<IReadOnlyList<HexAngle>>();
        r.AddRange(DecomposeInt(a));
        r.AddRange(DecomposeInt(b));
        r.Add(Angles("wedew"));   // 幂
        if (includeC && c != 1)
        {
            r.AddRange(DecomposeInt(c));
            r.Add(Angles("waqaw"));   // 乘
        }
        if (d != 0)
        {
            r.AddRange(DecomposeInt(d));
            r.Add(Angles("waaw"));    // 加
        }
        return r;
    }

    private static List<IReadOnlyList<HexAngle>> Negate(List<IReadOnlyList<HexAngle>> patterns)
    {
        if (patterns.Count == 1)
        {
            var s = Letters(patterns[0]);
            if (s.StartsWith("aqaa", StringComparison.Ordinal)) return new List<IReadOnlyList<HexAngle>> { Angles("dedd" + s[4..]) };
            if (s.StartsWith("dedd", StringComparison.Ordinal)) return new List<IReadOnlyList<HexAngle>> { Angles("aqaa" + s[4..]) };
        }
        var r = new List<IReadOnlyList<HexAngle>>(patterns) { Angles("deddw"), Angles("waqaw") };   // × -1
        return r;
    }

    // ==================== 簿记员之策略（上游 Bookkeeper.java；true = 丢掉，false = 留下） ====================

    /// <summary>查询里只有 v（丢）和 - 或 _（留）时给出对应的簿记员之策略。</summary>
    public PatternEntries.Entry? BookkeeperFromQuery(string query)
    {
        var target = new List<bool>();
        foreach (var ch in query)
        {
            if (ch is 'v' or 'V') target.Add(true);
            else if (ch is '-' or '_') target.Add(false);
            else return null;
        }
        return target.Count == 0 ? null : BookkeeperEntry(target);
    }

    /// <summary>上游 get(sig)：按上一步丢没丢推下一步的转角（第一笔是 a 就是先丢）。</summary>
    public PatternEntries.Entry? BookkeeperFromSig(IReadOnlyList<HexAngle> sigIn)
    {
        if (sigIn.Count == 0) return null;
        var sig = sigIn.ToList();
        var target = new List<bool>();
        bool lastWasDrop = false;
        if (sig[0] == HexAngle.LeftBack)
        {
            target.Add(true);
            sig.RemoveAt(0);
            lastWasDrop = true;
        }
        else
        {
            target.Add(false);
        }

        int i = 0;
        while (i < sig.Count)
        {
            var dir = sig[i];
            var next = i + 1 < sig.Count ? sig[i + 1] : sig[i];
            if (dir == (lastWasDrop ? HexAngle.Right : HexAngle.Forward))
            {
                target.Add(false);
                lastWasDrop = false;
                i += 1;
            }
            else if (dir == (lastWasDrop ? HexAngle.RightBack : HexAngle.Right) && next == HexAngle.LeftBack)
            {
                target.Add(true);
                lastWasDrop = true;
                i += 2;
            }
            else
            {
                return null;
            }
        }
        return BookkeeperEntry(target);
    }

    private PatternEntries.Entry BookkeeperEntry(List<bool> target)
    {
        var angles = new List<HexAngle>();
        var inp = new StringBuilder();
        var outp = new StringBuilder();
        for (int i = 0; i < target.Count; i++)
        {
            bool lastWasDrop = i > 0 && target[i - 1];
            if (target[i])
            {
                angles.Add(lastWasDrop ? HexAngle.RightBack : HexAngle.Right);
                angles.Add(HexAngle.LeftBack);
                if (inp.Length > 0) inp.Append(", ");
                inp.Append('_');
            }
            else
            {
                angles.Add(lastWasDrop ? HexAngle.Right : HexAngle.Forward);
                if (inp.Length > 0) inp.Append(", ");
                if (outp.Length > 0) outp.Append(", ");
                inp.Append(i + 1);
                outp.Append(i + 1);
            }
        }
        // 第一笔由起笔方向决定，不归智能签名管
        angles.RemoveAt(0);

        var rep = new string(target.Select(t => t ? 'v' : '-').ToArray());
        var doc = new PatternEntries.Impl("hexcessible:bookkeeper", "", BookkeeperDesc(target), inp.ToString(), outp.ToString(), 0);
        var sigs = new[] { (IReadOnlyList<HexAngle>)angles };
        return new PatternEntries.Entry("hexcessible:bookkeeper/" + rep, string.Format(_text.MaskName, rep), HexDir.East, sigs, sigs, new[] { doc });
    }

    /// <summary>上游 getDesc：「从当前的栈出发，此图案会 保留 2 个，然后 删除 1 个 ……。」</summary>
    private string BookkeeperDesc(List<bool> target)
    {
        bool firstIsDrop = target[0];
        var counts = new List<int>();
        bool current = !firstIsDrop;
        foreach (var t in target)
        {
            if (current != t)
            {
                counts.Add(1);
                current = !current;
            }
            else
            {
                counts[^1]++;
            }
        }

        var sb = new StringBuilder(_text.BookkeeperPrefix);
        for (int i = 0; i < counts.Count; i++)
        {
            if (i != 0) sb.Append(_text.Join);
            bool drop = i % 2 == 0 ? firstIsDrop : !firstIsDrop;
            int n = counts[i];
            sb.Append(n == 1 ? (drop ? _text.Drop1 : _text.Keep1) : string.Format(drop ? _text.Drop : _text.Keep, n));
        }
        sb.Append(_text.Suffix);
        return sb.ToString();
    }

    private static List<HexAngle> Angles(string s) => s.Select(c => KeyboardPlacement.AngleOf(c)!.Value).ToList();

    private static string Letters(IEnumerable<HexAngle> sig) => new(sig.Select(KeyboardPlacement.LetterOf).ToArray());
}
