using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace HexCastingTerraria.Content;

/// <summary>
/// 把游戏自带的一张贴图读出来改色，做成本模组自己的贴图：深板岩（<see cref="Tiles.DeepslateArt"/>）、
/// 共振药水与共振增益（<see cref="Items.ResonanceArt"/>）。只在内存里做，不存任何文件 —— 泰拉原版贴图不进公开仓库。
/// 只能在客户端的主线程上调（要用显卡建贴图）。
///
/// 泰拉贴图读出来是预乘过透明度的：这里先还原成直通透明度再交给改色函数；存成 PNG 读回来时泰拉会再预乘一次。
/// 建出来的贴图不归资源库管，用的人卸载时自己释放。
/// </summary>
internal static class VanillaRecolor
{
    /// <param name="vanillaPath">游戏自带贴图的路径，如 <c>Images/Item_2322</c>。</param>
    /// <param name="name">新贴图的名字（只用来标识）。</param>
    /// <param name="recolor">改色：拿到整张图的像素（直通透明度，一行接一行）和宽、高，原地改。</param>
    public static Asset<Texture2D> Create(string vanillaPath, string name, Action<Color[], int, int> recolor)
    {
        var src = Main.Assets.Request<Texture2D>(vanillaPath, AssetRequestMode.ImmediateLoad).Value;
        var data = new Color[src.Width * src.Height];
        src.GetData(data);
        for (int i = 0; i < data.Length; i++) data[i] = Unpremultiply(data[i]);
        recolor(data, src.Width, src.Height);

        using var png = new MemoryStream();
        using (var tex = new Texture2D(Main.graphics.GraphicsDevice, src.Width, src.Height))
        {
            tex.SetData(data);
            tex.SaveAsPng(png, src.Width, src.Height);
        }
        png.Position = 0;
        return Main.Assets.CreateUntracked<Texture2D>(png, name + ".png", AssetRequestMode.ImmediateLoad);
    }

    /// <summary>按色标插值：<paramref name="t"/> 落在哪两个色标之间就在那两个颜色之间取，超出两端取端点。透明度给 255。</summary>
    public static Color Gradient((float At, Color Color)[] stops, float t)
    {
        if (t <= stops[0].At) return stops[0].Color;
        for (int k = 1; k < stops.Length; k++)
        {
            if (t <= stops[k].At)
            {
                var (a, b) = (stops[k - 1], stops[k]);
                return Color.Lerp(a.Color, b.Color, (t - a.At) / (b.At - a.At));
            }
        }
        return stops[^1].Color;
    }

    /// <summary>亮度（0 ~ 1），按人眼对红绿蓝的敏感程度加权。</summary>
    public static float Luma(Color c) => (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;

    private static Color Unpremultiply(Color c)
    {
        if (c.A == 0) return Color.Transparent;
        if (c.A == 255) return c;
        float k = 255f / c.A;
        return new Color((byte)Math.Min(255f, c.R * k), (byte)Math.Min(255f, c.G * k), (byte)Math.Min(255f, c.B * k), c.A);
    }
}
