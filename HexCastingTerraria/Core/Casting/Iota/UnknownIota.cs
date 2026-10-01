namespace HexCastingTerraria.Core.Casting.Iotas;

/// <summary>
/// 不认识种类的 iota —— 多半来自一个**关掉了的附属**（例如 HexParse 的注释 iota）。
///
/// 原版 Hex Casting 遇到不认识的 iota 类型会变成垃圾，数据就丢了；本模组要求更严（ADDONS.md「开关的实际效果」）：
/// 原样保管它的信封（种类 + 载荷），存档 / 联机原样写回，重新打开附属就恢复成原来的 iota。
/// 在栈上它只是一个不能执行、没有真假值的值，和垃圾一样。
/// </summary>
public sealed class UnknownIota : Iota
{
    public UnknownIota(string kindTag, object? payload)
    {
        KindTag = kindTag;
        Payload = payload;
    }

    /// <summary>信封里的种类标签，例如 "hexparse:comment"。</summary>
    public string KindTag { get; }

    /// <summary>信封里的载荷，原样保管。</summary>
    public object? Payload { get; }

    public override IotaKind Kind => IotaKind.Unknown;

    public override string TypeName => "unknown";

    public override bool ValueEquals(Iota other)
        => other is UnknownIota u && u.KindTag == KindTag && IotaSerializer.SameTree(u.Payload, Payload);

    public override object? Serialize() => IotaSerializer.Envelope(KindTag, Payload);

    protected override string DescribeValue() => $"未加载的 iota（{KindTag}）";

    /// <summary>上游 brokenIota 是灰色的「损坏的iota」；这里说明是哪种没加载（附属关着时会这样）。</summary>
    public override DisplayText DisplayRich() => DisplayText.Literal(DescribeValue(), McColors.Gray);
}
