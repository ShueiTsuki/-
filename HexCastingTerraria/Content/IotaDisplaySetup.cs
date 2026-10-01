using HexCastingTerraria.Core.Casting.Iotas;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content;

/// <summary>
/// 给 Core 的 iota 显示接上游戏：实体叫什么（上游 EntityIota 显示实体名）、Shift 按没按着（HexParse 的注释按住 Shift 不显示）。
/// </summary>
public sealed class IotaDisplaySetup : ModSystem
{
    public override void Load()
    {
        IotaDisplay.EntityName = NameOf;
        IotaDisplay.ShiftDown = () => !Main.dedServ
            && (Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift));
    }

    public override void Unload()
    {
        IotaDisplay.EntityName = null;
        IotaDisplay.ShiftDown = null;
    }

    /// <summary>实体还在就给它的名字；不在了返回 null（显示「未知实体」）。</summary>
    private static string? NameOf(EntityIota e)
    {
        int i = e.Index;
        switch (e.Target)
        {
            case EntityIota.EntityKind.Player:
                return i >= 0 && i < Main.maxPlayers && Main.player[i] is { active: true } p ? p.name : null;
            case EntityIota.EntityKind.Npc:
                return i >= 0 && i < Main.maxNPCs && Main.npc[i] is { active: true } n ? n.GivenOrTypeName : null;
            case EntityIota.EntityKind.Projectile:
                return i >= 0 && i < Main.maxProjectiles && Main.projectile[i] is { active: true } pr ? pr.Name : null;
            case EntityIota.EntityKind.Item:
                return i >= 0 && i < Main.maxItems && Main.item[i] is { active: true } it ? it.Name : null;
            default:
                return null;
        }
    }
}
