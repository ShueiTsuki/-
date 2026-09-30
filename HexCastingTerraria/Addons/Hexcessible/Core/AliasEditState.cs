using System.Linq;

namespace HexCastingTerraria.Addons.Hexcessible.Core;

/// <summary>
/// 改别名（上游 drawstate/AliasChanging.java 去掉渲染的部分）：输入框里的字、原名、签名、确认时存什么。
/// </summary>
public sealed class AliasEditState
{
    public AliasEditState(PatternEntries.Entry entry)
    {
        Alias = entry.IsAliased ? entry.Name : "";
        Original = entry.RawName;
        Signature = entry.Signature;
        Id = entry.Id;
    }

    public string Alias { get; set; }

    public string Original { get; }

    public string Signature { get; }

    public string Id { get; }

    public bool IsBlank => string.IsNullOrWhiteSpace(Alias);

    /// <summary>上游 Ctrl+退格：删掉最后一个空格分隔的词。</summary>
    public void DeleteWord()
    {
        var words = AutoCompleteState.JavaSplitSpace(Alias);
        Alias = string.Join(" ", words.Take(System.Math.Max(0, words.Count - 1)));
    }

    /// <summary>上游 Enter / Tab：空着就存回原名（等于去掉别名），否则去掉首尾空白。</summary>
    public string ValueToStore => IsBlank ? Original : Alias.Trim();
}
