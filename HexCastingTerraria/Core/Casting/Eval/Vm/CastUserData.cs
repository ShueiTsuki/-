using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Core.Casting.Eval.Vm;

/// <summary>
/// 本次施法的临时数据袋。
/// 移植自源项目 `CastingImage.userData`（那边是一个 NBT `CompoundTag`）。
///
/// 用途：法术图案有时需要「跨图案记住点什么」，但又不该写进 iota（那会进存档）。
/// 目前有两个用途：
///   1. `add_motion` 的防刷计数（源项目 bug #387）
///   2. `read/local` / `write/local` 的 Ravenmind（源项目 `HexAPI.RAVENMIND_USERDATA`）
///
/// 生命周期：**只在一次施法内有效**，施法结束即丢弃。
///
/// 注意：但是「一次施法」在**法术环**里比想象的长：
/// 环会带着同一个 <see cref="CastingImage"/> 走完所有石板，
/// 所以一块石板 `write/local` 写进去的东西，**后面几块石板读得到**。
/// 这正是 locals 在源项目里的主要用途。
/// </summary>
public sealed class CastUserData
{
    /// <summary>`add_motion` 已推动过的目标。对应源项目 `HexAPI.MARKED_MOVED_USERDATA`。</summary>
    public const string MarkedMovedGroup = "marked_moved";

    private readonly Dictionary<string, HashSet<string>> _groups = new();

    /// <summary>
    /// Ravenmind：`read/local` / `write/local` 读写的那一个 iota。
    /// 对应源项目 `userData[RAVENMIND_USERDATA]`（那边存的是序列化后的 NBT）。
    ///
    /// null = 从未写过（`read/local` 会压一个 null iota，与源项目一致）。
    /// </summary>
    public Iota? Ravenmind { get; set; }

    /// <summary>分组里是否已有该键。</summary>
    public bool Has(string group, string key)
        => _groups.TryGetValue(group, out var set) && set.Contains(key);

    /// <summary>把键加入分组。</summary>
    public void Add(string group, string key)
    {
        if (!_groups.TryGetValue(group, out var set))
        {
            set = new HashSet<string>();
            _groups[group] = set;
        }
        set.Add(key);
    }

    /// <summary>所有分组和里面的键（法术环走到一半存档用，见 Content/Net/CastingImageTag）。</summary>
    public IEnumerable<(string Group, IReadOnlyCollection<string> Keys)> Groups()
    {
        foreach (var kv in _groups) yield return (kv.Key, kv.Value);
    }

    /// <summary>
    /// 复制一份。对应源项目 `userData.copy()`。
    ///
    /// 注意：必须深拷贝：`CastingImage` 是不可变的，若新旧 image 共享同一个袋子，
    /// 写入会「回溯污染」之前的状态 —— mishap 回滚时数据已经改掉了。
    /// </summary>
    public CastUserData Clone()
    {
        var copy = new CastUserData { Ravenmind = Ravenmind };
        foreach (var kv in _groups)
        {
            copy._groups[kv.Key] = new HashSet<string>(kv.Value);
        }
        return copy;
    }
}
