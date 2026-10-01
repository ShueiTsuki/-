using System.Collections.Generic;
using System.IO;
using HexCastingTerraria.Content.Items;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;
using Terraria.Enums;

namespace HexCastingTerraria.Addons.HexDebug.Game.Splicing;

/// <summary>
/// 核心框架（上游 blocks/focusholder/）：在世界里放一个 iota 载体（核心、法术书……）。
/// 拿着载体（或空手）右键 = 和里面的交换；里面有东西就亮着。剪接图案（放映员）也能对它用。
/// </summary>
public sealed class FocusHolderTile : AddonTile
{
    public override string AddonId => "hexdebug";

    public override string Texture => HexDebugArt.Dir + "FocusHolder";

    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        Main.tileContainer[Type] = true;
        Main.tileSolid[Type] = false;
        Main.tileNoAttach[Type] = true;
        DustType = DustID.Stone;
        HitSound = SoundID.Tink;
        AddMapEntry(new Color(90, 80, 100));
        TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
        TileObjectData.newTile.AnchorBottom = new AnchorData(AnchorType.SolidTile | AnchorType.SolidWithTop | AnchorType.Table, 1, 0);
        TileObjectData.newTile.HookPostPlaceMyPlayer = new PlacementHook(
            ModContent.GetInstance<FocusHolderEntity>().Hook_AfterPlacement, -1, 0, false);
        TileObjectData.addTile(Type);
    }

    public override void MouseOver(int i, int j)
    {
        var p = Main.LocalPlayer;
        p.noThrow = 2;
        p.cursorItemIconEnabled = true;
        var te = FocusHolderEntity.FindAt(i, j);
        p.cursorItemIconID = te is { Item.IsAir: false } ? te.Item.type : ModContent.ItemType<FocusHolderItem>();
    }

    /// <summary>上游 use：手上是能放的载体或空手 → 交换；手上是别的东西不理。</summary>
    public override bool RightClick(int i, int j)
    {
        var te = FocusHolderEntity.FindAt(i, j);
        var player = Main.LocalPlayer;
        if (te is null) return false;
        var held = player.inventory[player.selectedItem];
        if (!held.IsAir && !FocusHolderEntity.IsValid(held)) return false;
        var stored = te.Item.Clone();
        var put = held.Clone();
        player.inventory[player.selectedItem] = stored;
        if (Main.netMode == NetmodeID.SinglePlayer) te.SetItem(put);
        else
        {
            NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, player.selectedItem);
            SplicingTableNet.SetFocusHolder(i, j, put);
        }
        Terraria.Audio.SoundEngine.PlaySound(SoundID.Grab);
        return true;
    }

    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly || Main.netMode == NetmodeID.MultiplayerClient) return;
        if (FocusHolderEntity.FindAt(i, j) is not { } te) return;
        if (!te.Item.IsAir) Item.NewItem(new EntitySource_TileBreak(i, j), i * 16, j * 16, 16, 16, te.Item.Clone());
        te.Kill(i, j);
    }

    /// <summary>放下装着东西的核心框架（配方做出来的）时，把里面的东西放进去。</summary>
    public override void PlaceInWorld(int i, int j, Item item)
    {
        if (item.ModItem is not FocusHolderItem { Inner: { IsAir: false } inner }) return;
        if (Main.netMode == NetmodeID.SinglePlayer) FocusHolderEntity.FindAt(i, j)?.SetItem(inner.Clone());
        else SplicingTableNet.SetFocusHolder(i, j, inner.Clone());
    }
}

/// <summary>核心框架的物品；配方「核心框架 + 核心的材料」做出来的里面已经装着一个新核心（上游 FocusHolderBlockItem）。</summary>
public sealed class FocusHolderItem : AddonItem
{
    public override string AddonId => "hexdebug";

    public override string Texture => HexDebugArt.Dir + "FocusHolderItem";

    public Item? Inner { get; set; }

    public override void SetDefaults()
    {
        Item.DefaultToPlaceableTile(ModContent.TileType<FocusHolderTile>());
        Item.width = 16;
        Item.height = 16;
        Item.maxStack = 1;
        Item.rare = ItemRarityID.Green;
        Item.value = Item.sellPrice(silver: 20);
    }

    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        if (Inner is { IsAir: false } inner)
        {
            tooltips.Add(new TooltipLine(Mod, "HexDebugFocusHolder", Terraria.Localization.Language.GetTextValue(
                "Mods.HexCastingTerraria.HexDebug.FocusHolderItemIs", inner.Name)));
        }
    }

    // 上游 recipes/focus_holder.json（"GSG"/"S S"/"GSG"）：金粒 ×4 + 板岩方块 ×4（泰拉没有金粒，用一个金锭）
    public override void AddRecipes()
    {
        CreateRecipe()
            .AddRecipeGroup(HexRecipeGroups.SlateBlocks, 4)
            .AddIngredient(ItemID.GoldBar)
            .AddTile(TileID.WorkBenches)
            .Register();

        // 上游 focus_holder_filling_shaped/focus：核心框架 + 核心的材料 → 装着新核心的框架（材料照本体核心的配方）
        CreateRecipe()
            .AddIngredient<FocusHolderItem>()
            .AddIngredient(ItemID.FallenStar, 4)
            .AddIngredient(ItemID.Silk, 4)
            .AddIngredient<ChargedAmethyst>()
            .AddTile(TileID.WorkBenches)
            .AddOnCraftCallback((r, item, consumed, dest) =>
            {
                var focus = new Item(ModContent.ItemType<Focus>());
                if (item.ModItem is FocusHolderItem h) h.Inner = focus;
            })
            .Register();
    }

    public override void SaveData(TagCompound tag)
    {
        if (Inner is { IsAir: false }) tag["inner"] = ItemIO.Save(Inner);
    }

    public override void LoadData(TagCompound tag) => Inner = tag.TryGet("inner", out TagCompound t) ? ItemIO.Load(t) : null;

    public override void NetSend(BinaryWriter writer)
    {
        writer.Write(Inner is { IsAir: false });
        if (Inner is { IsAir: false }) ItemIO.Send(Inner, writer, writeStack: true);
    }

    public override void NetReceive(BinaryReader reader) => Inner = reader.ReadBoolean() ? ItemIO.Receive(reader, readStack: true) : null;
}

public sealed class FocusHolderEntity : AddonTileEntity
{
    public override string AddonId => "hexdebug";

    public Item Item { get; private set; } = new();

    /// <summary>上游 isValidItem：iota 载体。</summary>
    public static bool IsValid(Item item) => item.ModItem is ItemIotaStorage;

    public override bool IsTileValidForEntity(int x, int y)
        => Main.tile[x, y].HasTile && Main.tile[x, y].TileType == ModContent.TileType<FocusHolderTile>();

    public static FocusHolderEntity? FindAt(int x, int y)
        => TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te) ? te as FocusHolderEntity : null;

    public override int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            NetMessage.SendTileSquare(Main.myPlayer, i, j, 1);
            NetMessage.SendData(MessageID.TileEntityPlacement, number: i, number2: j, number3: Type);
            return -1;
        }
        return Place(i, j);
    }

    public void SetItem(Item item)
    {
        if (!item.IsAir && !IsValid(item)) return;
        Item = item;
        Sync();
    }

    public void Sync()
    {
        // 里面有东西就亮（上游 HAS_ITEM）
        var t = Main.tile[Position.X, Position.Y];
        short fx = (short)(Item.IsAir ? 0 : 18);
        if (t.TileFrameX != fx)
        {
            t.TileFrameX = fx;
            if (Main.netMode == NetmodeID.Server) NetMessage.SendTileSquare(-1, Position.X, Position.Y, 1);
        }
        if (Main.netMode == NetmodeID.Server) NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, ID, Position.X, Position.Y);
    }

    public override void SaveData(TagCompound tag)
    {
        if (!Item.IsAir) tag["item"] = ItemIO.Save(Item);
    }

    public override void LoadData(TagCompound tag) => Item = tag.TryGet("item", out TagCompound t) ? ItemIO.Load(t) : new Item();

    public override void NetSend(BinaryWriter writer) => ItemIO.Send(Item, writer, writeStack: true);

    public override void NetReceive(BinaryReader reader) => Item = ItemIO.Receive(reader, readStack: true);
}
