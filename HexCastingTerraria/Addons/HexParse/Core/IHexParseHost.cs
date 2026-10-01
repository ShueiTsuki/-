using HexCastingTerraria.Core.Casting.Iotas;

namespace HexCastingTerraria.Addons.HexParse.Core;

/// <summary>消息的样式（上游用聊天颜色区分）。</summary>
public enum HexParseMessageKind
{
    /// <summary>普通输出（结果、列表）。</summary>
    Info,

    /// <summary>警告：未知符号（上游金色）。</summary>
    Warning,

    /// <summary>错误：解析失败（上游深红色）。</summary>
    Error,
}

/// <summary>
/// 解析器要向游戏要的东西（上游这些是直接读 ServerPlayer / ServerLevel 的）。
/// 游戏侧（Addons/HexParse/Game）实现一份，离线测试实现一份假的 —— Core 因此不碰泰拉。
/// </summary>
public interface IHexParseHost
{
    /// <summary>施法者自己（<c>self</c> / <c>myself</c>）；没有施法者时为 null。</summary>
    EntityIota? Self { get; }

    /// <summary>作者名（读出代码时开头的 <c>// Author:</c>）。</summary>
    string AuthorName { get; }

    /// <summary>这个大法术（长 id，如 hexcasting:lightning）在本世界解锁了没有（ByScroll 模式下问的）。</summary>
    bool IsGreatUnlocked(string longId);

    /// <summary>玩家手动指定的短名称归属（<c>/hexParse conflict set</c>，存世界）；没指定返回 null。</summary>
    string? ManualShortName(string shortName);

    /// <summary>宏（<c>#名</c>）或别名（不以 # 开头）的内容；没有返回 null。</summary>
    string? GetMacro(string key);

    /// <summary>
    /// 解析 <c>entity_…</c>（泰拉偏差：没有 UUID，写法是 <c>entity_player_&lt;编号&gt;</c> / <c>entity_npc_&lt;编号&gt;</c> / <c>entity_item_&lt;编号&gt;</c> / <c>entity_wallscroll_&lt;编号&gt;</c> 等，见 HexParseHost）。
    /// 别的玩家的真名要抛 <see cref="HexParseException"/>（上游 MishapOthersName）；认不出返回 null（上游变成 NullIota）。
    /// </summary>
    Iota? ResolveEntity(string node);

    /// <summary>实体 -> 代码（不是 self 的时候）。</summary>
    string EntityToCode(EntityIota entity);

    /// <summary>发一条消息给施法者。</summary>
    void Message(string text, HexParseMessageKind kind);
}

/// <summary>解析单个符号时的错误（上游抛 RuntimeException，外层接住后发消息）。</summary>
public sealed class HexParseException : System.Exception
{
    public HexParseException(string message) : base(message) { }
}
