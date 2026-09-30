using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>
/// <c>nbt_…</c>：没有文本写法的 iota 整个编码进代码（上游 parsers/FallbackBinaryParser.kt）。
///
/// 上游是「MC 的 NBT 字节 -> zlib 压缩 -> Base64，+ 换成 -，= 换成 _」。
/// **偏差**：泰拉没有 NBT，这里编码的是本模组的 iota 信封树（IotaSerializer 的格式），外层做法一样（zlib + Base64 + 同样的替换）。
/// 所以 MC 里导出的 nbt_ 串在这里读不出来（反之亦然）；本模组内部读写往返一致。
/// 读出来是不认识的种类（例如关掉的附属留下的）时照样原样保管（UnknownIota）。
/// </summary>
public static class FallbackBinary
{
    public const string Prefix = "nbt_";

    public static string Encode(Iota iota)
    {
        using var raw = new MemoryStream();
        using (var w = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true))
        {
            WriteNode(w, iota.Serialize());
        }
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(z);
        }
        return Prefix + Convert.ToBase64String(packed.ToArray()).Replace('+', '-').Replace('=', '_');
    }

    /// <summary>解析 nbt_…；数据坏了抛异常（外层发「解析出错」）。</summary>
    public static Iota Decode(string node)
    {
        string b64 = node.Substring(Prefix.Length).Replace('-', '+').Replace('_', '=');
        using var packed = new MemoryStream(Convert.FromBase64String(b64));
        using var z = new ZLibStream(packed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        raw.Position = 0;
        using var r = new BinaryReader(raw, Encoding.UTF8);
        object? tree = ReadNode(r, 0);
        if (!IotaSerializer.TryDeserialize(tree, out var iota)) throw new HexParseException("非法iota：" + node);
        return iota;
    }

    private const byte TNull = 0, TFalse = 1, TTrue = 2, TDouble = 3, TString = 4, TList = 5, TMap = 6;
    private const int MaxDepth = 256;   // 上游 HexIotaTypes.MAX_SERIALIZATION_DEPTH

    private static void WriteNode(BinaryWriter w, object? v)
    {
        switch (v)
        {
            case null: w.Write(TNull); break;
            case bool b: w.Write(b ? TTrue : TFalse); break;
            case double d: w.Write(TDouble); w.Write(d); break;
            case string s: w.Write(TString); w.Write(s); break;
            case List<object?> list:
                w.Write(TList);
                w.Write7BitEncodedInt(list.Count);
                foreach (var x in list) WriteNode(w, x);
                break;
            case Dictionary<string, object?> map:
                w.Write(TMap);
                w.Write7BitEncodedInt(map.Count);
                foreach (var kv in map)
                {
                    w.Write(kv.Key);
                    WriteNode(w, kv.Value);
                }
                break;
            default:
                throw new HexParseException("不能编码的数据：" + v.GetType().Name);
        }
    }

    private static object? ReadNode(BinaryReader r, int depth)
    {
        if (depth > MaxDepth) throw new HexParseException("嵌套过深");
        byte t = r.ReadByte();
        switch (t)
        {
            case TNull: return null;
            case TFalse: return false;
            case TTrue: return true;
            case TDouble: return r.ReadDouble();
            case TString: return r.ReadString();
            case TList:
            {
                int n = r.Read7BitEncodedInt();
                var list = new List<object?>(Math.Min(n, 1024));
                for (int i = 0; i < n; i++) list.Add(ReadNode(r, depth + 1));
                return list;
            }
            case TMap:
            {
                int n = r.Read7BitEncodedInt();
                var map = new Dictionary<string, object?>();
                for (int i = 0; i < n; i++) map[r.ReadString()] = ReadNode(r, depth + 1);
                return map;
            }
            default:
                throw new HexParseException("数据损坏");
        }
    }
}
