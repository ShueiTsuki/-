using System.Collections.Generic;
using System.IO;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Content;

/// <summary>
/// 大法术「每个世界的笔顺」的存档与同步（源项目 ScrungledPatternsSave）。
///
///   - 新世界：按世界种子生成（<see cref="PatternRegistry.GeneratePerWorld"/>），并**存进世界存档** ——
///     与源项目一样，存下来是为了算法以后改了也不影响老世界；
///   - 联机：随世界数据同步给客户端（客户端要用它识别画出来的图案、画古卷）；
///   - 退出世界：恢复标准笔顺（主菜单里的书、离线用例都用标准笔顺）。
/// </summary>
public sealed class PerWorldPatternSystem : ModSystem
{
    private bool _loadedFromSave;

    public override void ClearWorld()
    {
        _loadedFromSave = false;
        PatternRegistry.ResetPerWorldToCanonical();
    }

    public override void OnWorldLoad()
    {
        if (_loadedFromSave || Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;
        PatternRegistry.SetPerWorld(PatternRegistry.GeneratePerWorld(Main.ActiveWorldFileData.Seed));
    }

    public override void OnWorldUnload() => PatternRegistry.ResetPerWorldToCanonical();

    public override void SaveWorldData(TagCompound tag)
    {
        var list = new List<TagCompound>();
        foreach (var (id, pattern) in PatternRegistry.PerWorldTable)
        {
            list.Add(new TagCompound { ["id"] = id, ["dir"] = (byte)pattern.StartDir, ["angles"] = pattern.AnglesSignature() });
        }
        tag["hexPerWorldPatterns"] = list;
    }

    public override void LoadWorldData(TagCompound tag)
    {
        if (!tag.ContainsKey("hexPerWorldPatterns")) return;
        var table = new Dictionary<string, HexPattern>();
        foreach (var t in tag.GetList<TagCompound>("hexPerWorldPatterns"))
        {
            if (HexPattern.TryFromAnglesUnchecked(t.GetString("angles"), (HexDir)t.GetByte("dir"), out var p, out _) && p != null)
            {
                table[t.GetString("id")] = p;
            }
        }
        if (table.Count > 0)
        {
            PatternRegistry.SetPerWorld(table);
            _loadedFromSave = true;
        }
    }

    public override void NetSend(BinaryWriter writer)
    {
        var table = PatternRegistry.PerWorldTable;
        writer.Write((byte)table.Count);
        foreach (var (id, pattern) in table)
        {
            writer.Write(id);
            writer.Write((byte)pattern.StartDir);
            writer.Write(pattern.AnglesSignature());
        }
    }

    public override void NetReceive(BinaryReader reader)
    {
        int n = reader.ReadByte();
        var table = new Dictionary<string, HexPattern>();
        for (int i = 0; i < n; i++)
        {
            string id = reader.ReadString();
            var dir = (HexDir)reader.ReadByte();
            string angles = reader.ReadString();
            if (HexPattern.TryFromAnglesUnchecked(angles, dir, out var p, out _) && p != null) table[id] = p;
        }
        PatternRegistry.SetPerWorld(table);
    }
}
