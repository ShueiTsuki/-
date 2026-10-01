using HexCastingTerraria.Config;
using HexCastingTerraria.Content;
using HexCastingTerraria.Content.Items;
using HexCastingTerraria.Core.Casting.Eval;
using Terraria;

namespace HexCastingTerraria.Addons.HexDebug.Game.Splicing;

/// <summary>
/// 制念台施放融注咒术时的施法环境（上游 casting/eval/SplicingTableCastEnv.kt）：施法者还是按下按钮的玩家，
/// 但施法范围以制念台为心（半径见配置，大哨卫照样延伸），媒质先用台子里存的、再直接从媒质槽的物品里抽。
/// </summary>
public sealed class SplicingTableCastEnv : PlayerCastingEnvironment
{
    private readonly SplicingTableEntity _table;
    private ICastingWorld? _world;

    public SplicingTableCastEnv(Player player, SplicingTableEntity table) : base(player) => _table = table;

    public override ICastingWorld World => _world ??= new HexSpaceWorld(new TerrariaCastingWorld(Player)
    {
        Ambit = (_table.Position.X + 0.5, _table.Position.Y + 0.5, HexAddonsConfig.Instance.HexDebugOptions.SplicingTableAmbit),
    });

    protected override long ExtractMediaEnvironment(long cost, bool simulate)
    {
        long left = _table.WithdrawForCast(cost, simulate);
        if (left <= 0) return 0;
        var item = _table.Slots[SplicingTableEntity.SlotMedia];
        if (item.ModItem is MediaFlask flask)
        {
            long have = System.Math.Min(left, flask.Media);
            if (!simulate) flask.Withdraw(have);
            left -= have;
        }
        else if (MediaItems.TryGet(item, out long unit, out _))
        {
            // 原版：静态媒质物品按整个扣，多出来的浪费掉
            int need = (int)System.Math.Min(item.stack, (left + unit - 1) / unit);
            if (!simulate)
            {
                item.stack -= need;
                if (item.stack <= 0) item.TurnToAir();
            }
            left -= need * unit;
        }
        return System.Math.Max(0, left);
    }
}
