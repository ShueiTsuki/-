using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Addons.HexDebug.Core.Splicing;
using HexCastingTerraria.Content;
using HexCastingTerraria.Content.Items;
using HexCastingTerraria.Core.Casting.Castables;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Eval.Mishaps;
using HexCastingTerraria.Core.Casting.Eval.SideEffects;
using HexCastingTerraria.Core.Casting.Eval.Vm;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Media;
using Terraria;
using Terraria.Localization;

namespace HexCastingTerraria.Addons.HexDebug.Game.Splicing;

/// <summary>
/// 剪接台的 16 个图案（上游 casting/actions/splicing/*）：从咒术里读写剪接台的选区、视野、剪贴板、
/// 列表 / 剪贴板里法术书的页码、制念台融注的咒术。第一个参数都是方块的位置。
/// </summary>
internal static class SplicingPatterns
{
    private static string M(string key) => Language.GetTextValue("Mods.HexCastingTerraria.HexDebug.BadBlock." + key);

    /// <summary>上游 getBlockPos + assertPosInRange：位置、范围、对应的图格。</summary>
    public static (double X, double Y, int TileX, int TileY) Pos(IReadOnlyList<Iota> args, CastingEnvironment env)
    {
        var (x, y, z) = CastingEnvironment.RequireVec3(args[0], "位置");
        env.AssertVecInRange(Math.Floor(x) + 0.5, Math.Floor(y) + 0.5, z);
        var (tx, ty) = HexSpaceWorld.TileOf(x, y);
        return (x, y, tx, ty);
    }

    public static SplicingTableEntity Table(double x, double y, int tx, int ty)
        => SplicingTableEntity.FindAt(tx, ty) ?? throw new MishapBadBlock(x, y, M("SplicingTable"));

    /// <summary>上游 getPositiveIntOrNull：null 或非负整数。</summary>
    public static int? PositiveIntOrNull(Iota iota) => iota switch
    {
        NullIota => null,
        DoubleIota d when IsInt(d.Value) && d.Value >= 0 => (int)Math.Round(d.Value),
        _ => throw new MishapInvalidIota(iota, M("PositiveOrNull")),
    };

    public static int PositiveInt(Iota iota) => iota is DoubleIota d && IsInt(d.Value) && d.Value >= 0
        ? (int)Math.Round(d.Value)
        : throw new MishapInvalidIota(iota, M("PositiveInt"));

    private static bool IsInt(double v) => Math.Abs(v - Math.Round(v)) < 1e-4 && Math.Abs(v) < int.MaxValue;

    public static IReadOnlyList<ParticleSpray> Sparks(double x, double y)
        => new[] { ParticleSpray.Burst(Math.Floor(x) + 0.5, Math.Floor(y) + 0.5, spread: 0.25f, count: 40) };

    /// <summary>只做一次世界改动的法术效果。</summary>
    public sealed class Effect : IRenderedSpell
    {
        private readonly Action _run;

        public Effect(Action run) => _run = run;

        public CastingImage? Cast(CastingEnvironment env, CastingImage image)
        {
            if (Main.netMode != Terraria.ID.NetmodeID.MultiplayerClient) _run();
            return null;
        }
    }

    public static SpellResult Result(Action run, long cost, double x, double y)
        => new() { Effect = new Effect(run), Cost = cost, Particles = Sparks(x, y) };

    /// <summary>上游 OpReadSpellbookIndex.getSpellbook：剪接台的列表 / 剪贴板槽、或核心框架里的法术书（不能是空白的）。</summary>
    public static (Action Sync, Spellbook Book) Spellbook(double x, double y, int tx, int ty, bool useListItem)
    {
        Item stack;
        string notSpellbook;
        Action sync;
        if (SplicingTableEntity.FindAt(tx, ty) is { } table)
        {
            stack = table.Slots[useListItem ? SplicingTableEntity.SlotList : SplicingTableEntity.SlotClipboard];
            notSpellbook = useListItem ? M("TableOrHolderSpellbook") : M("ClipboardSpellbook");
            sync = table.Sync;
        }
        else if (FocusHolderEntity.FindAt(tx, ty) is { } holder)
        {
            if (!useListItem) throw new MishapBadBlock(x, y, M("SplicingTable"));
            stack = holder.Item;
            notSpellbook = M("TableOrHolderSpellbook");
            sync = holder.Sync;
        }
        else
        {
            throw new MishapBadBlock(x, y, M("TableOrHolder"));
        }
        if (stack.ModItem is not Spellbook book || book.ArePagesEmpty) throw new MishapBadBlock(x, y, notSpellbook);
        return (sync, book);
    }

    public static ISplicingHolder Clipboard(SplicingTableEntity t, double x, double y, string mishapKey)
        => ItemHolder.Of(t.Slots[SplicingTableEntity.SlotClipboard]) ?? throw new MishapBadBlock(x, y, M(mishapKey));

    public static Dictionary<string, IAction> All() => new()
    {
        ["hexdebug:splicing/selection/read"] = new ReadSelection(),
        ["hexdebug:splicing/selection/write"] = new WriteSelection(),
        ["hexdebug:splicing/view_index/read"] = new ReadViewIndex(),
        ["hexdebug:splicing/view_index/write"] = new WriteViewIndex(),
        ["hexdebug:splicing/list/spellbook_index/read"] = new ReadSpellbookIndex(true),
        ["hexdebug:splicing/list/spellbook_index/write"] = new WriteSpellbookIndex(true),
        ["hexdebug:splicing/list/spellbook_index/readable"] = new ReadableSpellbookIndex(true),
        ["hexdebug:splicing/clipboard/read"] = new ReadClipboard(),
        ["hexdebug:splicing/clipboard/write"] = new WriteClipboard(),
        ["hexdebug:splicing/clipboard/readable"] = new ReadableClipboard(),
        ["hexdebug:splicing/clipboard/writable"] = new WritableClipboard(),
        ["hexdebug:splicing/clipboard/spellbook_index/read"] = new ReadSpellbookIndex(false),
        ["hexdebug:splicing/clipboard/spellbook_index/write"] = new WriteSpellbookIndex(false),
        ["hexdebug:splicing/clipboard/spellbook_index/readable"] = new ReadableSpellbookIndex(false),
        ["hexdebug:splicing/enlightened/hex/read"] = new ReadEnlightenedHex(),
        ["hexdebug:splicing/enlightened/hex/write"] = new WriteEnlightenedHex(),
    };

    // ==================== 选区 / 视野 ====================

    /// <summary>剪接器之分解：选区的 [起点, 终点（不含）]；光标只有起点；没有选区两个都是 null。</summary>
    private sealed class ReadSelection : ConstMediaAction
    {
        public override int Argc => 1;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            var sel = Table(x, y, tx, ty).State.Selection;
            Iota start = sel is null ? NullIota.Instance : new DoubleIota(sel.Start);
            Iota end = sel?.End is { } e ? new DoubleIota(e + 1) : NullIota.Instance;
            return new[] { start, end };
        }
    }

    /// <summary>剪接器之策略：两个都是 null 去掉选区；终点 null 放光标；否则选中 [小, 大)。</summary>
    private sealed class WriteSelection : SpellAction
    {
        public override int Argc => 3;

        public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            int? from = PositiveIntOrNull(args[1]);
            int? to = PositiveIntOrNull(args[2]);
            var table = Table(x, y, tx, ty);
            Selection? sel = from is not { } f ? null : to is not { } t ? Selection.Edge(f) : Selection.Range(Math.Min(f, t), Math.Max(f, t) - 1);
            return Result(() =>
            {
                table.State.Selection = sel;
                table.State.ClampView(table.ReadList());
                table.Sync();
            }, 0, x, y);
        }
    }

    private sealed class ReadViewIndex : ConstMediaAction
    {
        public override int Argc => 1;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            return new Iota[] { new DoubleIota(Table(x, y, tx, ty).State.ViewStartIndex) };
        }
    }

    private sealed class WriteViewIndex : SpellAction
    {
        public override int Argc => 2;

        public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            int index = PositiveInt(args[1]);
            var table = Table(x, y, tx, ty);
            return Result(() =>
            {
                table.State.ViewStartIndex = index;
                table.State.ClampView(table.ReadList());
                table.Sync();
            }, 0, x, y);
        }
    }

    // ==================== 法术书页码 ====================

    private sealed class ReadSpellbookIndex : ConstMediaAction
    {
        private readonly bool _list;

        public ReadSpellbookIndex(bool list) => _list = list;

        public override int Argc => 1;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            return new Iota[] { new DoubleIota(Spellbook(x, y, tx, ty, _list).Book.GetPage(1)) };
        }
    }

    private sealed class WriteSpellbookIndex : SpellAction
    {
        private readonly bool _list;

        public WriteSpellbookIndex(bool list) => _list = list;

        public override int Argc => 2;

        public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            int index = args[1] is DoubleIota d && IsInt(d.Value) && d.Value >= 1 && d.Value <= Content.Items.Spellbook.MaxPages
                ? (int)Math.Round(d.Value)
                : throw new MishapInvalidIota(args[1], string.Format(M("IntBetween"), 1, Content.Items.Spellbook.MaxPages));
            var (sync, book) = Spellbook(x, y, tx, ty, _list);
            return Result(() =>
            {
                book.SelectPage(index);
                sync();
            }, 0, x, y);
        }
    }

    private sealed class ReadableSpellbookIndex : ConstMediaAction
    {
        private readonly bool _list;

        public ReadableSpellbookIndex(bool list) => _list = list;

        public override int Argc => 1;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            try
            {
                Spellbook(x, y, tx, ty, _list);
                return new[] { BooleanIota.True };
            }
            catch (Mishap)
            {
                return new[] { BooleanIota.False };
            }
        }
    }

    // ==================== 剪贴板 ====================

    private sealed class ReadClipboard : ConstMediaAction
    {
        public override int Argc => 1;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            var holder = Clipboard(Table(x, y, tx, ty), x, y, "ClipboardRead");
            return new[] { holder.Read() ?? NullIota.Instance };
        }
    }

    private sealed class WriteClipboard : SpellAction
    {
        public override int Argc => 2;

        public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            var datum = args[1];
            var table = Table(x, y, tx, ty);
            var item = table.Slots[SplicingTableEntity.SlotClipboard];
            if (item.ModItem is not ItemIotaStorage storage || !storage.WriteIota(datum, simulate: true))
            {
                throw new MishapBadBlock(x, y, M("ClipboardWrite"));
            }
            // 上游 getTrueNameFromDatum(datum, null)：任何人的真名都不许写进剪贴板
            MishapOthersName.ThrowIfTrueName(datum, null, allowSelf: false);
            return Result(() =>
            {
                storage.WriteIota(datum, simulate: false);
                table.Sync();
            }, 0, x, y);
        }
    }

    private sealed class ReadableClipboard : ConstMediaAction
    {
        public override int Argc => 1;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            bool ok = SplicingTableEntity.FindAt(tx, ty) is { } t && ItemHolder.Of(t.Slots[SplicingTableEntity.SlotClipboard]) is not null;
            return new[] { ok ? BooleanIota.True : BooleanIota.False };
        }
    }

    private sealed class WritableClipboard : ConstMediaAction
    {
        public override int Argc => 1;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            bool ok = SplicingTableEntity.FindAt(tx, ty) is { } t && ItemHolder.Of(t.Slots[SplicingTableEntity.SlotClipboard]) is { Writable: true };
            return new[] { ok ? BooleanIota.True : BooleanIota.False };
        }
    }

    // ==================== 制念台 ====================

    private sealed class ReadEnlightenedHex : ConstMediaAction
    {
        public override int Argc => 1;

        public override IReadOnlyList<Iota> Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            var table = Table(x, y, tx, ty);
            if (!table.Enlightened) throw new MishapBadBlock(x, y, M("Enlightened"));
            return new[] { table.Hex is { } hex ? new ListIota(hex) : (Iota)NullIota.Instance };
        }
    }

    /// <summary>融注制念台：把一段咒术存进制念台（5 个充能紫水晶的媒质）。</summary>
    private sealed class WriteEnlightenedHex : SpellAction
    {
        public override int Argc => 2;

        public override SpellResult Execute(IReadOnlyList<Iota> args, CastingEnvironment env)
        {
            var (x, y, tx, ty) = Pos(args, env);
            var hex = args[1] is ListIota l ? l.Items.ToList() : throw new MishapInvalidIota(args[1], "列表");
            var table = Table(x, y, tx, ty);
            if (!table.Enlightened) throw new MishapBadBlock(x, y, M("Enlightened"));
            foreach (var iota in hex) MishapOthersName.ThrowIfTrueName(iota, null, allowSelf: false);
            return Result(() =>
            {
                table.Hex = hex;
                table.Sync();
            }, 5 * MediaConstants.CrystalUnit, x, y);
        }
    }
}
