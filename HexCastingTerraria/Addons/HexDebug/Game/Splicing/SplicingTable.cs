using System.Collections.Generic;
using System.IO;
using System.Linq;
using HexCastingTerraria.Addons.HexDebug.Core.Splicing;
using HexCastingTerraria.Config;
using HexCastingTerraria.Content.Items;
using HexCastingTerraria.Content.Net;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Media;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;

namespace HexCastingTerraria.Addons.HexDebug.Game.Splicing;

/// <summary>剪接台 / 制念台（上游 blocks/splicing/SplicingTableBlock.kt）：1×1，右键打开编辑界面。</summary>
public abstract class SplicingTableTileBase : AddonTile
{
    public override string AddonId => "hexdebug";

    public abstract bool Enlightened { get; }

    public override string Texture => HexDebugArt.Dir + (Enlightened ? "EnlightenedSplicingTable" : "SplicingTable");

    public override void SetStaticDefaults()
    {
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileFrameImportant[Type] = true;
        Main.tileContainer[Type] = true;
        Main.tileNoFail[Type] = false;
        DustType = DustID.PurpleTorch;
        HitSound = SoundID.Tink;
        AddMapEntry(Enlightened ? new Color(120, 80, 170) : new Color(90, 70, 110));

        TileObjectData.newTile.CopyFrom(TileObjectData.Style1x1);
        TileObjectData.newTile.HookPostPlaceMyPlayer = new PlacementHook(
            ModContent.GetInstance<SplicingTableEntity>().Hook_AfterPlacement, -1, 0, false);
        TileObjectData.addTile(Type);
    }

    public override bool RightClick(int i, int j)
    {
        if (SplicingTableEntity.FindAt(i, j) is null) return false;
        SplicingTableUI.Open(new Point16(i, j));
        return true;
    }

    public override void MouseOver(int i, int j)
    {
        var p = Main.LocalPlayer;
        p.noThrow = 2;
        p.cursorItemIconEnabled = true;
        p.cursorItemIconID = Enlightened ? ModContent.ItemType<EnlightenedSplicingTableItem>() : ModContent.ItemType<SplicingTableItem>();
    }

    /// <summary>拆掉时把里面的物品掉出来（上游 Containers.dropContents）。</summary>
    public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (fail || effectOnly || Main.netMode == NetmodeID.MultiplayerClient) return;
        if (SplicingTableEntity.FindAt(i, j) is not { } te) return;
        // 方块换成另一种剪接台（脑叶切除升级成制念台）时不掉东西：同一个实体接着用
        foreach (var item in te.Slots)
        {
            if (item.IsAir) continue;
            Item.NewItem(new EntitySource_TileBreak(i, j), i * 16, j * 16, 16, 16, item.Clone());
        }
        te.Kill(i, j);
    }
}

public sealed class SplicingTableTile : SplicingTableTileBase
{
    public override bool Enlightened => false;
}

public sealed class EnlightenedSplicingTableTile : SplicingTableTileBase
{
    public override bool Enlightened => true;
}

public sealed class SplicingTableItem : AddonItem
{
    public override string AddonId => "hexdebug";

    public override string Texture => HexDebugArt.Dir + "SplicingTableItem";

    public override void SetDefaults()
    {
        Item.DefaultToPlaceableTile(ModContent.TileType<SplicingTableTile>());
        Item.width = 16;
        Item.height = 16;
        Item.rare = ItemRarityID.Green;
        Item.value = Item.sellPrice(silver: 50);
    }

    // 上游 recipes/splicing_table.json（"PCP"/"AFA"/"SGS"）：启迪木板 ×2 + 充能紫水晶 + 紫水晶碎片 ×2 + 核心 + 板岩方块 ×2 + 金锭
    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient<Content.Tiles.EdifiedPlanksItem>(2)
            .AddIngredient<ChargedAmethyst>()
            .AddIngredient<AmethystShard>(2)
            .AddIngredient<Focus>()
            .AddRecipeGroup(HexRecipeGroups.SlateBlocks, 2)
            .AddIngredient(ItemID.GoldBar)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}

/// <summary>制念台：没有配方，脑叶切除（剪接台 + 哥布林工匠）得到（上游 brainsweep/enlightened_splicing_table）。</summary>
public sealed class EnlightenedSplicingTableItem : AddonItem
{
    public override string AddonId => "hexdebug";

    public override string Texture => HexDebugArt.Dir + "EnlightenedSplicingTableItem";

    public override void SetDefaults()
    {
        Item.DefaultToPlaceableTile(ModContent.TileType<EnlightenedSplicingTableTile>());
        Item.width = 16;
        Item.height = 16;
        Item.rare = ItemRarityID.Pink;
        Item.value = Item.sellPrice(gold: 2);
    }
}

/// <summary>槽位里的物品当作 iota 载体（核心、结念绳、法术书、卷轴……）。</summary>
internal sealed class ItemHolder : ISplicingHolder
{
    private readonly Item _item;

    public ItemHolder(Item item) => _item = item;

    public static ItemHolder? Of(Item item) => item is { IsAir: false, ModItem: ItemIotaStorage } ? new ItemHolder(item) : null;

    private ItemIotaStorage Storage => (ItemIotaStorage)_item.ModItem;

    public Iota? Read() => Storage.Read();

    public bool Writable => Storage.Writeable;

    public bool Write(Iota? value) => Storage.WriteIota(value, simulate: false);
}

/// <summary>
/// 剪接台的方块实体（上游 blocks/splicing/SplicingTableBlockEntity.kt 的游戏部分）：10 个槽（列表、剪贴板、媒质、法杖、储物 2×3）、
/// 媒质池、选区与视野、撤销栈、制念台融注的咒术。编辑在服务端（单机就是本地）做，做完同步给所有客户端。
/// </summary>
public sealed class SplicingTableEntity : AddonTileEntity
{
    public override string AddonId => "hexdebug";

    public const int SlotList = 0;
    public const int SlotClipboard = 1;
    public const int SlotMedia = 2;
    public const int SlotStaff = 3;
    public const int StorageStart = 4;
    public const int SlotCount = 10;

    public Item[] Slots { get; } = Enumerable.Range(0, SlotCount).Select(_ => new Item()).ToArray();

    public SplicingTableState State { get; } = new();

    public long Media { get; private set; }

    /// <summary>制念台融注的咒术（上游 hexTag）；剪接台永远为 null。</summary>
    public List<Iota>? Hex { get; set; }

    /// <summary>客户端用来判断撤销 / 重做按钮（撤销栈只在服务端）。</summary>
    public int UndoSize { get; private set; }
    public int UndoIndex { get; private set; } = -1;

    private int _castingCooldown;
    private bool _isCasting;

    private static HexDebugOptions Options => HexAddonsConfig.Instance.HexDebugOptions;

    public static long MediaCost => Options.SplicingTableMediaCost;

    public static long MaxMedia => System.Math.Max(1, Options.SplicingTableMaxMedia);

    public bool Enlightened => Main.tile[Position.X, Position.Y].TileType == ModContent.TileType<EnlightenedSplicingTableTile>();

    public override bool IsTileValidForEntity(int x, int y)
    {
        var t = Main.tile[x, y];
        return t.HasTile && (t.TileType == ModContent.TileType<SplicingTableTile>() || t.TileType == ModContent.TileType<EnlightenedSplicingTableTile>());
    }

    public static SplicingTableEntity? FindAt(int x, int y)
        => TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te) ? te as SplicingTableEntity : null;

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

    // ==================== 槽位 ====================

    /// <summary>上游 canPlaceItem：列表 / 剪贴板放 iota 载体，媒质槽放媒质物品，法杖槽放法杖，储物随便放。</summary>
    public static bool CanPlace(int slot, Item item) => item.IsAir || slot switch
    {
        SlotList or SlotClipboard => item.ModItem is ItemIotaStorage,
        SlotMedia => item.ModItem is MediaMaterial or MediaFlask,
        SlotStaff => item.ModItem is HexStaff,
        _ => true,
    };

    /// <summary>服务端：放进 / 拿出某格（客户端点格子后发来的新内容）。</summary>
    public void SetSlot(int slot, Item item)
    {
        if (slot is < 0 or >= SlotCount || !CanPlace(slot, item)) return;
        Slots[slot] = item;
        if (slot == SlotList) State.ListStackChanged(item.IsAir, ReadList());
        if (slot == SlotClipboard) State.ClipboardStackChanged(item.IsAir);
        if (slot == SlotMedia) RefillMedia();
        Sync();
    }

    public List<Iota>? ReadList() => ItemHolder.Of(Slots[SlotList])?.Read() is ListIota l ? l.Items.ToList() : null;

    // ==================== 编辑（服务端） ====================

    private SplicingTableData Data(Player? player)
    {
        State.UndoStack.MaxSize = Options.MaxUndoStackSize;
        var caster = player is null ? null : new EntityIota(EntityIota.EntityKind.Player, player.whoAmI);
        return State.Data(player is not null, ItemHolder.Of(Slots[SlotList]), ItemHolder.Of(Slots[SlotClipboard]), values =>
        {
            try
            {
                foreach (var v in values) MishapOthersName.ThrowIfTrueName(v, caster, allowSelf: true);
                return true;
            }
            catch (MishapOthersName)
            {
                return false;
            }
        });
    }

    public void RunAction(Player player, SplicingTableAction action)
    {
        if (State.RunAction(action, Data(player), Media, MediaCost)) ConsumeMedia();
        Sync();
    }

    /// <summary>上游 drawPattern：剪接台的小画布上画了一个图案。返回给画布上色的结果。</summary>
    public ResolvedPatternType DrawPattern(Player player, HexPattern pattern)
    {
        var (type, consume) = State.DrawPattern(pattern, Data(player), Media, MediaCost);
        if (consume) ConsumeMedia();
        Sync();
        return type;
    }

    public void SelectIndex(Player player, int index, bool shift, bool isIota)
    {
        State.SelectIndex(ReadList(), index, shift, isIota);
        Sync();
    }

    /// <summary>客户端看到的样子（槽位物品已同步过来，客户端自己算）。</summary>
    public SplicingClientView ClientView()
    {
        var data = State.Data(true, ItemHolder.Of(Slots[SlotList]), ItemHolder.Of(Slots[SlotClipboard]));
        var view = State.ClientView(data, Enlightened, Hex is not null);
        return new SplicingClientView
        {
            List = view.List,
            Clipboard = view.Clipboard,
            IsListWritable = view.IsListWritable,
            IsClipboardWritable = view.IsClipboardWritable,
            IsEnlightened = view.IsEnlightened,
            HasHex = view.HasHex,
            UndoSize = UndoSize,
            UndoIndex = UndoIndex,
        };
    }

    // ==================== 媒质 ====================

    private void ConsumeMedia()
    {
        Media = System.Math.Clamp(Media - MediaCost, 0, MaxMedia);
        RefillMedia();
    }

    /// <summary>
    /// 上游 refillMedia：从媒质槽里补到上限。粉、碎片、晶体一个一个扣，只扣装得下的（不浪费）；媒质之瓶抽到满为止。
    /// 施法中不补（不让咒术借机取用）。
    /// </summary>
    public void RefillMedia()
    {
        if (_isCasting || Media >= MaxMedia) return;
        var item = Slots[SlotMedia];
        switch (item.ModItem)
        {
            case MediaMaterial mat:
                while (item.stack > 0 && Media + mat.MediaValue <= MaxMedia)
                {
                    item.stack--;
                    Media += mat.MediaValue;
                }
                if (item.stack <= 0) item.TurnToAir();
                break;
            case MediaFlask flask:
                Media += flask.Withdraw(MaxMedia - Media);
                break;
        }
    }

    public override void Update()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        if (_castingCooldown > 0) _castingCooldown--;
        long before = Media;
        int stackBefore = Slots[SlotMedia].stack;
        RefillMedia();
        if (Media != before || Slots[SlotMedia].stack != stackBefore) Sync();
    }

    // ==================== 制念台：施放融注的咒术（上游 castHex） ====================

    public void CastHex(Player player)
    {
        if (!Enlightened || Media < MediaCost || _castingCooldown > 0 || Hex is null) return;
        // 先扣媒质，再让咒术去碰媒质
        ConsumeMedia();
        var env = new SplicingTableCastEnv(player, this);
        var vm = global::HexCastingTerraria.Core.Casting.Eval.Vm.CastingVM.Empty(env);
        _isCasting = true;
        global::HexCastingTerraria.Core.Casting.Eval.Vm.CastOutcome outcome;
        try
        {
            outcome = vm.QueueExecute(vm.Image, Hex);
        }
        finally
        {
            _isCasting = false;
        }
        RefillMedia();
        Content.SpellVisuals.Broadcast(outcome.Particles, player);
        // 上游 SplicingTableCastEnv.postCast：最响的那个求值音效在台子处播
        Content.SpellSounds.EmitEval(outcome.Sound, new Vector2(Position.X * 16 + 8, Position.Y * 16 + 8));
        _castingCooldown = Options.SplicingTableCastingCooldown;
        Sync();
    }

    /// <summary>剪接图案里写进来的施放用媒质（上游 ADMediaHolder.withdrawMedia 等经由 SplicingTableCastEnv）。</summary>
    public long WithdrawForCast(long cost, bool simulate)
    {
        long taken = System.Math.Min(cost, Media);
        if (!simulate) Media -= taken;
        return cost - taken;
    }

    // ==================== 存档 / 同步 ====================

    public void Sync()
    {
        UndoSize = State.UndoStack.Size;
        UndoIndex = State.UndoStack.Index;
        // 制念台：融注了咒术就亮（上游 IMBUED 属性）
        if (Enlightened)
        {
            var t = Main.tile[Position.X, Position.Y];
            short fx = (short)(Hex is null ? 0 : 18);
            if (t.TileFrameX != fx)
            {
                t.TileFrameX = fx;
                if (Main.netMode == NetmodeID.Server) NetMessage.SendTileSquare(-1, Position.X, Position.Y, 1);
            }
        }
        if (Main.netMode == NetmodeID.Server) NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, ID, Position.X, Position.Y);
    }

    public override void SaveData(TagCompound tag)
    {
        tag["items"] = Slots.Select(ItemIO.Save).ToList();
        tag["media"] = Media;
        tag["selectionFrom"] = State.Selection?.From ?? -1;
        tag["selectionTo"] = State.Selection?.To ?? -1;
        tag["viewStartIndex"] = State.ViewStartIndex;
        if (Hex is not null) tag["hex"] = IotaTag.ToTag(new ListIota(Hex));
    }

    public override void LoadData(TagCompound tag)
    {
        var items = tag.GetList<TagCompound>("items");
        for (int i = 0; i < SlotCount; i++) Slots[i] = i < items.Count ? ItemIO.Load(items[i]) : new Item();
        Media = tag.GetLong("media");
        State.Selection = Selection.FromRawIndices(tag.ContainsKey("selectionFrom") ? tag.GetInt("selectionFrom") : -1,
            tag.ContainsKey("selectionTo") ? tag.GetInt("selectionTo") : -1);
        State.ViewStartIndex = tag.GetInt("viewStartIndex");
        Hex = tag.TryGet("hex", out TagCompound hex) && IotaTag.TryFromTag(hex, out var iota) && iota is ListIota l ? l.Items.ToList() : null;
    }

    public override void NetSend(BinaryWriter writer)
    {
        foreach (var item in Slots) ItemIO.Send(item, writer, writeStack: true);
        writer.Write(Media);
        writer.Write(State.Selection?.From ?? -1);
        writer.Write(State.Selection?.To ?? -1);
        writer.Write(State.ViewStartIndex);
        writer.Write(UndoSize);
        writer.Write(UndoIndex);
        writer.Write(Hex is not null);
    }

    public override void NetReceive(BinaryReader reader)
    {
        for (int i = 0; i < SlotCount; i++) Slots[i] = ItemIO.Receive(reader, readStack: true);
        Media = reader.ReadInt64();
        int from = reader.ReadInt32();
        int to = reader.ReadInt32();
        State.Selection = Selection.FromRawIndices(from, to);
        State.ViewStartIndex = reader.ReadInt32();
        UndoSize = reader.ReadInt32();
        UndoIndex = reader.ReadInt32();
        // 客户端只需要知道「有没有融注」；内容只在服务端用
        bool hasHex = reader.ReadBoolean();
        if (Main.netMode == NetmodeID.MultiplayerClient) Hex = hasHex ? new List<Iota>() : null;
    }
}
