using HexCastingTerraria.Content.Buffs;
using Terraria;

namespace HexCastingTerraria.Client;

/// <summary>
/// 原版的咒术网格大小属性（HexAttributes.GRID_ZOOM：基础 1.0，范围 0.5 ~ 4.0），照 MC 属性的算法叠加：
///   探知透镜 MULTIPLY_BASE +0.33（<see cref="ScryingOverlay.GridZoom"/>）；
///   明晰 MULTIPLY_TOTAL +0.25、蒙翳 MULTIPLY_TOTAL -0.2（药水只有一级）。
/// 原版每帧按它算格距（GuiSpellcasting.hexSize：格距 = 基础格距 / 属性值），所以药效中途到期，开着的画布也跟着变；
/// 这里同样每帧算（<see cref="HexClientSystem.PostUpdateInput"/>）。配置里的「画布网格缩放」是移植版加的个人偏好，再乘在外面。
/// </summary>
internal static class HexGridZoom
{
    public static float Of(Player player)
    {
        double zoom = 1.0;
        if (ScryingOverlay.HasSight(player)) zoom *= ScryingOverlay.GridZoom;
        if (player.HasBuff<EnlargeGrid>()) zoom *= 1 + 0.25;
        if (player.HasBuff<ShrinkGrid>()) zoom *= 1 - 0.2;
        return (float)System.Math.Clamp(zoom, 0.5, 4.0);
    }
}
