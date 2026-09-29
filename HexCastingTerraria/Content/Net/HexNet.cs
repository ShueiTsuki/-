using System.Collections.Generic;
using System.IO;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Content.Net;

/// <summary>
/// 网络消息类型。
/// </summary>
internal enum HexMessage : byte
{
    /// <summary>客户端 → 服务端：我刚画完一条图案。</summary>
    CastPattern = 1,

    /// <summary>服务端 → 该客户端：当前栈状态（HUD 显示用）。</summary>
    StackSync = 2,

    /// <summary>服务端 → 附近客户端：瞄准点标记（纯表现）。</summary>
    SpellVisual = 3,

    /// <summary>客户端 → 服务端：请求清空（潜行右键重开）。</summary>
    ResetCast = 4,

    /// <summary>客户端 → 服务端：请求把图案写进某块石板。</summary>
    SlatePattern = 5,

    /// <summary>客户端 → 服务端：请求改某块石板的朝向。</summary>
    SlateNormal = 6,

    /// <summary>客户端 → 服务端：请求启动某个原动力的法术环。</summary>
    StartCircle = 7,

    /// <summary>客户端 → 服务端：请求改某根导线的轴向。</summary>
    DirectrixFacing = 8,

    /// <summary>服务端 → 附近客户端：某处敲了一个音符（`beep` 法术，纯表现）。</summary>
    Beep = 9,

    /// <summary>客户端 → 服务端：请求施放某个物品栏槽位里的打包法术。</summary>
    CastPackaged = 10,

    /// <summary>服务端 → 附近客户端：法术环的执行游标（当前走到哪一格，纯表现）。</summary>
    CircleCursor = 11,

    /// <summary>客户端 → 服务端：请求改某块壁挂卷轴上挂的图案。</summary>
    WallScroll = 12,

    /// <summary>服务端 → 附近客户端：一次施法的粒子（纯表现）。</summary>
    SpellParticles = 13,

    /// <summary>服务端 → 附近客户端：一个语义音效（纯表现）。</summary>
    SpellSound = 14,

    /// <summary>
    /// 服务端 → 被法术作用的那个玩家的客户端：推一下（加速度）或传送到某处。
    /// 泰拉的玩家移动由**自己的客户端**说了算，服务端改 velocity / position 不会生效。
    /// </summary>
    PlayerMotion = 15,

    /// <summary>服务端 → 被作用的玩家客户端：加一个 buff（buff 归玩家客户端管，服务端加了不算）。</summary>
    PlayerBuff = 16,

    /// <summary>
    /// 服务端 → 那个玩家的客户端：动玩家自己的东西（扣血、丢手持物品、溺水、掉背包、扣背包格子、进度标记）。
    /// 这些由本人客户端做主，服务端改了会被忽略，见 Content/PlayerEffects.cs。
    /// </summary>
    OwnerEffect = 17,

    /// <summary>
    /// 双向：玩家存档里的咒法学状态（进度、哨卫、配色）。客户端进服 / 本地改动时报给服务端，
    /// 服务端施法改了之后发给所有人。见 HexPlayer.HandleState。
    /// </summary>
    PlayerState = 18,
}

/// <summary>
/// iota 的紧凑二进制编解码。
///
/// 为什么不用 <see cref="IotaSerializer"/> 的信封：那套格式是为
/// `TagCompound`（存档）设计的，走网络会有大量装箱与字符串开销。
/// 网络侧用字节级编码更省，也更适合每画一条图案就同步一次的高频场景。
///
/// 种类编号与 <see cref="IotaSerializer"/> 保持一致，便于排查。
/// </summary>
internal static class IotaWire
{
    private const byte KindNull = 0;
    private const byte KindGarbage = 1;
    private const byte KindBool = 2;
    private const byte KindDouble = 3;
    private const byte KindVec = 4;
    private const byte KindEntity = 5;
    private const byte KindPattern = 6;
    private const byte KindList = 7;

    public static void Write(BinaryWriter w, Iota iota)
    {
        switch (iota)
        {
            case NullIota:
                w.Write(KindNull);
                break;

            case GarbageIota:
                w.Write(KindGarbage);
                break;

            case BooleanIota b:
                w.Write(KindBool);
                w.Write(b.Value);
                break;

            case DoubleIota d:
                w.Write(KindDouble);
                w.Write(d.Value);
                break;

            case VectorIota v:
                w.Write(KindVec);
                w.Write(v.X);
                w.Write(v.Y);
                w.Write(v.Z);
                break;

            case EntityIota e:
                w.Write(KindEntity);
                w.Write((byte)e.Target);
                w.Write(e.Index);
                break;

            case PatternIota p:
                w.Write(KindPattern);
                w.Write(p.Pattern.AnglesSignature());
                w.Write((byte)p.Pattern.StartDir);
                break;

            case ListIota l:
                w.Write(KindList);
                // 列表长度上限：泰拉包大小有限，而且正常法术不会有几千项的列表。
                // 超出就截断并明确记成垃圾位 —— 宁可少几项，也不要把包撑爆导致断连。
                int count = System.Math.Min(l.Count, 1024);
                w.Write((ushort)count);
                for (int i = 0; i < count; i++)
                {
                    Write(w, l.Items[i]);
                }
                break;

            case ContinuationIota:
                // 续延含帧栈，无法过网。写成垃圾位而不是抛异常 ——
                // 抛异常会让整条法术在联机下直接失败，而单机是好的，很难查。
                w.Write(KindGarbage);
                break;

            default:
                w.Write(KindGarbage);
                break;
        }
    }

    public static Iota Read(BinaryReader r)
    {
        byte kind = r.ReadByte();
        switch (kind)
        {
            case KindNull:
                return NullIota.Instance;

            case KindGarbage:
                return GarbageIota.Instance;

            case KindBool:
                return BooleanIota.Of(r.ReadBoolean());

            case KindDouble:
                return new DoubleIota(r.ReadDouble());

            case KindVec:
                return new VectorIota(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());

            case KindEntity:
            {
                byte t = r.ReadByte();
                int idx = r.ReadInt32();
                if (t > (byte)EntityIota.EntityKind.Item) return GarbageIota.Instance;
                return new EntityIota((EntityIota.EntityKind)t, idx);
            }

            case KindPattern:
            {
                string angles = r.ReadString();
                byte dir = r.ReadByte();
                if (dir > (byte)Core.Casting.Math.HexDir.NorthWest) return GarbageIota.Instance;
                if (!Core.Casting.Math.HexPattern.TryFromAnglesUnchecked(
                        angles, (Core.Casting.Math.HexDir)dir, out var parsed, out _) || parsed == null)
                {
                    return GarbageIota.Instance;
                }
                return new PatternIota(parsed);
            }

            case KindList:
            {
                int count = r.ReadUInt16();
                var items = new List<Iota>(count);
                for (int i = 0; i < count; i++)
                {
                    items.Add(Read(r));
                }
                return new ListIota(items);
            }

            default:
                // 未知种类：一律当垃圾位，不抛异常（避免版本不一致时直接断连）
                return GarbageIota.Instance;
        }
    }

    /// <summary>写一个图案（不经过 iota 包装）。</summary>
    public static void WritePattern(BinaryWriter w, Core.Casting.Math.HexPattern pattern)
    {
        w.Write(pattern.AnglesSignature());
        w.Write((byte)pattern.StartDir);
    }

    /// <summary>读一个图案。失败返回 null。</summary>
    public static Core.Casting.Math.HexPattern? ReadPattern(BinaryReader r)
    {
        string angles = r.ReadString();
        byte dir = r.ReadByte();
        if (dir > (byte)Core.Casting.Math.HexDir.NorthWest) return null;
        if (!Core.Casting.Math.HexPattern.TryFromAnglesUnchecked(
                angles, (Core.Casting.Math.HexDir)dir, out var parsed, out _) || parsed == null)
        {
            return null;
        }
        return parsed;
    }
}
