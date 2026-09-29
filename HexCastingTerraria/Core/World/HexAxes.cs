namespace HexCastingTerraria.Core.World;

/// <summary>
/// 法术坐标（原版约定：方块单位、+Y 朝上）与泰拉图格坐标（+Y 朝下）之间的换算公式。
/// 纯函数，H = 世界高度（图格数）。泰拉侧的 HexSpaceWorld 用 Main.maxTilesY 调它们；离线测试直接调。
///
/// 位置：y ↔ H − y（自己是自己的逆）。方向：y ↔ −y。
/// 方块下标：原版「点在哪个方块里」= floor(y)；对应泰拉图格 H − 1 − floor(y)。
/// ⚠️ 不能写成 floor(H − y)：y 恰好是整数时（人站在地上，脚底 y 就是整数）会差一格。
/// </summary>
public static class HexAxes
{
    /// <summary>位置换算（两个方向公式相同）。</summary>
    public static double FlipPosition(double y, double h) => h - y;

    /// <summary>方向 / 位移 / 速度换算。</summary>
    public static double FlipDirection(double y) => -y;

    /// <summary>法术方块下标 ↔ 泰拉图格下标（两个方向公式相同）。</summary>
    public static int FlipBlock(int y, int h) => h - 1 - y;

    /// <summary>法术坐标的点 → 它所在的泰拉图格下标。</summary>
    public static int TileOfPoint(double hexY, int h) => FlipBlock((int)System.Math.Floor(hexY), h);

    /// <summary>
    /// 法术坐标的点 → 泰拉图格坐标（连续值），并保证 floor(结果) == <see cref="TileOfPoint"/>：
    /// 减一个远小于一个像素的量，让整数边界落进正确的那一格。
    /// </summary>
    public static double PointToTileY(double hexY, double h) => h - hexY - 1e-7;
}
