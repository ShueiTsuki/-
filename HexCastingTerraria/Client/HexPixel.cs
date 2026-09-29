using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace HexCastingTerraria.Client;

/// <summary>
/// 共享的 1×1 白色贴图，用于画线段与方块点。
///
/// ⚠️ **必须延迟创建**：FNA3D 要求图形 API 只在主线程调用，
/// 而模组 `Load()` 不在主线程 —— 在那里 `new Texture2D` 会抛
/// `ThreadStateException` 并导致模组被禁用（这个坑本项目踩过一次）。
/// 所以这里等到第一次绘制时才创建。
/// </summary>
public static class HexPixel
{
    private static Texture2D? _pixel;

    /// <summary>1×1 白点。第一次访问时创建。</summary>
    public static Texture2D Value
    {
        get
        {
            if (_pixel == null || _pixel.IsDisposed)
            {
                _pixel = new Texture2D(Main.graphics.GraphicsDevice, 1, 1);
                _pixel.SetData(new[] { Color.White });
            }
            return _pixel;
        }
    }

    /// <summary>卸载时释放（避免换世界后纹理泄漏）。</summary>
    public static void Unload()
    {
        _pixel?.Dispose();
        _pixel = null;
    }

    /// <summary>用 1px 贴图画一条线。</summary>
    public static void DrawLine(SpriteBatch sb, Vector2 a, Vector2 b, float width, Color color)
    {
        var delta = b - a;
        float length = delta.Length();
        if (length < 0.01f) return;

        float angle = (float)System.Math.Atan2(delta.Y, delta.X);
        sb.Draw(Value, a, null, color, angle,
            new Vector2(0f, 0.5f), new Vector2(length, width), SpriteEffects.None, 0f);
    }

    /// <summary>用 1px 贴图画一个实心方点。</summary>
    public static void DrawDot(SpriteBatch sb, Vector2 center, float radius, Color color)
    {
        int s = System.Math.Max(1, (int)(radius * 2f));
        sb.Draw(Value, center - new Vector2(s * 0.5f), null, color, 0f,
            Vector2.Zero, new Vector2(s, s), SpriteEffects.None, 0f);
    }
}
