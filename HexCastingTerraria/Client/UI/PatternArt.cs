using System.Collections.Generic;
using HexCastingTerraria.Core.Canvas;
using HexCastingTerraria.Core.Casting.Math;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 按原版画法画静态图案（<see cref="StaticPatternArt"/>）的两个入口：
///   - <see cref="DrawUi"/>：界面里（提示框、开发者面板、HUD）当场画
///   - <see cref="QueueWorld"/>：世界里的方块（石板、挂轴、阿卡夏）先登记，图格画完后一次画完（<see cref="PatternArtSystem"/>）
///
/// 这里曾经是 DeepSeek 自己的画法（黑色描边 + 纯色线 + 红色起点 + 黄色箭头），原版没有这种样子。
/// </summary>
public static class PatternArt
{
    private static readonly List<ColoredVertex> Scratch = new();

    /// <summary>MC 刻（20 / 秒），抖动的石板用。</summary>
    public static double Time => Main.GameUpdateCount / 3.0;

    /// <summary>
    /// 在界面层里画：<paramref name="topLeft"/> / <paramref name="size"/> 是当前批次的坐标（界面缩放层就是缩放前的坐标）。
    /// <paramref name="uiScaled"/> = 当前批次套着 UIScaleMatrix（多数界面层）；false = 屏幕像素层。
    /// </summary>
    public static void DrawUi(HexPattern pattern, Vector2 topLeft, float size, PatternStyle style, PatternPalette palette,
        bool uiScaled = true, double seed = 0, bool strokeOrder = false)
    {
        Scratch.Clear();
        StaticPatternArt.Render(Scratch, pattern, style, palette, seed, Time, new Vec2f(topLeft.X, topLeft.Y), size, strokeOrder);
        var m = uiScaled ? Main.UIScaleMatrix : Matrix.Identity;
        PrimitiveBatch.Flush(Scratch, m, m);
    }

    /// <summary>界面里的「这个图案长这样」：原版卷轴提示框的样式（可读 + 起笔点 + 格点）。按住 Ctrl 显示笔顺渐变。</summary>
    public static void DrawReadable(HexPattern pattern, Vector2 center, float size, bool uiScaled = true)
    {
        bool ctrl = Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftControl)
                    || Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightControl);
        DrawUi(pattern, center - new Vector2(size / 2f), size, PatternStyle.Readable,
            (ctrl ? PatternPalette.DefaultGradient : PatternPalette.Default).WithDots(true, true), uiScaled, 0, ctrl);
    }

    private readonly record struct WorldJob(HexPattern Pattern, Vector2 TopLeft, float Size, PatternStyle Style, PatternPalette Palette, double Seed);

    private static readonly List<WorldJob> Jobs = new();

    /// <summary>世界里的方块登记一个图案（世界像素坐标的左上角 + 边长）。在图格绘制回调里调用。</summary>
    public static void QueueWorld(HexPattern pattern, Vector2 worldTopLeft, float size, PatternStyle style, PatternPalette palette, double seed)
        => Jobs.Add(new WorldJob(pattern, worldTopLeft, size, style, palette, seed));

    internal static void FlushWorld()
    {
        if (Jobs.Count == 0) return;
        Scratch.Clear();
        foreach (var j in Jobs)
        {
            var tl = j.TopLeft - Main.screenPosition;
            StaticPatternArt.Render(Scratch, j.Pattern, j.Style, j.Palette, j.Seed, Time, new Vec2f(tl.X, tl.Y), j.Size);
        }
        Jobs.Clear();
        PrimitiveBatch.DrawDirect(Scratch, Main.GameViewMatrix.TransformationMatrix);
    }
}

/// <summary>图格画完以后把这一帧登记的方块图案一次画完（此时没有 SpriteBatch 开着）。</summary>
public sealed class PatternArtSystem : ModSystem
{
    public override void PostDrawTiles() => PatternArt.FlushWorld();
}
