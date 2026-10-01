using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Content.Net;

/// <summary>
/// 施法镜像 ↔ 存档（原版 CastingImage.serializeToNbt / loadFromNbt）。法术环走到一半存档时用：
/// 栈、括号里攒着的 iota、转义标记、已用算力、临时数据袋（渡鸦之思、推动过的目标）都存。
///
/// 移植版的跳转 iota 还存不了档（<see cref="Iota.Serialize"/> 明确抛 NotSupportedException），原版能存。
/// 存的时候先查，存不了的 iota 换成垃圾，不让整份世界存档失败。
/// </summary>
public static class CastingImageTag
{
    public static TagCompound ToTag(CastingImage image)
    {
        var stack = new List<TagCompound>();
        foreach (var iota in image.Stack) stack.Add(Safe(iota));

        var paren = new List<TagCompound>();
        var escaped = new List<int>();
        foreach (var p in image.Parenthesized)
        {
            paren.Add(Safe(p.Iota));
            escaped.Add(p.Escaped ? 1 : 0);
        }

        var groups = new TagCompound();
        foreach (var (group, keys) in image.UserData.Groups()) groups[group] = new List<string>(keys);

        var tag = new TagCompound
        {
            ["stack"] = stack,
            ["parenCount"] = image.ParenCount,
            ["paren"] = paren,
            ["parenEscaped"] = escaped,
            ["escapeNext"] = image.EscapeNext,
            ["ops"] = image.OpsConsumed,
            ["groups"] = groups,
        };
        if (image.UserData.Ravenmind is { } ravenmind) tag["ravenmind"] = Safe(ravenmind);
        return tag;
    }

    public static CastingImage FromTag(TagCompound tag)
    {
        var stack = new List<Iota>();
        foreach (var t in tag.GetList<TagCompound>("stack")) stack.Add(Load(t));

        var paren = new List<ParenthesizedIota>();
        var parenTags = tag.GetList<TagCompound>("paren");
        var escaped = tag.GetList<int>("parenEscaped");
        for (int i = 0; i < parenTags.Count; i++)
        {
            paren.Add(new ParenthesizedIota(Load(parenTags[i]), i < escaped.Count && escaped[i] != 0));
        }

        var userData = new CastUserData();
        if (tag.TryGet("ravenmind", out TagCompound ravenmind)) userData.Ravenmind = Load(ravenmind);
        if (tag.TryGet("groups", out TagCompound groups))
        {
            foreach (var kv in groups)
            {
                foreach (var key in groups.GetList<string>(kv.Key)) userData.Add(kv.Key, key);
            }
        }

        return new CastingImage(stack, tag.GetInt("parenCount"), paren, tag.GetBool("escapeNext"), tag.GetLong("ops"), userData);
    }

    /// <summary>
    /// 加载时自检（挂在 <see cref="IotaTag.SelfTest"/> 里，专用服务器验证看它的「失败 0 条」）：
    /// 一个什么都有的镜像经 TagIO 真写一遍二进制再读回，逐项比对；栈上的跳转 iota 必须变成垃圾而不是让存档失败。
    /// </summary>
    internal static List<string> SelfTest()
    {
        var failures = new List<string>();
        try
        {
            HexPattern.TryFromAngles("qaq", HexDir.NorthEast, out var pattern, out _);
            var userData = new CastUserData { Ravenmind = new VectorIota(1, 2, 3) };
            userData.Add(CastUserData.MarkedMovedGroup, "npc:3");
            var image = new CastingImage(
                new List<Iota> { new DoubleIota(1.5), new ListIota(new List<Iota> { new PatternIota(pattern!), NullIota.Instance }), new ContinuationIota(SpellContinuation.Start()) },
                1,
                new List<ParenthesizedIota> { new(new DoubleIota(2), true), new(new PatternIota(pattern!), false) },
                true, 42, userData);

            using var ms = new System.IO.MemoryStream();
            TagIO.ToStream(new TagCompound { ["image"] = ToTag(image) }, ms);
            ms.Position = 0;
            var back = FromTag(TagIO.FromStream(ms).Get<TagCompound>("image"));

            bool Same(Iota a, Iota b) => IotaSerializer.SameTree(a.Serialize(), b.Serialize());
            if (back.Stack.Count != 3 || !Same(back.Stack[0], image.Stack[0]) || !Same(back.Stack[1], image.Stack[1]))
                failures.Add("施法镜像：栈读回不一致");
            if (back.Stack.Count == 3 && back.Stack[2] is not GarbageIota)
                failures.Add("施法镜像：跳转 iota 没换成垃圾");
            if (back.ParenCount != 1 || back.Parenthesized.Count != 2 || !back.Parenthesized[0].Escaped || back.Parenthesized[1].Escaped
                || !Same(back.Parenthesized[1].Iota, image.Parenthesized[1].Iota))
                failures.Add("施法镜像：括号读回不一致");
            if (!back.EscapeNext || back.OpsConsumed != 42)
                failures.Add("施法镜像：转义 / 算力读回不一致");
            if (back.UserData.Ravenmind is not { } r || !Same(r, userData.Ravenmind!) || !back.UserData.Has(CastUserData.MarkedMovedGroup, "npc:3"))
                failures.Add("施法镜像：临时数据袋读回不一致");
        }
        catch (System.Exception e)
        {
            failures.Add("施法镜像：" + e.Message);
        }
        return failures;
    }

    private static TagCompound Safe(Iota iota) => IotaTag.ToTag(Savable(iota) ? iota : GarbageIota.Instance);

    /// <summary>
    /// 跳转 iota（含嵌在列表里的）移植版存不了档。先查再存，不靠抛异常再接住 ——
    /// tML 会把每个被接住的异常记成一条警告，玩家存档时日志里会冒出来。
    /// </summary>
    private static bool Savable(Iota iota) => iota switch
    {
        ContinuationIota => false,
        ListIota list => System.Linq.Enumerable.All(list.Items, Savable),
        _ => true,
    };

    private static Iota Load(TagCompound tag) => IotaTag.TryFromTag(tag, out var iota) ? iota : GarbageIota.Instance;
}
