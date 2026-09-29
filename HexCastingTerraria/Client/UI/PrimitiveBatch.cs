using System.Collections.Generic;
using HexCastingTerraria.Core.Canvas;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 把 <see cref="PatternGeometry"/> 产出的三角形直接交给显卡。
///
/// 原版用 MC 的 Tesselator 画真正的三角形（拐角扇形、圆头、逐顶点渐变）。
/// SpriteBatch 只能画矩形贴图，之前用「旋转的 1px 矩形 + 顶点补圆盘」去凑，
/// 拐角有缺口/斜接、渐变是分段色块 —— 这就是「线条看着不对」的来源。
///
/// 坐标为**屏幕像素**（后备缓冲区），调用方负责传入同一坐标系下的鼠标位置。
/// </summary>
public static class PrimitiveBatch
{
    private static BasicEffect? _effect;
    private static VertexPositionColor[] _buffer = new VertexPositionColor[4096];
    private static bool _loggedFailure;

    /// <summary>
    /// 结束当前 SpriteBatch、画三角形、再以界面层的默认参数重新开启 SpriteBatch。
    /// 只在界面层绘制回调里调用（那里 Main.spriteBatch 处于 Begin 状态）。
    /// </summary>
    public static void Flush(List<ColoredVertex> verts, Matrix spriteBatchTransform)
    {
        if (verts.Count < 3) return;

        var sb = Main.spriteBatch;
        sb.End();
        try
        {
            Draw(verts);
        }
        catch (System.Exception e)
        {
            // 自动化验证覆盖不到客户端绘制；万一某个图形后端不支持，记一次日志，不拖垮整个界面层
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                HexCastingTerraria.Instance?.Logger.Error($"[HexCasting] 画布笔迹绘制失败：{e}");
            }
        }
        finally
        {
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise, null, spriteBatchTransform);
        }
    }

    private static void Draw(List<ColoredVertex> verts)
    {
        var gd = Main.graphics.GraphicsDevice;
        if (_effect is null || _effect.IsDisposed)
        {
            _effect = new BasicEffect(gd) { VertexColorEnabled = true, TextureEnabled = false, LightingEnabled = false };
        }

        var vp = gd.Viewport;
        _effect.World = Matrix.Identity;
        _effect.View = Matrix.Identity;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0, vp.Width, vp.Height, 0, 0, 1);

        int count = verts.Count - verts.Count % 3;
        if (_buffer.Length < count) _buffer = new VertexPositionColor[count * 2];
        for (int i = 0; i < count; i++)
        {
            var v = verts[i];
            // 顶点颜色是非预乘 alpha；配合 NonPremultiplied 混合与原版 GL 的默认混合一致
            _buffer[i] = new VertexPositionColor(new Vector3(v.X, v.Y, 0f),
                new Color((byte)(v.Argb >> 16), (byte)(v.Argb >> 8), (byte)v.Argb, (byte)(v.Argb >> 24)));
        }

        gd.BlendState = BlendState.NonPremultiplied;
        gd.DepthStencilState = DepthStencilState.None;
        gd.RasterizerState = RasterizerState.CullNone;   // 原版 disableCull：扇形方向不一
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserPrimitives(PrimitiveType.TriangleList, _buffer, 0, count / 3);
        }
    }
}
