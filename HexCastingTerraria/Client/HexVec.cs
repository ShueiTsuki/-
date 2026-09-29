using System.Collections.Generic;
using HexCastingTerraria.Core.Casting.Math;
using Microsoft.Xna.Framework;

namespace HexCastingTerraria.Client;

/// <summary>
/// `Core` 的 <see cref="Vec2f"/> ↔ XNA 的 <see cref="Vector2"/> 互转。
///
/// 为什么要有这一层：`Core/` 不许引用 XNA（这样它才能在无头环境里被测），
/// 于是几何计算都在 Core 里用 `Vec2f` 完成，绘制侧再转成 `Vector2`。
/// 换算只在这一个文件里发生 —— 到处手写 `new Vector2(v.X, v.Y)`
/// 迟早会出现「某处忘了转、或者把 x/y 写反」这种编译器抓不到的错。
///
/// 只加实际用到的三个重载，不加没用的。
/// </summary>
internal static class HexVec
{
    public static Vector2 ToXna(this Vec2f v) => new(v.X, v.Y);

    public static Vec2f ToCore(this Vector2 v) => new(v.X, v.Y);

    /// <summary>整条折线转换（绘制用）。</summary>
    public static List<Vector2> ToXna(this List<Vec2f> src)
    {
        var outList = new List<Vector2>(src.Count);
        for (int i = 0; i < src.Count; i++)
        {
            outList.Add(src[i].ToXna());
        }
        return outList;
    }
}
