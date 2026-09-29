namespace HexCastingTerraria.Core.Casting.Eval.SideEffects;

/// <summary>
/// 求值音效种类。移植自 at.petrak.hexcasting.api.casting.eval.sideeffects.EvalSound。
/// </summary>
public enum EvalSound
{
    NormalExecute = 0,
    Hermes = 1,
    Thoth = 2,
    Mishap = 3,
    Nothing = 4,
    /// <summary>OpHalt 使用（对应源项目 HexEvalSounds.SPELL）。</summary>
    Spell = 5,

    /// <summary>
    /// 被静音的执行。对应源项目 MUTE。
    /// 用于 hasCastingSound() == false 的法术图案 —— 它们不发声。
    /// </summary>
    Mute = 6,
}
