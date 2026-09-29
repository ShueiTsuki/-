using System.Collections.Generic;
using HexCastingTerraria.Core.Registry;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace HexCastingTerraria.Content;

/// <summary>
/// 往世界里的箱子塞原版的三种战利品（源项目 HexLootHandler）：
///
///   - **远古卷轴**：写着本世界某个大法术的笔顺。每个箱子 max(随机(−范围, 范围), 0) 个（getScrollCount）；
///   - **故事残卷**：40% 概率（DEFAULT_LORE_CHANCE），读了解锁一篇传说；
///   - **远古杂件**：40% 概率（DEFAULT_CYPHER_CHANCE），封着预设咒术的一次性符纸。
///
/// MC 结构 → 泰拉箱子（按「稀有程度 / 探索难度」对应，范围照抄原版）：
///
/// | 原版箱子 | 卷轴范围 | 泰拉箱子 |
/// |---|---|---|
/// | simple_dungeon / abandoned_mineshaft | 1 | 木箱、金箱（洞穴）、蛛网箱、冰冻箱、蘑菇箱、花岗岩 / 大理石箱 |
/// | nether_bridge | 1 | 暗影箱（未上锁） |
/// | jungle_temple / desert_pyramid / village | 2 | 常春藤箱（地下丛林）、砂岩箱（地下沙漠）、生命木箱 |
/// | shipwreck / end_city / bastion_treasure | 3 | 水中箱、天域箱（浮空岛）、上锁的暗影箱（地狱） |
/// | ancient_city / pillager_outpost | 4 | 丛林蜥蜴箱（神庙） |
/// | woodland_mansion / stronghold_library | 5 | 上锁的金箱（地牢） |
///
/// 新世界在生成时放；已有的世界在第一次加载时补一次（世界存档记一个标记，不会重复）。
/// </summary>
public sealed class HexChestLoot : ModSystem
{
    private bool _injected;

    private const double LoreChance = 0.4;
    private const double CypherChance = 0.4;

    private enum ChestKind { None, Common, Shadow, Jungle, Desert, LivingWood, Water, Sky, LockedShadow, Lihzahrd, Dungeon }

    private static ChestKind Classify(int x, int y)
    {
        var t = Main.tile[x, y];
        int style = t.TileFrameX / 36;
        if (t.TileType == TileID.Containers)
        {
            return style switch
            {
                0 or 1 or 11 or 15 or 32 or 50 or 51 => ChestKind.Common,
                2 => ChestKind.Dungeon,
                3 => ChestKind.Shadow,
                4 => ChestKind.LockedShadow,
                10 => ChestKind.Jungle,
                12 => ChestKind.LivingWood,
                13 => ChestKind.Sky,
                16 => ChestKind.Lihzahrd,
                17 => ChestKind.Water,
                _ => ChestKind.None,
            };
        }
        if (t.TileType == TileID.Containers2 && style == 10)
        {
            return ChestKind.Desert;   // 砂岩箱
        }
        return ChestKind.None;
    }

    private static int ScrollRange(ChestKind k) => k switch
    {
        ChestKind.Common or ChestKind.Shadow => 1,
        ChestKind.Jungle or ChestKind.Desert or ChestKind.LivingWood => 2,
        ChestKind.Water or ChestKind.Sky or ChestKind.LockedShadow => 3,
        ChestKind.Lihzahrd => 4,
        ChestKind.Dungeon => 5,
        _ => 0,
    };

    /// <summary>原版 DEFAULT_LORE_INJECTS：地牢、矿井、村庄、要塞图书馆、林地府邸、掠夺者前哨。</summary>
    private static bool HasLore(ChestKind k) => k is ChestKind.Common or ChestKind.LivingWood or ChestKind.Dungeon or ChestKind.Lihzahrd;

    /// <summary>原版 DEFAULT_CYPHER_INJECTS：地牢、矿井、要塞走廊、丛林神庙、沙漠神殿、远古城市、下界要塞。</summary>
    private static bool HasCypher(ChestKind k) => k is ChestKind.Common or ChestKind.Dungeon or ChestKind.Jungle
        or ChestKind.Desert or ChestKind.Lihzahrd or ChestKind.Shadow or ChestKind.LockedShadow;

    public override void PostWorldGen()
    {
        Inject(WorldGen.genRand.Next());
        _injected = true;
    }

    public override void OnWorldLoad()
    {
        if (_injected || Main.netMode == NetmodeID.MultiplayerClient) return;
        Inject(Main.rand.Next());
        _injected = true;
    }

    public override void ClearWorld() => _injected = false;

    public override void SaveWorldData(TagCompound tag) => tag["hexLootInjected"] = _injected;

    public override void LoadWorldData(TagCompound tag) => _injected = tag.GetBool("hexLootInjected");

    private static void Inject(int seed)
    {
        var rand = new System.Random(seed);
        var perWorld = new List<string>(PatternRegistry.PerWorldIds);
        perWorld.Sort(System.StringComparer.Ordinal);
        for (int i = 0; i < Main.maxChests; i++)
        {
            var chest = Main.chest[i];
            if (chest is null || !WorldGen.InWorld(chest.x, chest.y)) continue;
            var kind = Classify(chest.x, chest.y);
            if (kind == ChestKind.None) continue;

            var loot = new List<Item>();
            int range = ScrollRange(kind);
            int scrolls = System.Math.Max(rand.Next(-range, range + 1), 0);   // getScrollCount
            for (int s = 0; s < scrolls; s++)
            {
                var item = new Item(ModContent.ItemType<Items.AncientScroll>());
                (item.ModItem as Items.AncientScroll)!.SetOp(perWorld[rand.Next(perWorld.Count)]);
                loot.Add(item);
            }
            if (HasLore(kind) && rand.NextDouble() < LoreChance)
            {
                loot.Add(new Item(ModContent.ItemType<Items.LoreFragment>()));
            }
            if (HasCypher(kind) && rand.NextDouble() < CypherChance)
            {
                var item = new Item(ModContent.ItemType<Items.AncientCypher>());
                (item.ModItem as Items.AncientCypher)!.Roll(rand);
                loot.Add(item);
            }

            foreach (var item in loot)
            {
                for (int slot = 0; slot < chest.item.Length; slot++)
                {
                    if (chest.item[slot] is null || chest.item[slot].IsAir)
                    {
                        chest.item[slot] = item;
                        break;
                    }
                }
            }
        }
    }
}
