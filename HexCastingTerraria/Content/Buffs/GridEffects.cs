using Terraria;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Buffs;

/// <summary>
/// 明晰（原版 HexMobEffects.ENLARGE_GRID，有益）：咒术网格大小 ×1.25（MULTIPLY_TOTAL +0.25），网格变细、能画的地方更大。
/// 喝明晰药水得到（<see cref="Items.EnlargeGridPotion"/>）。网格大小的算法见 <see cref="Client.HexGridZoom"/>。
///
/// 图标是原版的效果图标，后面的蓝框用泰拉自带的空白增益底图在启动时拼上（<see cref="Items.GridPotionArt"/>）。
/// </summary>
public sealed class EnlargeGrid : ModBuff
{
}

/// <summary>
/// 蒙翳（原版 HexMobEffects.SHRINK_GRID，有害）：咒术网格大小 ×0.8（MULTIPLY_TOTAL -0.2），网格变粗、能画的地方更小。
/// 喝蒙翳药水得到（<see cref="Items.ShrinkGridPotion"/>）。原版是有害效果，泰拉里算减益：不能右键取消，图标是红框。
/// </summary>
public sealed class ShrinkGrid : ModBuff
{
    public override void SetStaticDefaults()
    {
        Main.debuff[Type] = true;
    }
}
