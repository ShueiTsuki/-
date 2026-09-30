using HexCastingTerraria.Core.Media;
using Microsoft.Xna.Framework;
using Terraria;

namespace HexCastingTerraria.Client;

/// <summary>
/// 法术配色（「颜料 / 染色剂」）的取色。
///
/// 与原版一致：每个玩家身上记着一份颜料（原版 FrozenPigment = 颜料物品 + 施放「内化染色剂」的人），
/// 取色走原版 ColorProvider.getColor(time, position) —— 多色颜料随时间渐变，
/// 同一时刻不同位置的颜色也略有错开（粒子因此五颜六色）。公式在 <see cref="Pigments"/>。
/// 没用过颜料的玩家是原版默认的「空无染色剂」紫。
/// </summary>
public static class HexPigment
{
    /// <summary>MC 刻（泰拉 60 帧 / 秒，MC 20 刻 / 秒）。</summary>
    public static float McTime => Main.GameUpdateCount / 3f;

    /// <summary>某个玩家的配色在某个位置上的颜色（<paramref name="posDot"/> = 原版 (0.1,0.1,0.1)·position）。</summary>
    public static Color ColorOf(Player player, float posDot = 0f)
    {
        var hp = Content.HexPlayer.Get(player);
        return ToColor(Pigments.Color(hp.PigmentIdOrDefault, hp.PigmentOwner, McTime, posDot));
    }

    /// <summary>
    /// 粒子用：原版每个粒子用它的飞行方向（单位向量）当位置取色，
    /// 点乘 (0.1,0.1,0.1) 落在约 ±0.17 之间 —— 这里直接随机一个。
    /// </summary>
    public static Color Sample(Player player)
    {
        var hp = Content.HexPlayer.Get(player);
        return Sample(hp.PigmentIdOrDefault, hp.PigmentOwner);
    }

    /// <summary>同上，直接给颜料（法术环用原动力上的颜料，不一定是某个玩家的）。</summary>
    public static Color Sample(string pigmentId, System.Guid owner)
        => ToColor(Pigments.Color(pigmentId, owner, McTime, Main.rand.NextFloat(-0.17f, 0.17f)));

    /// <summary>本地玩家此刻的颜色。</summary>
    public static Color Current => Main.gameMenu || Main.LocalPlayer is not { active: true }
        ? ToColor(Pigments.Color(Pigments.DefaultId, System.Guid.Empty, McTime, 0f))
        : ColorOf(Main.LocalPlayer);

    private static Color ToColor(int rgb) => new((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}
