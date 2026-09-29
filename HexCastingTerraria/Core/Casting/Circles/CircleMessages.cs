namespace HexCastingTerraria.Core.Casting.Circles;

/// <summary>
/// 法术环的消息出口。
///
/// 环**没有施法者**，所以不能像玩家环境那样往聊天栏发 ——
/// 源项目是显示在原动力方块上方（`postDisplay`）。
///
/// 这里只留一个委托：**`Core/` 不得引用 tModLoader**，
/// 实际实现由 `HexCastingTerraria.Load()` 注入。
/// （同类问题本项目已踩过四次：Xna.Color、Xna.Vector2、ModConfig、ModConfig 又一次。）
/// </summary>
public static class CircleMessages
{
    /// <summary>消息接收器。默认丢弃（离线测试时就是这种状态）。</summary>
    public static System.Action<string> Sink { get; set; } = static _ => { };

    public static void Post(string message) => Sink(message);
}
