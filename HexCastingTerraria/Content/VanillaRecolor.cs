using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace HexCastingTerraria.Content;

/// <summary>
/// 把游戏自带的一张贴图读出来改色，做成本模组自己的贴图：深板岩（<see cref="Tiles.DeepslateArt"/>）、
/// 共振药水与共振增益（<see cref="Items.ResonanceArt"/>）、明晰 / 蒙翳药水与增益（<see cref="Items.GridPotionArt"/>）。只在内存里做，不存任何文件 —— 泰拉原版贴图不进公开仓库。
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

    /// <summary>读一张已经加载的贴图的像素（还原成直通透明度），拿来往改色的图上叠。</summary>
    public static Color[] ReadPixels(Texture2D tex)
    {
        var data = new Color[tex.Width * tex.Height];
        tex.GetData(data);
        for (int i = 0; i < data.Length; i++) data[i] = Unpremultiply(data[i]);
        return data;
    }

    /// <summary>把同尺寸的 <paramref name="top"/> 按透明度叠到 <paramref name="data"/> 上（都是直通透明度）。</summary>
    public static void Overlay(Color[] data, Color[] top)
    {
        for (int i = 0; i < data.Length; i++)
        {
            float ta = top[i].A / 255f;
            if (ta <= 0f) continue;
            float ba = data[i].A / 255f;
            float a = ta + ba * (1f - ta);
            Color Mix(Color t, Color b) => new(
                (int)System.Math.Round((t.R * ta + b.R * ba * (1f - ta)) / a),
                (int)System.Math.Round((t.G * ta + b.G * ba * (1f - ta)) / a),
                (int)System.Math.Round((t.B * ta + b.B * ba * (1f - ta)) / a),
                (int)System.Math.Round(a * 255f));
            data[i] = Mix(top[i], data[i]);
        }
    }

    /// <summary>泰拉药水贴图（20×30）的瓶身改色：瓶颈和瓶塞（第 14 行以上）不动。共振药水用它。</summary>
    public static void RecolorPotionBody(Color[] data, int width, (float At, Color Color)[] stops)
        => RecolorMasked(data, i => i >= 14 * width, stops);

    /// <summary>
    /// 只改液体：挑出饱和度够高的像素（药水里是有颜色的液体，玻璃和瓶塞是灰白的）。明晰 / 蒙翳药水用它（小治疗药水的烧瓶，液体是红的）。
    /// </summary>
    public static void RecolorLiquid(Color[] data, (float At, Color Color)[] stops)
    {
        var original = (Color[])data.Clone();
        RecolorMasked(data, i => Saturation(original[i]) > 0.35f, stops);
    }

    /// <summary>挑出来的那些像素先把亮度拉到 0 ~ 1，再在色标之间取，明暗关系保留；透明的不动。</summary>
    private static void RecolorMasked(Color[] data, Func<int, bool> inMask, (float At, Color Color)[] stops)
    {
        var mask = new bool[data.Length];
        float lo = 1f, hi = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i].A == 0 || !inMask(i)) continue;
            mask[i] = true;
            float l = Luma(data[i]);
            lo = Math.Min(lo, l);
            hi = Math.Max(hi, l);
        }
        if (hi <= lo) return;
        for (int i = 0; i < data.Length; i++)
        {
            if (!mask[i]) continue;
            data[i] = Gradient(stops, (Luma(data[i]) - lo) / (hi - lo)) with { A = data[i].A };
        }
    }

    private static float Saturation(Color c)
    {
        int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max == 0 ? 0f : (max - min) / (float)max;
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
