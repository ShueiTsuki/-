using System.Collections.Generic;
using HexCastingTerraria.Addons.HexDebug.Core;
using HexCastingTerraria.Content.Tiles;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace HexCastingTerraria.Addons.HexDebug.Game;

/// <summary>
/// 调试法术环（上游 debugger/circles/CircleDebugEnv.kt + MixinBlockEntityAbstractImpetus / MixinCircleExecutionState / MixinBlockSlate）：
/// 拿调试杖对着促动石用，启动这个环并在每块石板上停下来，用调试杖一步步走石板上的图案；走完一块，环接着走到下一块。
/// </summary>
internal sealed class CircleDebugEnv : DebugEnvironment, ICircleDebugHook
{
    private readonly Player _caster;
    private readonly HexImpetusEntity _impetus;
    private CastingImage? _newImage;

    public CircleDebugEnv(Player caster, HexImpetusEntity impetus, int threadId)
    {
        _caster = caster;
        _impetus = impetus;
        ThreadId = threadId;
    }

    public int ThreadId { get; }

    public bool IsPaused { get; private set; }

    public override string Name => Lang.GetMapObjectName(Main.Map[_impetus.Position.X, _impetus.Position.Y].Type) is { Length: > 0 } n
        ? n
        : Terraria.Localization.Language.GetTextValue("Mods.HexCastingTerraria.HexDebug.CircleName");

    /// <summary>上游：施法者离促动石不超过影响范围（32 格）。</summary>
    public override bool IsCasterInRange
        => _caster is { active: true, dead: false }
           && Vector2.Distance(_caster.Center, new Vector2(_impetus.Position.X * 16 + 8, _impetus.Position.Y * 16 + 8)) <= 32 * 16;

    /// <summary>上游 resume：这块石板顺利跑完就把栈交回环，环接着走（还挂着这个调试就继续算在调试中）。</summary>
    public override bool Resume(CastingEnvironment env, CastingImage image, ResolvedPatternType resolutionType)
    {
        if (!resolutionType.IsSuccess()) return false;
        IsPaused = false;
        _newImage = image;
        return _impetus.IsRunning && ReferenceEquals(_impetus.DebugHook, this);
    }

    public override void Restart(int threadId)
    {
        Terminate();
        CircleDebugging.Start(_caster, _impetus, threadId);
    }

    /// <summary>上游 terminate：停掉环（endExecution）。</summary>
    public override void Terminate()
    {
        if (ReferenceEquals(_impetus.DebugHook, this)) _impetus.StopExecution();
    }

    // ---- 促动石那边的挂点 ----

    public void OnSlate(HexImpetusEntity impetus, CastingEnvironment env, CastingImage image, HexPattern? pattern)
    {
        IsPaused = true;
        var iotas = pattern is null ? new List<Iota>() : new List<Iota> { new PatternIota(pattern) };
        HexDebugSessions.StartDebuggingIotas(_caster, this, env, iotas, image);
    }

    public CastingImage? TakeNewImage()
    {
        var img = _newImage;
        _newImage = null;
        return img;
    }

    /// <summary>上游 endExecution 时 removeDebugThread。</summary>
    public void OnEnd() => HexDebugSessions.RemoveThread(_caster.whoAmI, ThreadId, terminate: false);
}

internal static class CircleDebugging
{
    /// <summary>上游 startDebugging：环已经在跑就不管；启动不了（没闭合等）返回 false。</summary>
    public static bool Start(Player caster, HexImpetusEntity impetus, int threadId)
    {
        if (impetus.IsRunning) return false;
        if (!HexDebugSessions.CreateThread(caster, threadId, () => new CircleDebugEnv(caster, impetus, threadId), out var env)) return false;
        impetus.TryStart(caster);
        if (!impetus.IsRunning)
        {
            HexDebugSessions.RemoveThread(caster.whoAmI, threadId, terminate: false);
            return false;
        }
        impetus.DebugHook = (CircleDebugEnv)env;
        return true;
    }

    /// <summary>鼠标指着的格子上的促动石（够得着才算）。</summary>
    public static HexImpetusEntity? ImpetusAt(Player player, int x, int y)
    {
        if (!Terraria.DataStructures.TileEntity.ByPosition.TryGetValue(new Terraria.DataStructures.Point16(x, y), out var te)) return null;
        if (te is not HexImpetusEntity impetus) return null;
        return Vector2.Distance(player.Center, new Vector2(x * 16 + 8, y * 16 + 8)) <= 16 * 8 ? impetus : null;
    }
}
