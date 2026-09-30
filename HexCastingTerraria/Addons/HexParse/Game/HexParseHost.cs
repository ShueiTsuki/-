using HexCastingTerraria.Addons.HexParse.Core;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace HexCastingTerraria.Addons.HexParse.Game;

/// <summary>
/// 解析器在游戏里的宿主（<see cref="IHexParseHost"/>）：施法者是谁、大法术解锁表、宏、实体、消息往哪发。
/// 客户端和服务端都能建：消息在本机就直接显示，在服务端就发给那个玩家。
/// </summary>
public sealed class HexParseHost : IHexParseHost
{
    private readonly Player _player;

    public HexParseHost(Player player) => _player = player;

    public Player Player => _player;

    public EntityIota? Self => new(EntityIota.EntityKind.Player, _player.whoAmI);

    public string AuthorName => _player.name;

    public bool IsGreatUnlocked(string longId) => HexParseWorld.IsUnlocked(longId);

    public string? ManualShortName(string shortName) => HexParseWorld.ManualShortName(shortName);

    /// <summary>宏 / 别名存在客户端（上游也是客户端保存）；服务端没有宏。</summary>
    public string? GetMacro(string key) => Main.netMode != NetmodeID.Server ? HexParseMacros.Get(key) : null;

    /// <summary>
    /// 泰拉偏差：没有 UUID。写法 entity_player_编号 / entity_npc_编号 / entity_item_编号 / entity_projectile_编号。
    /// 别的玩家的真名 = 事故「他人之名」（上游 MishapOthersName，这里报错不写入）；找不到 / 已经没了 = null。
    /// </summary>
    public Iota? ResolveEntity(string node)
    {
        var parts = node.Split('_');
        if (parts.Length != 3 || !int.TryParse(parts[2], out int index)) return null;
        switch (parts[1])
        {
            case "player":
                if (index < 0 || index >= Main.maxPlayers || Main.player[index] is not { active: true } other) return null;
                if (index != _player.whoAmI) throw new HexParseException($"试图侵犯{other.name}的灵魂的隐私");   // 原版 mishap.others_name 的官方中文
                return new EntityIota(EntityIota.EntityKind.Player, index);
            case "npc":
                return index >= 0 && index < Main.maxNPCs && Main.npc[index].active ? new EntityIota(EntityIota.EntityKind.Npc, index) : null;
            case "item":
                return index >= 0 && index < Main.maxItems && Main.item[index].active ? new EntityIota(EntityIota.EntityKind.Item, index) : null;
            case "projectile":
                return index >= 0 && index < Main.maxProjectiles && Main.projectile[index].active ? new EntityIota(EntityIota.EntityKind.Projectile, index) : null;
            default:
                return null;
        }
    }

    public string EntityToCode(EntityIota entity) => entity.Target switch
    {
        EntityIota.EntityKind.Player => "entity_player_" + entity.Index,
        EntityIota.EntityKind.Npc => "entity_npc_" + entity.Index,
        EntityIota.EntityKind.Item => "entity_item_" + entity.Index,
        _ => "entity_projectile_" + entity.Index,
    };

    public void Message(string text, HexParseMessageKind kind)
    {
        if (Main.netMode == NetmodeID.Server) HexParseNet.SendMessage(_player.whoAmI, text, kind);
        else Show(text, kind);
    }

    /// <summary>本机显示一条 HexParse 消息（上游的聊天颜色：警告金色、错误深红）。</summary>
    public static void Show(string text, HexParseMessageKind kind)
        => Main.NewText(text, kind switch
        {
            HexParseMessageKind.Warning => new Color(255, 170, 0),
            HexParseMessageKind.Error => new Color(170, 0, 0),
            _ => new Color(85, 255, 85),
        });

    /// <summary>当前世界的图案名表（本体 + 开着的附属；大法术用本世界画法；短名冲突用世界里存的指定）。</summary>
    public static PatternNames Names() => PatternNames.Build(PatternRegistry.PatternInThisWorld, HexParseWorld.ManualShortName);

    public CodeParser NewParser() => new(this, HexParseSettings.Current, Names());

    public IotaWriter NewWriter() => new(this, HexParseSettings.Current);
}
