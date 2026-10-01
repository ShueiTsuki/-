using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Addons.HexDebug.Core;
using HexCastingTerraria.Addons.HexDebug.Core.Splicing;
using HexCastingTerraria.Client;
using HexCastingTerraria.Client.UI;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.UI;

namespace HexCastingTerraria.Addons.HexDebug.Game.Splicing;

/// <summary>
/// 剪接台的界面（上游 gui/splicing/SplicingTableScreen.kt）：像箱子一样，打开时背包也开着，用泰拉的物品格子放东西。
/// 9 个 iota 格子（格子之间的缝可以点，放光标）、视野与编辑按钮（亮不亮照上游）、槽位、媒质条、导出、
/// 用法杖在大画布上画图案插进列表、制念台的施法按钮。滚轮滚动视野（潜行翻页、Ctrl 到头）。
/// </summary>
public sealed class SplicingTableUI : AddonSystem
{
    public override string AddonId => "hexdebug";

    public static Point16? OpenPos { get; private set; }

    /// <summary>正在用法杖往剪接台画图案（画布开着，图案送给剪接台）。</summary>
    private static bool _drawing;
    private static HexCanvas.Snapshot? _staffCanvas;

    private const float Cell = 46f;
    private const float Gap = 8f;
    private const float TextScale = 0.75f;

    private static readonly Color Back = new Color(25, 18, 40) * 0.92f;
    private static readonly Color Edge = new(120, 90, 190);
    private static readonly Color CellBack = new Color(50, 40, 75) * 0.9f;
    private static readonly Color Selected = new Color(150, 110, 230) * 0.6f;
    private static readonly Color Cursor = new(255, 220, 120);
    private static readonly Color Text = new(230, 225, 240);
    private static readonly Color Dim = new(140, 135, 155);
    private static readonly Color[] Brackets = { new(255, 120, 120), new(255, 200, 100), new(150, 230, 120), new(110, 200, 255), new(200, 140, 255) };

    private static string T(string key, params object[] args) => Language.GetTextValue("Mods.HexCastingTerraria.HexDebug.Splicing." + key, args);

    // ==================== 打开 / 关闭 ====================

    public static void Open(Point16 pos)
    {
        if (OpenPos == pos)
        {
            Close();
            return;
        }
        var p = Main.LocalPlayer;
        p.chest = -1;
        Main.npcChatText = "";
        Main.playerInventory = true;
        OpenPos = pos;
        Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuOpen);
    }

    public static void Close()
    {
        if (OpenPos is null) return;
        OpenPos = null;
        Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuClose);
    }

    private static SplicingTableEntity? Table => OpenPos is { } p ? SplicingTableEntity.FindAt(p.X, p.Y) : null;

    public override void PostUpdateInput()
    {
        if (OpenPos is not { } pos) return;
        if (_drawing) return;
        if (Table is null || !Main.playerInventory || Main.LocalPlayer.chest != -1 || Main.LocalPlayer.dead
            || !SplicingTableNet.InReach(Main.LocalPlayer, pos.X, pos.Y))
        {
            Close();
        }
    }

    public override void OnWorldUnload()
    {
        OpenPos = null;
        _drawing = false;
        _staffCanvas = null;
    }

    // ==================== 用法杖画图案 ====================

    /// <summary>上游：法杖槽里有法杖时，在剪接台里开一个施法界面，画的图案替换选区（或插在光标处）。</summary>
    private static void StartDrawing(Point16 pos)
    {
        var canvas = HexCanvasState.Canvas;
        if (canvas.IsOpen) HexCanvasState.CloseCanvas();
        _staffCanvas = canvas.TakeSnapshot();
        canvas.Reset();
        Content.Items.HexStaff.OpenCanvas();
        _drawing = true;
        HexCanvasState.HudStackOverride = Array.Empty<string>();
        HexCanvasState.PatternSink = rp =>
        {
            SplicingTableNet.Draw(pos.X, pos.Y, rp.Pattern);
            return true;
        };
        HexCanvasState.Closed -= OnCanvasClosed;
        HexCanvasState.Closed += OnCanvasClosed;
    }

    /// <summary>服务端回来的颜色（转义 = 放进去了，出错 = 没放进去）。</summary>
    public static void DrawResult(ResolvedPatternType type)
    {
        if (_drawing && HexCanvasState.Canvas.IsOpen) HexCanvasState.Canvas.ApplyResolution(type);
    }

    private static void OnCanvasClosed()
    {
        HexCanvasState.Closed -= OnCanvasClosed;
        var canvas = HexCanvasState.Canvas;
        canvas.Reset();
        if (_staffCanvas is { } staff) canvas.RestoreSnapshot(staff);
        _staffCanvas = null;
        _drawing = false;
        if (OpenPos is not null) Main.playerInventory = true;
    }

    // ==================== 绘制与点击 ====================

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(l => l.Name == "Vanilla: Inventory");
        if (index < 0) return;
        layers.Insert(index + 1, new LegacyGameInterfaceLayer("HexCastingTerraria: Splicing Table", () =>
        {
            if (OpenPos is { } pos && !_drawing && Main.playerInventory && Table is { } te) Draw(Main.spriteBatch, pos, te);
            return true;
        }, InterfaceScaleType.UI));
    }

    private static bool Clicked(Rectangle r)
    {
        if (!r.Contains(Main.mouseX, Main.mouseY)) return false;
        Main.LocalPlayer.mouseInterface = true;
        if (!(Main.mouseLeft && Main.mouseLeftRelease)) return false;
        Main.mouseLeftRelease = false;
        return true;
    }

    private static void Box(SpriteBatch sb, Rectangle r, Color fill, Color? border = null)
    {
        var px = TextureAssets.MagicPixel.Value;
        sb.Draw(px, r, fill);
        if (border is { } b)
        {
            sb.Draw(px, new Rectangle(r.X, r.Y, r.Width, 1), b);
            sb.Draw(px, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), b);
            sb.Draw(px, new Rectangle(r.X, r.Y, 1, r.Height), b);
            sb.Draw(px, new Rectangle(r.Right - 1, r.Y, 1, r.Height), b);
        }
    }

    private static void Label(SpriteBatch sb, string s, Vector2 at, Color c, float scale = TextScale)
        => Utils.DrawBorderString(sb, s, at, c, scale);

    private static void Draw(SpriteBatch sb, Point16 pos, SplicingTableEntity te)
    {
        var view = te.ClientView();
        var sel = te.State.Selection;
        int viewStart = te.State.ViewStartIndex;

        float x = 73f, y = Main.instance.invBottom + 4f;
        float width = SplicingTableData.IotaButtons * Cell + (SplicingTableData.IotaButtons + 1) * Gap + 16f;
        var panel = new Rectangle((int)x, (int)y, (int)width, 300);
        Box(sb, panel, Back, Edge);
        if (panel.Contains(Main.mouseX, Main.mouseY))
        {
            Main.LocalPlayer.mouseInterface = true;
            HandleScroll(pos);
        }

        // 标题与媒质条
        string title = Lang.GetItemNameValue(te.Enlightened
            ? Terraria.ModLoader.ModContent.ItemType<EnlightenedSplicingTableItem>()
            : Terraria.ModLoader.ModContent.ItemType<SplicingTableItem>());
        Label(sb, title, new Vector2(x + 10, y + 6), Text, 0.85f);
        var bar = new Rectangle((int)(x + width - 210), (int)y + 9, 150, 10);
        Box(sb, bar, new Color(30, 25, 45), Edge);
        float frac = (float)Math.Clamp((double)te.Media / SplicingTableEntity.MaxMedia, 0, 1);
        Box(sb, new Rectangle(bar.X + 1, bar.Y + 1, (int)((bar.Width - 2) * frac), bar.Height - 2), new Color(180, 120, 255));
        if (bar.Contains(Main.mouseX, Main.mouseY))
        {
            Main.instance.MouseText(T("MediaBar", global::HexCastingTerraria.Core.Media.MediaConstants.Format(te.Media), global::HexCastingTerraria.Core.Media.MediaConstants.Format(SplicingTableEntity.MaxMedia)));
        }

        // 导出（上游：把列表写成 .hexpattern 文本放进系统剪贴板）
        var export = new Rectangle(bar.Right + 8, (int)y + 4, 44, 20);
        bool canExport = view.List is not null;
        Box(sb, export, canExport ? CellBack : new Color(40, 35, 50), Edge);
        Label(sb, ".hex", new Vector2(export.X + 6, export.Y + 2), canExport ? Text : Dim, 0.7f);
        if (export.Contains(Main.mouseX, Main.mouseY)) Main.instance.MouseText(T("Button.Export"));
        if (canExport && Clicked(export))
        {
            ReLogic.OS.Platform.Get<ReLogic.OS.IClipboard>().Value = string.Join("\n", view.List!.Select(v => new string(' ', 4 * Math.Max(0, v.Depth)) + IotaText.Source(v.Iota)));
        }

        // iota 格子与缝
        float rowY = y + 32f;
        float cx = x + 8f + Gap;
        IotaHoverText? hover = null;
        for (int k = 0; k < SplicingTableData.IotaButtons; k++)
        {
            int index = viewStart + k;
            // 格子左边的缝（下标 = 右边那一格）
            var edge = new Rectangle((int)(cx - Gap), (int)rowY, (int)Gap, (int)Cell);
            bool edgeInRange = view.IsListReadable && (view.IsInRange(index) || view.IsInRange(index - 1) || (index == 0 && view.ListSize == 0));
            if (sel is Selection.EdgeSel es && es.Index == index) Box(sb, new Rectangle(edge.X + 2, edge.Y, 4, edge.Height), Cursor);
            if (edgeInRange && Clicked(edge)) SplicingTableNet.Select(pos.X, pos.Y, index, IsShift(), isIota: false);

            var cell = new Rectangle((int)cx, (int)rowY, (int)Cell, (int)Cell);
            bool inRange = view.IsInRange(index);
            Box(sb, cell, sel?.Contains(index) == true ? Selected : CellBack, inRange ? BracketColor(view.List![index]) ?? Edge : new Color(60, 50, 80));
            if (inRange)
            {
                var iv = view.List![index];
                DrawIota(sb, iv.Iota, cell);
                if (cell.Contains(Main.mouseX, Main.mouseY)) hover = new IotaHoverText(iv);
                if (Clicked(cell)) SplicingTableNet.Select(pos.X, pos.Y, index, IsShift(), isIota: true);
            }
            Label(sb, index.ToString(), new Vector2(cell.X + 2, cell.Bottom + 1), Dim, 0.6f);
            cx += Cell + Gap;
        }
        {
            // 最后一格右边的缝
            int index = viewStart + SplicingTableData.IotaButtons;
            var edge = new Rectangle((int)(cx - Gap), (int)rowY, (int)Gap, (int)Cell);
            if (sel is Selection.EdgeSel es && es.Index == index) Box(sb, new Rectangle(edge.X + 2, edge.Y, 4, edge.Height), Cursor);
            bool edgeInRange = view.IsListReadable && (view.IsInRange(index) || view.IsInRange(index - 1));
            if (edgeInRange && Clicked(edge)) SplicingTableNet.Select(pos.X, pos.Y, index, IsShift(), isIota: false);
        }
        if (!view.IsListReadable) Label(sb, T("NoList"), new Vector2(x + 16, rowY + 14), Dim);

        // 按钮（两行）
        float by = rowY + Cell + 18f;
        float bx = x + 10f;
        foreach (var a in Enum.GetValues<SplicingTableAction>())
        {
            string label = ShortLabel(a);
            float w = FontAssets.MouseText.Value.MeasureString(label).X * 0.7f + 12f;
            if (bx + w > x + width - 10)
            {
                bx = x + 10f;
                by += 24f;
            }
            var r = new Rectangle((int)bx, (int)by, (int)w, 20);
            bool enabled = SplicingActions.Test(a, view, sel, viewStart)
                           && (!SplicingActions.ConsumesMedia(a) || te.Media >= SplicingTableEntity.MediaCost);
            Box(sb, r, enabled ? CellBack : new Color(35, 30, 45), enabled ? Edge : new Color(60, 55, 70));
            Label(sb, label, new Vector2(r.X + 6, r.Y + 2), enabled ? Text : Dim, 0.7f);
            if (r.Contains(Main.mouseX, Main.mouseY)) Main.instance.MouseText(T("Button." + a));
            if (enabled && Clicked(r)) SplicingTableNet.Action(pos.X, pos.Y, a);
            bx += w + 4f;
        }

        // 槽位
        float sy = by + 32f;
        float sx = x + 10f;
        DrawSlot(sb, te, pos, SplicingTableEntity.SlotList, new Vector2(sx, sy), T("Slot.List"));
        DrawSlot(sb, te, pos, SplicingTableEntity.SlotClipboard, new Vector2(sx + 56, sy), T("Slot.Clipboard"));
        DrawSlot(sb, te, pos, SplicingTableEntity.SlotMedia, new Vector2(sx + 112, sy), T("Slot.Media"));
        DrawSlot(sb, te, pos, SplicingTableEntity.SlotStaff, new Vector2(sx + 168, sy), T("Slot.Staff"));
        for (int i = 0; i < 6; i++)
        {
            DrawSlot(sb, te, pos, SplicingTableEntity.StorageStart + i, new Vector2(sx + 240 + (i % 3) * 44, sy + (i / 3) * 44 - 20), null, 0.75f);
        }

        // 剪贴板内容、画图案、施法
        float ay = sy + 52f;
        if (view.Clipboard is { } clip) Label(sb, T("ClipboardIs", Fit(IotaText.Display(clip), 300)), new Vector2(sx, ay), Dim, 0.7f);
        float rightX = x + width - 10f;
        bool canDraw = !te.Slots[SplicingTableEntity.SlotStaff].IsAir && view.IsListWritable && sel is not null && te.Media >= SplicingTableEntity.MediaCost;
        var drawBtn = new Rectangle((int)(rightX - 90), (int)(sy - 20), 90, 22);
        Box(sb, drawBtn, canDraw ? CellBack : new Color(35, 30, 45), canDraw ? Edge : new Color(60, 55, 70));
        Label(sb, T("Draw"), new Vector2(drawBtn.X + 8, drawBtn.Y + 3), canDraw ? Text : Dim, 0.75f);
        if (canDraw && Clicked(drawBtn)) StartDrawing(pos);
        if (te.Enlightened)
        {
            bool canCast = view.HasHex && te.Media >= SplicingTableEntity.MediaCost;
            var castBtn = new Rectangle((int)(rightX - 90), (int)(sy + 8), 90, 22);
            Box(sb, castBtn, canCast ? new Color(90, 60, 140) : new Color(35, 30, 45), canCast ? Cursor : new Color(60, 55, 70));
            Label(sb, T("Cast"), new Vector2(castBtn.X + 8, castBtn.Y + 3), canCast ? Text : Dim, 0.75f);
            if (canCast && Clicked(castBtn)) SplicingTableNet.Cast(pos.X, pos.Y);
        }

        panel.Height = (int)(ay + 24 - y);

        if (hover is { } h) Main.instance.MouseText(h.Text());
    }

    /// <summary>上游 SplicingTableIotaRenderer 的提示：名字 + 索引下标（图案再加图案编码，列表再加长度）。</summary>
    private sealed class IotaHoverText
    {
        private readonly SplicingIotaView _v;

        public IotaHoverText(SplicingIotaView v) => _v = v;

        public string Text()
        {
            var lines = new List<string> { IotaText.Display(_v.Iota), T("Tooltip.Index", _v.Index) };
            if (_v.Iota is PatternIota p) lines.Add(T("Tooltip.Signature", IotaText.SimpleString(p.Pattern)));
            if (_v.Iota is ListIota l) lines.Add(T("Tooltip.Length", l.Count));
            lines.Add(T("Tooltip.Depth", _v.Depth));
            return string.Join("\n", lines);
        }
    }

    private static Color? BracketColor(SplicingIotaView v)
    {
        if (v.Iota is not PatternIota p) return null;
        var s = p.Pattern.AnglesSignature();
        if (s != "qqq" && s != "eee") return null;
        int depth = s == "qqq" ? v.Depth : v.Depth;
        return Brackets[((depth % Brackets.Length) + Brackets.Length) % Brackets.Length];
    }

    private static void DrawIota(SpriteBatch sb, Iota iota, Rectangle cell)
    {
        // 联动：HexParse 的注释 iota
        if (Interop.HexParseCommentRenderer.TryDraw(sb, iota, cell)) return;
        if (iota is PatternIota p)
        {
            PatternArt.DrawReadable(p.Pattern, new Vector2(cell.Center.X, cell.Center.Y), Cell * 0.8f);
            return;
        }
        string s = iota is ListIota l ? "[" + l.Count + "]" : Fit(IotaText.Display(iota), Cell - 6);
        var size = FontAssets.MouseText.Value.MeasureString(s) * 0.65f;
        Label(sb, s, new Vector2(cell.Center.X - size.X / 2, cell.Center.Y - size.Y / 2), Text, 0.65f);
    }

    private static string Fit(string s, float width)
    {
        var font = FontAssets.MouseText.Value;
        if (font.MeasureString(s).X * 0.65f <= width) return s;
        int n = s.Length;
        while (n > 1 && font.MeasureString(s[..n] + "..").X * 0.65f > width) n--;
        return s[..n] + "..";
    }

    private static string ShortLabel(SplicingTableAction a) => a switch
    {
        SplicingTableAction.ViewLeftFull => "|<<",
        SplicingTableAction.ViewLeftPage => "<<",
        SplicingTableAction.ViewLeft => "<",
        SplicingTableAction.ViewRight => ">",
        SplicingTableAction.ViewRightPage => ">>",
        SplicingTableAction.ViewRightFull => ">>|",
        _ => T("Short." + a),
    };

    private static bool IsShift() => Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) || Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift);

    private static bool IsCtrl() => Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftControl) || Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightControl);

    /// <summary>上游 mouseScrolled：往上滚视野往左（潜行一页、Ctrl 到头），往下滚往右。</summary>
    private static void HandleScroll(Point16 pos)
    {
        int delta = PlayerInput.ScrollWheelDeltaForUI;
        if (delta == 0) return;
        PlayerInput.ScrollWheelDeltaForUI = 0;
        SplicingTableAction a = delta > 0
            ? (IsCtrl() ? SplicingTableAction.ViewLeftFull : IsShift() ? SplicingTableAction.ViewLeftPage : SplicingTableAction.ViewLeft)
            : (IsCtrl() ? SplicingTableAction.ViewRightFull : IsShift() ? SplicingTableAction.ViewRightPage : SplicingTableAction.ViewRight);
        SplicingTableNet.Action(pos.X, pos.Y, a);
    }

    /// <summary>泰拉的物品格子：照常点放、拿；格子内容变了就发给服务端（单机直接改）。</summary>
    private static void DrawSlot(SpriteBatch sb, SplicingTableEntity te, Point16 pos, int slot, Vector2 at, string? label, float scale = 0.85f)
    {
        float old = Main.inventoryScale;
        Main.inventoryScale = scale;
        var item = te.Slots[slot];
        int size = (int)(TextureAssets.InventoryBack.Width() * scale);
        var r = new Rectangle((int)at.X, (int)at.Y, size, size);
        if (r.Contains(Main.mouseX, Main.mouseY) && !PlayerInput.IgnoreMouseInterface)
        {
            Main.LocalPlayer.mouseInterface = true;
            bool blocked = Main.mouseLeft && Main.mouseLeftRelease && !Main.mouseItem.IsAir && !SplicingTableEntity.CanPlace(slot, Main.mouseItem);
            if (!blocked)
            {
                var before = item.Clone();
                ItemSlot.Handle(ref item, ItemSlot.Context.ChestItem);
                if (item.type != before.type || item.stack != before.stack || item.prefix != before.prefix)
                {
                    te.Slots[slot] = item;
                    if (Main.netMode == Terraria.ID.NetmodeID.SinglePlayer) te.SetSlot(slot, item);
                    else SplicingTableNet.SetSlot(pos.X, pos.Y, slot, item);
                }
            }
        }
        ItemSlot.Draw(sb, ref item, ItemSlot.Context.ChestItem, at);
        Main.inventoryScale = old;
        if (label is not null) Label(sb, label, new Vector2(at.X, at.Y + size + 1), Dim, 0.6f);
    }
}
