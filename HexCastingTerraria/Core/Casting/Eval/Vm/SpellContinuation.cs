namespace HexCastingTerraria.Core.Casting.Eval.Vm;

/// <summary>
/// 施法过程中的续延（帧栈）。
/// 移植自 at.petrak.hexcasting.api.casting.eval.vm.SpellContinuation。
///
/// 结构：单向链表，头部是最内层（即将执行的）帧。
/// </summary>
public abstract class SpellContinuation
{
    /// <summary>没有更多要执行的东西。</summary>
    public sealed class Done : SpellContinuation
    {
        public static readonly Done Instance = new();
        private Done() { }
    }

    /// <summary>还有帧要执行：当前帧 + 剩余续延。</summary>
    public sealed class NotDone : SpellContinuation
    {
        public IContinuationFrame Frame { get; }
        public SpellContinuation Next { get; }

        public NotDone(IContinuationFrame frame, SpellContinuation next)
        {
            Frame = frame;
            Next = next;
        }
    }

    /// <summary>把一帧压到栈顶，返回新续延（持久化，不修改自身）。</summary>
    public SpellContinuation PushFrame(IContinuationFrame frame) => new NotDone(frame, this);

    public static SpellContinuation Start() => Done.Instance;
}
