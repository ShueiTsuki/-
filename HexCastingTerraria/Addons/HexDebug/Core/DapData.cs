using System.Collections.Generic;

namespace HexCastingTerraria.Addons.HexDebug.Core;

/// <summary>
/// 外部调试器（DAP，上游 lsp4j 的 Variable / Scope / StackFrame）要的数据，由 <see cref="HexDebugger"/> 生成，
/// 游戏侧的 HexDebugAdapter 再写成 JSON。引用编号都是这个线程自己的（从 1 起），打包成全局编号是适配器的事。
/// </summary>
public sealed class DapVariable
{
    public DapVariable(string name, string value, string? type = null)
    {
        Name = name;
        Value = value;
        Type = type;
    }

    public string Name { get; }

    public string Value { get; set; }

    public string? Type { get; }

    /// <summary>展开后的子变量（0 = 没有）。</summary>
    public int VariablesReference { get; set; }

    public int? IndexedVariables { get; set; }
}

public sealed record DapScope(string Name, int VariablesReference);

/// <summary>调用栈里的一帧：编号（这个线程里的，真实帧从 1 起、虚拟帧从 10 的幂起）、名字、位置；虚拟帧显示得淡一些。</summary>
public sealed record DapFrame(int Id, string Name, IotaMetadata? Meta, bool Subtle);

/// <summary>上游 allocators/VariablesAllocator：一组变量一个引用编号（1 起）。</summary>
public sealed class VariablesAllocator
{
    private readonly List<IReadOnlyList<DapVariable>> _values = new();

    public int Add(IReadOnlyList<DapVariable> variables)
    {
        _values.Add(variables);
        return _values.Count;
    }

    public int Add(params DapVariable[] variables) => Add((IReadOnlyList<DapVariable>)variables);

    public IReadOnlyList<DapVariable> GetOrEmpty(int reference)
        => reference >= 1 && reference <= _values.Count ? _values[reference - 1] : System.Array.Empty<DapVariable>();

    public void Clear() => _values.Clear();
}

/// <summary>上游 ExceptionBreakpointType / SourceBreakpointMode 在 DAP 里的名字与说明（上游 label / description 原文）。</summary>
public static class DapNames
{
    public const string UncaughtMishaps = "UNCAUGHT_MISHAPS";

    public static string ModeName(SourceBreakpointMode m) => m switch
    {
        SourceBreakpointMode.Escaped => "ESCAPED",
        SourceBreakpointMode.All => "ALL",
        _ => "EVALUATED",
    };

    public static SourceBreakpointMode? ParseMode(string? name) => name switch
    {
        null => SourceBreakpointMode.Evaluated,
        "EVALUATED" => SourceBreakpointMode.Evaluated,
        "ESCAPED" => SourceBreakpointMode.Escaped,
        "ALL" => SourceBreakpointMode.All,
        _ => null,
    };

    public static (string Label, string Description) ModeText(SourceBreakpointMode m) => m switch
    {
        SourceBreakpointMode.Escaped => ("Escaped", "Stop if this iota would be escaped."),
        SourceBreakpointMode.All => ("All", "Always stop at this iota."),
        _ => ("Evaluated", "Stop if this iota would be evaluated. (default)"),
    };
}

/// <summary>上游 SharedDebugState.setBreakpoints 的一条结果。</summary>
public sealed record BreakpointResult(bool Verified, string? Message, string? Reason, DebugSource? Source, int? Line);
