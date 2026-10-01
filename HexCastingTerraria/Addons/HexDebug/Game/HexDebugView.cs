using System.Collections.Generic;
using System.IO;
using HexCastingTerraria.Addons.HexDebug.Core;

namespace HexCastingTerraria.Addons.HexDebug.Game;

/// <summary>
/// 一个调试线程此刻的样子，服务端算好发给本人客户端的游戏内调试面板。
/// 上游这些信息通过 DAP 交给外部编辑器（栈、渡鸦之思、状态三项、调用栈、源码与当前行、断点）。
/// </summary>
public sealed class HexDebugView
{
    public int ThreadId;
    public string Name = "";
    public DebuggerState State;
    public StopReason? Reason;

    /// <summary>当前停的位置所在的源码（编号、逐行文字、当前行、已设断点的行）。</summary>
    public int SourceRef;
    public List<string> SourceLines = new();
    public int CurrentLine = -1;
    public List<int> BreakpointLines = new();

    /// <summary>栈（栈顶在前）、渡鸦之思、上游 State 作用域的三项。</summary>
    public List<string> Stack = new();
    public string Ravenmind = "";
    public long OpsConsumed;
    public bool EscapeNext;
    public int ParenCount;

    /// <summary>调用栈（当前帧在前）：帧名与「源码#编号:行」。</summary>
    public List<(string Name, string Where, bool Virtual)> Frames = new();

    /// <summary>运行杖画过东西了（上游 EvalState.MODIFIED，物品上显示）。</summary>
    public bool EvaluatorModified;

    public void Write(BinaryWriter w)
    {
        w.Write((byte)ThreadId);
        w.Write(Name);
        w.Write((byte)State);
        w.Write(Reason.HasValue);
        if (Reason is { } r) w.Write((byte)r);
        w.Write(SourceRef);
        WriteList(w, SourceLines);
        w.Write(CurrentLine);
        w.Write((ushort)BreakpointLines.Count);
        foreach (var l in BreakpointLines) w.Write(l);
        WriteList(w, Stack);
        w.Write(Ravenmind);
        w.Write(OpsConsumed);
        w.Write(EscapeNext);
        w.Write(ParenCount);
        w.Write((ushort)Frames.Count);
        foreach (var (name, where, v) in Frames)
        {
            w.Write(name);
            w.Write(where);
            w.Write(v);
        }
        w.Write(EvaluatorModified);
    }

    public static HexDebugView Read(BinaryReader r)
    {
        var v = new HexDebugView
        {
            ThreadId = r.ReadByte(),
            Name = r.ReadString(),
            State = (DebuggerState)r.ReadByte(),
        };
        if (r.ReadBoolean()) v.Reason = (StopReason)r.ReadByte();
        v.SourceRef = r.ReadInt32();
        v.SourceLines = ReadList(r);
        v.CurrentLine = r.ReadInt32();
        int nb = r.ReadUInt16();
        for (int i = 0; i < nb; i++) v.BreakpointLines.Add(r.ReadInt32());
        v.Stack = ReadList(r);
        v.Ravenmind = r.ReadString();
        v.OpsConsumed = r.ReadInt64();
        v.EscapeNext = r.ReadBoolean();
        v.ParenCount = r.ReadInt32();
        int nf = r.ReadUInt16();
        for (int i = 0; i < nf; i++) v.Frames.Add((r.ReadString(), r.ReadString(), r.ReadBoolean()));
        v.EvaluatorModified = r.ReadBoolean();
        return v;
    }

    private static void WriteList(BinaryWriter w, List<string> list)
    {
        w.Write((ushort)list.Count);
        foreach (var s in list) w.Write(s);
    }

    private static List<string> ReadList(BinaryReader r)
    {
        int n = r.ReadUInt16();
        var list = new List<string>(n);
        for (int i = 0; i < n; i++) list.Add(r.ReadString());
        return list;
    }
}
