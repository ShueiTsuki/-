using Terraria;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 手上的物品接「潜行 + 滚轮」（上游 HexDebug items/base/ShiftScrollable：调试杖换步进模式、淬灵的再加 Ctrl 换线程）。
/// 物品归本地客户端：在这里改字段后由本体调 NetStateChanged 同步给服务端。
/// </summary>
public interface IShiftScrollable
{
    /// <summary>这一下（按没按 Ctrl）它要不要。</summary>
    bool CanShiftScroll(bool ctrl);

    /// <summary>increase：滚轮往下（原版 delta &lt; 0）。返回要显示的提示（null 不显示）。</summary>
    string? ShiftScroll(Player player, bool increase, bool ctrl);
}
