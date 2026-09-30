using System.Collections.Generic;
using System.IO;
using System.Linq;
using HexCastingTerraria.Core.Registry;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Addons.HexParse.Game;

/// <summary>
/// HexParse 存在世界里的东西：
/// - 大法术解锁表（上游 hooks/GreatPatternUnlocker.java，SavedData「hexparse.great.pattern.unlocks」）：存长 id
/// - 短名称的手动指定（上游 PatternMapper.ShortNameTrackerPersistent）：短名 -> 长 id
///
/// 解析在客户端做（物品、剪贴板、宏都在客户端），所以两张表都要同步给客户端：
/// 进服时 ModSystem.NetSend 带一份；服务端改了（学大法术、unlock_great、conflict set）整份重发。
/// HexParse 关着时这个系统不加载，存档里的数据由 tML 的 UnloadedSystem 原样保管。
/// </summary>
public sealed class HexParseWorld : AddonSystem
{
    public override string AddonId => "hexparse";

    private static readonly HashSet<string> Unlocked = new();
    private static readonly Dictionary<string, string> ShortNames = new();

    public static bool IsUnlocked(string longId) => Unlocked.Contains(longId);

    public static string? ManualShortName(string shortName) => ShortNames.TryGetValue(shortName, out var id) ? id : null;

    public static IReadOnlyCollection<string> UnlockedIds => Unlocked;

    /// <summary>解锁一个（长 id）。返回是否是新解锁的。只在服务端 / 单人调用，之后 <see cref="Sync"/>。</summary>
    public static bool Unlock(string longId) => Unlocked.Add(longId);

    public static bool Lock(string longId) => Unlocked.Remove(longId);

    /// <summary>上游 unlockAll：解锁全部每世界大法术。返回新解锁的个数。</summary>
    public static int UnlockAll() => PatternRegistry.All.Where(PatternRegistry.IsPerWorld).Count(d => Unlocked.Add(d.Id));

    /// <summary>上游 clear：全部锁上。返回原来解锁的个数。</summary>
    public static int LockAll()
    {
        int n = Unlocked.Count;
        Unlocked.Clear();
        return n;
    }

    public static void SetShortName(string shortName, string longId) => ShortNames[shortName] = longId;

    public override void ClearWorld()
    {
        Unlocked.Clear();
        ShortNames.Clear();
    }

    public override void SaveWorldData(TagCompound tag)
    {
        tag["unlocks"] = Unlocked.ToList();
        tag["shortNameKeys"] = ShortNames.Keys.ToList();
        tag["shortNameValues"] = ShortNames.Values.ToList();
    }

    public override void LoadWorldData(TagCompound tag)
    {
        Unlocked.Clear();
        foreach (var id in tag.GetList<string>("unlocks")) Unlocked.Add(id);
        ShortNames.Clear();
        var keys = tag.GetList<string>("shortNameKeys");
        var values = tag.GetList<string>("shortNameValues");
        for (int i = 0; i < keys.Count && i < values.Count; i++) ShortNames[keys[i]] = values[i];
    }

    public override void NetSend(BinaryWriter writer) => Write(writer);

    public override void NetReceive(BinaryReader reader) => Read(reader);

    internal static void Write(BinaryWriter w)
    {
        w.Write((ushort)Unlocked.Count);
        foreach (var id in Unlocked) w.Write(id);
        w.Write((ushort)ShortNames.Count);
        foreach (var (k, v) in ShortNames)
        {
            w.Write(k);
            w.Write(v);
        }
    }

    internal static void Read(BinaryReader r)
    {
        Unlocked.Clear();
        int n = r.ReadUInt16();
        for (int i = 0; i < n; i++) Unlocked.Add(r.ReadString());
        ShortNames.Clear();
        int m = r.ReadUInt16();
        for (int i = 0; i < m; i++) ShortNames[r.ReadString()] = r.ReadString();
    }

    /// <summary>服务端改了表之后调：整份发给所有客户端（单人什么都不做）。</summary>
    public static void Sync()
    {
        if (Main.netMode != NetmodeID.Server) return;
        var packet = HexParseNet.NewPacket(HexParseNet.Msg.WorldState);
        Write(packet);
        packet.Send();
    }
}
