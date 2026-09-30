using System.Collections.Generic;
using System.IO;
using System.Linq;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Content.Net;

/// <summary>
/// iota ↔ 存档（<see cref="TagCompound"/>）。**所有存 iota 的物品 / 方块实体都走这里**。
///
/// Core 的 <see cref="Iota.Serialize"/> 吐的是信封树（`Dictionary` / 混合类型的 `List` / `null`），
/// 而 `TagCompound` 只收同类型列表、不收字典、不收 null —— 之前各处直接把信封塞进 tag，
/// 一存档就抛 `Invalid NBT payload type`：玩家存档 / 世界存档整个失败（2026-10-01 联机闪退、
/// 箱子里的东西回档、开局物品重发，都是它）。
///
/// 这里把信封树逐节点编码成 TagCompound，每个节点只有一个键，键名就是值的种类：
/// 空 = null，`b` = bool，`d` = double，`s` = string，`l` = 列表（元素也是节点），`m` = 字典。
/// 编码可逆、与 Core 的信封格式一一对应，Core 那边改 iota 种类不用动这里。
/// </summary>
public static class IotaTag
{
    public static TagCompound ToTag(Iota iota) => Encode(iota.Serialize());

    /// <summary>读不出来返回 false —— 调用方把它当「空」，不要造默认值（同 <see cref="IotaSerializer.TryDeserialize"/>）。</summary>
    public static bool TryFromTag(object? tag, out Iota iota)
    {
        iota = NullIota.Instance;
        return tag is TagCompound t && TryDecode(t, out var tree) && IotaSerializer.TryDeserialize(tree, out iota);
    }

    private static TagCompound Encode(object? value)
    {
        switch (value)
        {
            case null: return new TagCompound();
            case bool b: return new TagCompound { ["b"] = b };
            case double d: return new TagCompound { ["d"] = d };
            case string s: return new TagCompound { ["s"] = s };
            case List<object?> list:
                return new TagCompound { ["l"] = list.ConvertAll(Encode) };
            case Dictionary<string, object?> map:
            {
                var m = new TagCompound();
                foreach (var kv in map) m[kv.Key] = Encode(kv.Value);
                return new TagCompound { ["m"] = m };
            }
            default:
                throw new InvalidDataException($"iota 信封里出现了不能存档的类型 {value.GetType()}");
        }
    }

    private static bool TryDecode(TagCompound tag, out object? value)
    {
        value = null;
        if (tag.Count == 0) return true;
        if (tag.TryGet("b", out bool b)) { value = b; return true; }
        if (tag.TryGet("d", out double d)) { value = d; return true; }
        if (tag.TryGet("s", out string s)) { value = s; return true; }
        if (tag.ContainsKey("l"))
        {
            var list = new List<object?>();
            foreach (var t in tag.GetList<TagCompound>("l"))
            {
                if (!TryDecode(t, out var item)) return false;
                list.Add(item);
            }
            value = list;
            return true;
        }
        if (tag.TryGet("m", out TagCompound m))
        {
            var map = new Dictionary<string, object?>();
            foreach (var kv in m)
            {
                if (kv.Value is not TagCompound child || !TryDecode(child, out var item)) return false;
                map[kv.Key] = item;
            }
            value = map;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 加载时自检：每种 iota 都经 `TagIO` 真写一遍二进制再读回来，信封树必须一模一样。
    /// 专用服务器验证（verify_server.ps1）检查这一行的「失败 0 条」。
    /// </summary>
    public static (int total, List<string> failures) SelfTest()
    {
        HexPattern.TryFromAngles("qaq", HexDir.NorthEast, out var pattern, out _);
        var samples = new List<Iota>
        {
            NullIota.Instance, GarbageIota.Instance, BooleanIota.Of(true), BooleanIota.Of(false),
            new DoubleIota(-1.25), new VectorIota(1.5, -2, 3), new EntityIota(EntityIota.EntityKind.Npc, 7),
            new PatternIota(pattern!), new ListIota(new List<Iota>()),
        };
        samples.Add(new ListIota(new List<Iota>(samples) { new ListIota(new List<Iota> { new DoubleIota(2), NullIota.Instance }) }));

        var failures = new List<string>();
        foreach (var iota in samples)
        {
            try
            {
                using var ms = new MemoryStream();
                TagIO.ToStream(new TagCompound { ["iota"] = ToTag(iota) }, ms);
                ms.Position = 0;
                var back = TagIO.FromStream(ms);
                if (!TryFromTag(back.Get<TagCompound>("iota"), out var read) || !SameTree(iota.Serialize(), read.Serialize()))
                {
                    failures.Add($"{iota.Display()} 读回不一致");
                }
            }
            catch (System.Exception e)
            {
                failures.Add($"{iota.Display()}：{e.Message}");
            }
        }
        return (samples.Count, failures);
    }

    private static bool SameTree(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (List<object?> x, List<object?> y) => x.Count == y.Count && Enumerable.Range(0, x.Count).All(i => SameTree(x[i], y[i])),
        (Dictionary<string, object?> x, Dictionary<string, object?> y)
            => x.Count == y.Count && x.All(kv => y.TryGetValue(kv.Key, out var v) && SameTree(kv.Value, v)),
        _ => Equals(a, b),
    };
}
