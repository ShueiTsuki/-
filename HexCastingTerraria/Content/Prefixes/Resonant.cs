using System.Collections.Generic;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Prefixes;

/// <summary>
/// 「共振」：镐子专属的前缀（移植版新增，用户 2026-10-01 定），代替原版的精准采集附魔 —— 泰拉没有附魔。
/// 只对咒法学里原版要精准采集的方块起作用：现在只有淬灵晶块（挖掉掉方块本身，见 <see cref="Tiles.QuenchedAllayGlobal"/>）；
/// 原版另一个是启迪树叶，那只是装饰，用户定不接。不改任何数值。
///
/// 只上镐子和钻头（镐力 &gt; 0）：分到「任意武器」这一类（钻头不算挥舞类近战），再用 <see cref="CanRoll"/> 限定；
/// 重铸、合成时和其他前缀一样随机出现。
/// </summary>
public sealed class Resonant : ModPrefix
{
    public override PrefixCategory Category => PrefixCategory.AnyWeapon;

    public override bool CanRoll(Item item) => item.pick > 0;

    public override IEnumerable<TooltipLine> GetTooltipLines(Item item)
    {
        yield return new TooltipLine(Mod, "HexResonant", Language.GetTextValue("Mods.HexCastingTerraria.Prefixes.Resonant.Effect"))
        {
            IsModifier = true,
        };
    }

    /// <summary>这件物品是不是带共振的镐子。</summary>
    public static bool On(Item? item) => item is { IsAir: false } && item.pick > 0 && item.prefix == ModContent.PrefixType<Resonant>();
}
