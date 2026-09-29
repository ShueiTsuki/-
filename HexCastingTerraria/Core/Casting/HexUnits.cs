namespace HexCastingTerraria.Core.Casting;

/// <summary>
/// 单位换算常量。
///
/// ⚠️ 这是 3D→2D 移植中**最容易漏掉但影响全局**的一点（见 TERRARIA_2D_ADAPTATION.md §7.5）：
///   Minecraft 里 1 格 = 1.0 距离单位
///   泰拉瑞亚里 1 图格 = 16 像素
///
/// 原作的距离常量是「按格数」写的，直接抄到泰拉会缩到 1/16。
/// 例如 DEFAULT_AMBIT_RADIUS = 32.0 在泰拉只有 **2 个图格** ——
/// 玩家一动就报「实体太远」，射线只能打到脚边。
///
/// 因此所有距离都必须过这里换算。
/// </summary>
public static class HexUnits
{
    /// <summary>一个图格等于多少像素。泰拉固定值。</summary>
    public const float PixelsPerTile = 16f;

    /// <summary>原作 DEFAULT_AMBIT_RADIUS（单位：格）。</summary>
    public const float AmbitRadiusTiles = 32f;

    /// <summary>施法作用半径（单位：像素）= 32 格 × 16 = 512 像素。</summary>
    public const float AmbitRadiusPixels = AmbitRadiusTiles * PixelsPerTile;

    /// <summary>原作 Action.RAYCAST_DISTANCE（单位：格）。</summary>
    public const float RaycastDistanceTiles = 32f;

    /// <summary>射线最大距离（单位：像素）= 512 像素。</summary>
    public const float RaycastDistancePixels = RaycastDistanceTiles * PixelsPerTile;

    /// <summary>格 → 像素。</summary>
    public static float TilesToPixels(float tiles) => tiles * PixelsPerTile;

    /// <summary>像素 → 格。</summary>
    public static float PixelsToTiles(float pixels) => pixels / PixelsPerTile;

    /// <summary>世界像素坐标 → 图格坐标（向下取整）。</summary>
    public static int PixelToTile(float pixels) => (int)System.Math.Floor(pixels / PixelsPerTile);

    /// <summary>图格坐标 → 该图格左上角的世界像素坐标。</summary>
    public static float TileToPixel(int tile) => tile * PixelsPerTile;
}
