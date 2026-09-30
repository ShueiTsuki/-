using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// `colorize`：拿一份**颜料**，把自己之后所有法术的配色换成它。
/// 移植自源项目 `OpColorize`。
///
/// 参数：无。颜料是从施法者身上找的（源项目查副手的 `isPigment`）。
/// 消耗 `DUST_UNIT`（1 万）—— 一次性收费，之后换色是永久的（除非再拿一管别的）。
///
/// 注意：泰拉侧的映射：源项目的颜料是模组自己的一套物品；
/// 泰拉有现成的**染料**，语义完全一致（给东西上色），所以直接用它。
/// 只有 13 种基础染料有单一颜色，特殊染料（火焰/渐变/彩虹）会被拒绝 ——
/// 给它们硬套一个颜色，玩家看到的会是「明明拿了彩虹染料却是紫色」。
/// </summary>
public sealed class OpColorize : SpellAction
{
    public override int Argc => 0;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        int pigment = env.FindPigmentItem();
        if (pigment == 0)
        {
            throw new MishapBadHeldItem(MishapBadHeldItem.Need.Colorizer);
        }

        return EnvSpell.Make(
            new EnvSpell.Simple(castEnv => castEnv.ApplyPigment(pigment)),
            MediaConstants.DustUnit);
    }
}
