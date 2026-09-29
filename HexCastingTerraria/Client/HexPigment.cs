using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace HexCastingTerraria.Client;

/// <summary>
/// 法术配色（「颜料」）。
///
/// 源项目里你可以在副手拿一管**颜料**（pigment）再施法，
/// 之后你所有的法术粒子、图案描线都会变成那个颜色 —— 纯表现，不影响任何机制。
/// 泰拉侧的对应物是**染料**：它本来就是「给东西上色」的物品，语义完全对得上。
///
/// 为什么颜色表要自己写一遍：泰拉的染料颜色并不存在一张可读的表里
/// （每个染料的颜色来自它的贴图，`Item.dye` 只是个编号），
/// 所以这里把 13 种基础染料的颜色硬编码出来。**只支持基础染料** ——
/// 特殊染料（火焰、渐变、彩虹）没有单一颜色，硬套一个色值只会得到「看起来不对」的结果。
/// </summary>
public static class HexPigment
{
    /// <summary>当前生效的颜色。默认是咒法学的主色。</summary>
    public static Color Current { get; private set; } = HexColors.PatternMain;

    /// <summary>当前生效的染料物品类型；0 = 没设置（用默认色）。</summary>
    public static int DyeType { get; private set; }

    /// <summary>每种基础染料对应的颜色。不在表里的染料一律回落到默认色。</summary>
    private static readonly (int Dye, Color Color)[] Table =
    {
        (ItemID.RedDye, new Color(255, 60, 60)),
        (ItemID.OrangeDye, new Color(255, 140, 40)),
        (ItemID.YellowDye, new Color(255, 230, 60)),
        (ItemID.LimeDye, new Color(160, 230, 60)),
        (ItemID.GreenDye, new Color(70, 200, 70)),
        (ItemID.TealDye, new Color(60, 200, 170)),
        (ItemID.CyanDye, new Color(60, 210, 230)),
        (ItemID.SkyBlueDye, new Color(110, 180, 255)),
        (ItemID.BlueDye, new Color(70, 100, 240)),
        (ItemID.PurpleDye, new Color(150, 70, 230)),
        (ItemID.VioletDye, new Color(190, 90, 230)),
        (ItemID.PinkDye, new Color(255, 140, 200)),
        (ItemID.BlackDye, new Color(60, 60, 70)),
        (ItemID.SilverDye, new Color(190, 195, 205)),
        (ItemID.BrownDye, new Color(160, 110, 70)),

        // 「亮色染料」是同一色系的高饱和版本，直接映射到更亮的同色 ——
        // 泰拉里它们确实就是「同色但更亮」，与玩家的直觉一致。
        (ItemID.BrightRedDye, new Color(255, 90, 90)),
        (ItemID.BrightOrangeDye, new Color(255, 170, 70)),
        (ItemID.BrightYellowDye, new Color(255, 245, 110)),
        (ItemID.BrightLimeDye, new Color(190, 255, 90)),
        (ItemID.BrightGreenDye, new Color(100, 240, 100)),
        (ItemID.BrightTealDye, new Color(90, 240, 210)),
        (ItemID.BrightCyanDye, new Color(90, 245, 255)),
        (ItemID.BrightSkyBlueDye, new Color(150, 210, 255)),
        (ItemID.BrightBlueDye, new Color(110, 140, 255)),
        (ItemID.BrightPurpleDye, new Color(185, 105, 255)),
        (ItemID.BrightVioletDye, new Color(220, 125, 255)),
        (ItemID.BrightPinkDye, new Color(255, 175, 225)),
    };

    /// <summary>取某个染料对应的颜色。不在表里返回 null（调用方决定怎么处理）。</summary>
    public static Color? ColorOf(int dyeItemType)
    {
        foreach (var (dye, color) in Table)
        {
            if (dye == dyeItemType) return color;
        }

        return null;
    }

    /// <summary>每帧由客户端根据本地玩家的设置刷新。</summary>
    public static void Refresh(int dyeItemType)
    {
        DyeType = dyeItemType;
        Current = ColorOf(dyeItemType) ?? HexColors.PatternMain;
    }

    /// <summary>清掉配色，回到默认。</summary>
    public static void Reset()
    {
        DyeType = 0;
        Current = HexColors.PatternMain;
    }
}
