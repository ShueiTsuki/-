using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Tiles;

/// <summary>
/// 淬灵块的掉落（原版 loot_tables/blocks/quenched_allay.json）：
/// 精准采集掉方块本身，否则掉 **2~4 片淬灵晶碎片**（时运每级再 +1 的概率）。
/// 泰拉没有精准采集和时运 —— 一律掉 2~4 片。方块本身用 4 片合成（见 gen_deco_blocks.ps1）。
///
/// 放在 GlobalTile 里是因为 QuenchedAllay 这个类由脚本生成（DecoBlocks.Generated.cs），不手改。
/// </summary>
public sealed class QuenchedAllayDrops : GlobalTile
{
    public override bool CanDrop(int i, int j, int type)
        => type != ModContent.TileType<QuenchedAllay>();

    public override void Drop(int i, int j, int type)
    {
        if (type != ModContent.TileType<QuenchedAllay>()) return;
        Item.NewItem(new EntitySource_TileBreak(i, j), i * 16, j * 16, 16, 16,
            ModContent.ItemType<Items.QuenchedAllayShard>(), WorldGen.genRand.Next(2, 5));
    }
}
