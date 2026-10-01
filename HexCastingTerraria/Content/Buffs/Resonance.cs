using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Buffs;

/// <summary>
/// 「共振」增益（移植版新增，用户 2026-10-01 定）：喝共振药水（<see cref="Items.ResonancePotion"/>）得到，30 分钟。
/// 作用和镐子的「共振」前缀（<see cref="Prefixes.Resonant"/>）一样：挖淬灵晶块掉方块本身，相当于原版的精准采集
/// （判定在 <see cref="Tiles.QuenchedAllayGlobal"/>）。
///
/// 图标借用泰拉挖矿增益的镐子，客户端加载完把金光换成紫光（<see cref="Items.ResonanceArt"/>）。
/// </summary>
public sealed class Resonance : ModBuff
{
    public override string Texture => $"Terraria/Images/Buff_{BuffID.Mining}";
}
