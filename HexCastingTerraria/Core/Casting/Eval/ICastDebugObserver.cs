namespace HexCastingTerraria.Core.Casting.Eval;

/// <summary>
/// 附属调试器（HexDebug）在施法环境上的挂点。本体只在三处通知它（上游 HexDebug 用 mixin 注入的同样三处）：
/// 赫尔墨斯 / 托特之策略的求值（OpEval.exec）、揭示等打印的消息（printMessage）、事故消息（sendMishapMsgToPlayer）。
/// 没在调试时为 null，本体行为不变。
/// </summary>
public interface ICastDebugObserver
{
    /// <summary>OpEval.Exec 被调用了（上游 MixinOpEval：lastEvaluatedAction = OpEval、lastDebugStepType = IN）。</summary>
    void OnEval();

    /// <summary>给施法者打印了一条消息。</summary>
    void OnPrint(string message);

    /// <summary>给施法者发了一条事故消息。</summary>
    void OnMishap(string message);
}
