using HexCastingTerraria.Client;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 咒法学之书。对应源项目的 `hexcasting:thehexbook`（Patchouli 写的那本引导书）。
///
/// 右键打开/关闭。书里列出全部 188 条图案的**图形**（这是唯一能教人画图案的方式），
/// 以及每条的参数个数、媒质消耗、是否需要启蒙、在泰拉侧是否已实现。
///
/// 与源项目的差异：原版那本书是**散文式教程**（一章章讲机制、配插图），
/// 这里只有**图案目录**。原因是原版的文字内容在源码的 `patchouli_books` 资源里
/// （英文 JSON），逐章翻译既超出移植范围、也不属于「机制」。
/// 机制类的说明写在仓库的 `*.md` 文档里。
/// </summary>
public sealed class HexBookItem : ModItem
{
    public override void SetDefaults()
    {
        Item.width = 20;
        Item.height = 20;
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.useTurn = true;
        Item.autoReuse = false;
        Item.maxStack = 1;
        Item.consumable = false;
        Item.noMelee = true;
        Item.rare = ItemRarityID.LightPurple;
        Item.value = Item.sellPrice(gold: 1);
        Item.UseSound = null;   // 翻书音效由界面自己做，见 HexClientSystem
    }

    public override bool? UseItem(Player player)
    {
        // 界面是纯客户端的：服务端不做任何事
        if (player.whoAmI != Main.myPlayer) return true;

        HexCanvasState.Book.Toggle();

        if (HexCanvasState.Book.IsOpen)
        {
            // 开书时把画布关掉 —— 两个界面叠在一起既看不清也没法操作
            HexCanvasState.Canvas.Close();
            HexCanvasState.SetMessage(null);
        }

        return true;
    }

    public override void AddRecipes()
    {
        // 用户指定的配方：**10 木板 + 10 稻草**。
        //
        // 材料便宜是有意的：书是**开局就送**的，这条配方只是「丢了以后能补回来」的兜底 ——
        // 兜底配方不该比正常获取更难，否则玩家弄丢书之后等于卡死。
        // 稻草（Hay）在泰拉里割草就有，木材更是随处可得。
        //
        // ⚠️ 合成站是**后加的**：原先这里刻意不设站，理由是「兜底配方应该最省事」。
        // 但用户指出「泰拉的合成是有工具的，有的东西不该在背包里直接做」——
        // 书这类"知识性"物品确实该在**书架**上做，这符合泰拉惯例，
        // 也不会让兜底变难：书架本身就是木材就能做的早期站。
        CreateRecipe()
            .AddIngredient(ItemID.Wood, 10)
            .AddIngredient(ItemID.Hay, 10)
            .AddTile(TileID.Bookcases)      // 书架
            .Register();
    }
}
