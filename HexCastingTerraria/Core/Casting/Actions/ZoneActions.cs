using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>区域查询的筛选种类。对应源项目 `OpGetEntitiesBy` 的五个谓词。</summary>
public enum ZoneEntityFilter
{
    /// <summary>被动生物。泰拉侧 = `npc.CountsAsACritter`。</summary>
    Animal = 0,

    /// <summary>敌对生物。</summary>
    Monster = 1,

    /// <summary>掉在地上的物品。</summary>
    Item = 2,

    /// <summary>玩家。</summary>
    Player = 3,

    /// <summary>活物（玩家或 NPC）。</summary>
    Living = 4,

    /// <summary>任意实体（源项目 `zone_entity` 的 `e -> true`：玩家、NPC、掉落物、弹幕都算）。</summary>
    Any = 5,
}

/// <summary>
/// `zone_entity/*`：查询一个球形（2D 下是圆形）区域内的实体。
/// 移植自源项目 selectors/OpGetEntitiesBy.kt。
///
/// 参数是 (中心坐标, 半径)。返回一个**列表**，按距离**从近到远**排序。
///
/// ⚠️ 源项目注释里专门写了一条：过滤条件必须包含「在施法范围内」，
/// 理由是修复 issue #792 ——
/// *Ignore truename ambit to fix #792 so you can't slurp up all players in the whole world*
/// （不加范围限制就能一次把全世界的玩家全选中）。
/// 泰拉侧的 `QueryEntities` 同样做了这个过滤。
/// </summary>
public sealed class OpGetEntitiesBy : ConstMediaAction
{
    private readonly ZoneEntityFilter _filter;
    private readonly bool _negate;

    public OpGetEntitiesBy(ZoneEntityFilter filter, bool negate)
    {
        _filter = filter;
        _negate = negate;
    }

    public override int Argc => 2;

    /// <summary>
    /// 免费。源项目 OpGetEntitiesBy **没有覆写** mediaCost，ConstMediaAction 的默认值是 0。
    /// （这里曾写「源项目 DUST_UNIT，一次 1 粉尘」—— 与原版不符）
    /// </summary>
    public override long MediaCost => 0;

    public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y) = CastingEnvironment.RequireVec(args[0], "区域中心");

        // 源项目 getPositiveDouble：0 <= x（**含 0**）
        double radius = CastingEnvironment.RequirePositiveDouble(args[1], "非负半径");

        env.AssertVecInRange(x, y);

        var world = env.RequireWorld();

        // 筛选与生物分类都交给世界侧 ——
        // Core 层拿不到 Terraria 的 `NPC.CountsAsACritter` / `friendly` 等字段。
        // （早先写过一个静态的分类缓存，那会在不同施法之间串数据，已废弃。）
        var found = world.QueryEntities(_filter, _negate, x, y, radius);

        var result = new List<Iota>(found.Count);
        foreach (var entity in found)
        {
            result.Add(entity);
        }

        return new Iota[] { new ListIota(result) };
    }

}

/// <summary>区域查询图案的注册。</summary>
public static class ZoneActions
{
    public static int Register()
    {
        int before = PatternRegistry.RegisteredActionCount;

        // 「任意」：源项目 e -> true（这里曾经注册成「生物」过滤）
        Register("hexcasting:zone_entity", ZoneEntityFilter.Any, negate: false);
        Register("hexcasting:zone_entity/animal", ZoneEntityFilter.Animal, negate: false);
        Register("hexcasting:zone_entity/not_animal", ZoneEntityFilter.Animal, negate: true);
        Register("hexcasting:zone_entity/monster", ZoneEntityFilter.Monster, negate: false);
        Register("hexcasting:zone_entity/not_monster", ZoneEntityFilter.Monster, negate: true);
        Register("hexcasting:zone_entity/item", ZoneEntityFilter.Item, negate: false);
        Register("hexcasting:zone_entity/not_item", ZoneEntityFilter.Item, negate: true);
        Register("hexcasting:zone_entity/player", ZoneEntityFilter.Player, negate: false);
        Register("hexcasting:zone_entity/not_player", ZoneEntityFilter.Player, negate: true);
        Register("hexcasting:zone_entity/living", ZoneEntityFilter.Living, negate: false);
        Register("hexcasting:zone_entity/not_living", ZoneEntityFilter.Living, negate: true);

        return PatternRegistry.RegisteredActionCount - before;
    }

    private static void Register(string id, ZoneEntityFilter filter, bool negate)
        => PatternRegistry.RegisterAction(id, new OpGetEntitiesBy(filter, negate));
}
