using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Core.Casting.Math;

namespace HexCastingTerraria.Addons.Hexcessible.Core;

/// <summary>
/// 自动补全的状态（上游 drawstate/AutoCompleting.java 去掉渲染的部分）：查询串、候选、选中项、选中的书页。
/// 锁定与否由调用方判断（书的解锁进度在游戏侧）。
/// </summary>
public sealed class AutoCompleteState
{
    private readonly PatternEntries _index;
    private readonly Func<PatternEntries.Entry, bool> _isLocked;
    private readonly Func<string, IEnumerable<PatternEntries.Entry>?>? _smart;
    private IReadOnlyList<PatternEntries.Entry> _suggestions;

    public AutoCompleteState(HexCoord start, PatternEntries index, Func<PatternEntries.Entry, bool> isLocked,
        Func<string, IEnumerable<PatternEntries.Entry>?>? smartSigs = null)
    {
        Start = start;
        _index = index;
        _isLocked = isLocked;
        _smart = smartSigs;
        _suggestions = index.All;
    }

    /// <summary>补全选中后从这里开始画（按下鼠标的格点，或 Ctrl+空格时鼠标所在的格点）。</summary>
    public HexCoord Start { get; }

    public string Query { get; private set; } = "";

    public int Chosen { get; private set; }

    public int ChosenDoc { get; private set; }

    /// <summary>上一次操作是鼠标（上游 lastInteractWasMouse，初值 true）。</summary>
    public bool LastInteractWasMouse { get; set; } = true;

    /// <summary>上游 noDistract：还没打字、也没用键盘操作过 —— 只显示一行淡淡的「输入名称……」，不弹候选。</summary>
    public bool NoDistract => LastInteractWasMouse && Query.Length == 0;

    public IReadOnlyList<PatternEntries.Entry> Suggestions => _suggestions;

    /// <summary>上游 getUnlockedSuggestions。</summary>
    public List<PatternEntries.Entry> Unlocked() => _suggestions.Where(e => !_isLocked(e)).ToList();

    public PatternEntries.Entry? ChosenEntry
    {
        get
        {
            var u = Unlocked();
            return Chosen < u.Count ? u[Chosen] : null;
        }
    }

    /// <summary>上游 setQuery（allow 关着就不变）：换查询就回到第一项、第一页。</summary>
    public void SetQuery(string query, bool allow = true)
    {
        if (!allow) return;
        Query = query;
        _suggestions = _index.Search(query, string.IsNullOrEmpty(query) ? null : _smart?.Invoke(query));
        Chosen = 0;
        ChosenDoc = 0;
    }

    /// <summary>上游 Ctrl+退格：删掉最后一个空格分隔的词（split 会丢掉末尾的空串，与 Java 一致）。</summary>
    public void DeleteWord(bool allow = true)
    {
        var words = JavaSplitSpace(Query);
        SetQuery(string.Join(" ", words.Take(Math.Max(0, words.Count - 1))), allow);
    }

    /// <summary>上游 offsetChosen：上下 / 滚轮，首尾相接。</summary>
    public void OffsetChosen(int by)
    {
        int size = Unlocked().Count;
        if (size == 0) return;
        Chosen = ((Chosen + by) % size + size) % size;
        ChosenDoc = 0;
    }

    /// <summary>上游 offsetChosenDoc：左右翻这条图案在书里的第几页。</summary>
    public void OffsetChosenDoc(int by)
    {
        var e = ChosenEntry;
        if (e is null) return;
        int size = e.Impls.Count;
        if (size == 0) return;
        ChosenDoc = ((ChosenDoc + by) % size + size) % size;
    }

    /// <summary>
    /// 上游 prepareOptions 的取窗：显示 count 项，选中项上面留 2 项（count 小于 3 时不留），不越过末尾。
    /// 返回 [起, 止) 下标。
    /// </summary>
    public (int From, int To) Window(int count)
    {
        int n = Unlocked().Count;
        int previs = count < 3 ? 0 : 2;
        int from = Math.Max(0, Math.Min(Chosen - previs, n - count));
        int to = Math.Min(n, from + count);
        return (from, to);
    }

    /// <summary>上游 lockedN：候选里被锁住的条数。</summary>
    public int LockedCount => _suggestions.Count - Unlocked().Count;

    /// <summary>Java String.split(" ")：去掉末尾的空串；整串为空时是一个空串。</summary>
    internal static List<string> JavaSplitSpace(string s)
    {
        var parts = s.Split(' ').ToList();
        while (parts.Count > 1 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        if (parts.Count == 1 && parts[0].Length == 0 && s.Length > 0) parts.Clear();
        return parts;
    }
}
