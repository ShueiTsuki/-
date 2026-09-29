using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Registry;

namespace HexCastingTerraria.Core.Casting.Actions;

/// <summary>
/// `brainsweep`：脑叶切除 —— 把一只生物「处理」掉，换来一块更高级的方块。
/// 移植自源项目 `OpBrainsweep`（大法术，需要启蒙）。
///
/// 参数：`(生物, 位置)`。位置决定用哪条配方，生物决定配不配得上。
///
/// ⚠️ **消耗来自配方，不是写死的**：源项目 `recipe.mediaCost`。
/// 这是它与其他法术最不一样的地方 —— 加一条新配方就多一种价格，
/// 所以这里的 `SpellResult.Cost` 必须从匹配到的配方里取。
///
/// 三条校验顺序也照抄源码：先看位置能不能改、再看生物能不能切、最后看有没有配方。
/// 顺序反了会给出**误导性的报错**（比如「这里不能改」出现在「这只生物不能切」之前，
/// 玩家会去检查方块而不是生物）。
/// </summary>
public sealed class OpBrainsweep : SpellAction
{
    public override int Argc => 2;

    public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var target = env.ResolveEntity(args[0]);
        var (x, y, z) = CastingEnvironment.RequireVec3(args[1], "位置");
        env.AssertVecInRange(x, y, z);

        var world = env.RequireWorld();

        // ① 位置：世界内、可编辑
        if (!world.CanEditAt(x, y))
        {
            throw new MishapBadLocation(x, y, "这里不能动");
        }

        // ② 生物：得是「可以切」的那类
        if (!world.IsBrainsweepable(target))
        {
            throw new MishapBadBrainsweep(target, x, y);
        }

        // ③ 不能切第二次
        if (world.IsBrainswept(target))
        {
            throw new MishapAlreadyBrainswept(target);
        }

        // ④ 配方：方块 + 生物种类
        int tile = world.TileTypeAt(x, y);
        int species = world.EntitySpeciesOf(target);
        if (!BrainsweepRules.TryFind(tile, species, out var recipe))
        {
            throw new MishapBadBrainsweep(target, x, y);
        }

        return WorldSpell.Make(
            new WorldSpell.Simple(w => w.Brainsweep(x, y, target, recipe)),
            recipe.MediaCost,
            new[]
            {
                ParticleSpray.Cloud(world.FeetPosition(target).X, world.FeetPosition(target).Y, spread: 1.0f, count: 40),
                ParticleSpray.Burst(x, y, spread: 0.3f, count: 100),
            });
    }

    /// <summary>音频不盖过受害者的死亡音效（源项目 `hasCastingSound = false`，注释写得很直白）。</summary>
    public override bool HasCastingSound(CastingEnvironment env) => false;
}
