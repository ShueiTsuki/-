using HexCastingTerraria.Core.Media;
using Terraria;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 按整件算媒质的物品（源项目 ADMediaHolder 的静态持有者）：紫水晶粉、紫水晶碎片、充能紫水晶、淬灵晶碎片（<see cref="MediaMaterial"/>），
/// 还有淬灵晶块 —— 原版淬灵晶块本身就是媒质（4 片碎片 = 120 粉，扣媒质时排在碎片前面），瓦 / 砖不算。
/// 淬灵晶块的物品类由建材脚本生成（DecoBlocks.Generated.cs），不能继承 MediaMaterial，所以在这里单独认。
///
/// 施法扣背包、掉落物当媒质、促动石塞媒质、剪接台媒质槽都走这里，不要再各处直接写 <c>is MediaMaterial</c>。
/// </summary>
internal static class MediaItems
{
    /// <param name="unitValue">一件的媒质。</param>
    /// <param name="priority">扣费优先级（<see cref="MediaPriority"/>）。</param>
    public static bool TryGet(Item item, out long unitValue, out int priority)
    {
        if (item.ModItem is MediaMaterial m)
        {
            unitValue = m.MediaValue;
            priority = m.Priority;
            return true;
        }
        if (!item.IsAir && item.type == ModContent.ItemType<Tiles.QuenchedAllayItem>())
        {
            unitValue = MediaConstants.QuenchedBlockUnit;
            priority = MediaPriority.QuenchedAllay;
            return true;
        }
        unitValue = 0;
        priority = 0;
        return false;
    }

    public static bool Is(Item item) => TryGet(item, out _, out _);
}
