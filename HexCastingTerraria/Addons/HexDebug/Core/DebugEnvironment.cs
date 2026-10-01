using System;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Vm;

namespace HexCastingTerraria.Addons.HexDebug.Core;

/// <summary>调试输出的类别（上游 OutputCategory：普通输出 / 错误）。</summary>
public enum OutputCategory
{
    Stdout,
    Stderr,
}

/// <summary>
/// 一次调试的来历（上游 core api DebugEnvironment）：谁在调试、结束后能不能接着跑（法术环）、怎么重启。
/// 同时挂在施法环境上接本体的三处通知（<see cref="ICastDebugObserver"/>）。
/// </summary>
public abstract class DebugEnvironment : ICastDebugObserver
{
    public Guid SessionId { get; } = Guid.NewGuid();

    /// <summary>上一步里本体报告的步进类型（赫尔墨斯 / 托特之策略 = 进入）。</summary>
    public DebugStepType? LastDebugStepType { get; set; }

    /// <summary>上一步执行的是不是 OpEval（上游 lastEvaluatedAction is OpEval，用来补尾调用的虚拟帧）。</summary>
    public bool LastEvaluatedWasEval { get; set; }

    /// <summary>调试器打印输出（游戏侧接到调试面板的输出区）：文字、类别、是否带源码位置。</summary>
    public Action<string, OutputCategory, bool>? Output { get; set; }

    /// <summary>上游 resume：跑完了还要不要接着跑下一段（法术环）；玩家用调试杖时永远是 false。</summary>
    public abstract bool Resume(CastingEnvironment env, CastingImage image, ResolvedPatternType resolutionType);

    public abstract void Restart(int threadId);

    public abstract void Terminate();

    public abstract bool IsCasterInRange { get; }

    /// <summary>上游 getName：调试杖的名字，显示在线程旁边。</summary>
    public abstract string Name { get; }

    public virtual void PostStep(CastingEnvironment env, CastingImage image, StopReason? reason) { }

    public void PrintDebugMessage(string message, OutputCategory category = OutputCategory.Stdout, bool withSource = true)
        => Output?.Invoke(message, category, withSource);

    void ICastDebugObserver.OnEval()
    {
        LastEvaluatedWasEval = true;
        LastDebugStepType = DebugStepType.In;
    }

    void ICastDebugObserver.OnPrint(string message) => PrintDebugMessage(message);

    void ICastDebugObserver.OnMishap(string message) => PrintDebugMessage(message, OutputCategory.Stderr);
}
