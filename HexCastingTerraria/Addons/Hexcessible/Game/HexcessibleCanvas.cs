using System;
using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Addons.Hexcessible.Core;
using HexCastingTerraria.Client;
using HexCastingTerraria.Client.UI;
using HexCastingTerraria.Config;
using HexCastingTerraria.Core.Casting.Math;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Localization;

namespace HexCastingTerraria.Addons.Hexcessible.Game;

/// <summary>
/// Hexcessible 在画布上的状态机（上游 drawstate/DrawState + Idling + MouseDrawing + KeyboardDrawing + AutoCompleting，
/// 以及 mixin/DrawStateMixin 的接线与左下角快捷键提示）。
/// 状态：空闲（悬停说明）/ 键盘绘制（<see cref="_kbd"/>）/ 自动补全（<see cref="_ac"/>）/ 改别名（<see cref="_alias"/>）/
/// 手画（本体在画，Hexcessible 只给提示）。
/// </summary>
public sealed class HexcessibleCanvas : ICanvasExtension
{
    // 上游 KeyboardDrawing.COLOR1..4（ARGB）
    private const uint Color1 = 0xff_64c8ff;
    private const uint Color2 = 0xff_fecbe6;
    private const uint Color3 = 0xaa_363a4f;
    private const uint Color4 = 0xaa_6e738d;

    private static readonly Color KeyHintColor = new(0xA8, 0xA8, 0xA8);

    private KeyboardDrawingState? _kbd;
    private AutoCompleteState? _ac;
    private AliasEditState? _alias;
    private Vector2? _lastMouse;

    // 上游 Idling：鼠标停在哪条画好的图案上、从什么时候开始
    private ResolvedPattern? _hovered;
    private PatternEntries.Entry? _hoveredEntry;
    private long _hoverStart;
    private bool _allowStart = true;

    private static HexcessibleSettings S => HexcessibleSettings.Current;

    private static string L(string key) => Language.GetTextValue("Mods.HexCastingTerraria.Hexcessible." + key);

    public bool Enabled => HexAddonsClientConfig.Instance?.Hexcessible == true;

    /// <summary>上游 allowStartDrawing（按这一帧处理输入之前的状态算：上游是先分发点击、再由当前状态决定能不能落笔）。</summary>
    public bool AllowStartDrawing => _allowStart;

    private bool CurrentAllowStart()
        => _alias is not null ? true
         : _kbd is not null ? _kbd.Sig.Count == 0
         : _ac is not null ? _ac.NoDistract
         : true;

    public bool Update(CanvasFrame f)
    {
        _allowStart = CurrentAllowStart();
        f.Canvas.Dimmed = S.Dimmed;
        f.Canvas.ShowAllDots = S.ShowAllDots;
        Func<HexCoord, bool> used = f.IsUsed;
        bool mouseMoved = _lastMouse is { } last && last != f.Mouse;
        _lastMouse = f.Mouse;

        // 上游 KeyDocsScreenMixin：按 N 查书（先于状态切换：手画中按也算）
        if (CanvasFrame.KeyPressed(Keys.N) && KeyDocsAllowed(f))
        {
            OpenDocs(f);
            return true;
        }

        // 上游 updateRequired：本体在画（DRAWING）时只能是手画；刚按下一个点（JUSTSTARTED）时变成以那一点为起点的自动补全
        switch (f.Canvas.State)
        {
            case DrawState.Drawing:
                _kbd = null;
                _ac = null;
                _alias = null;
                ClearHover();
                return false;
            case DrawState.JustStarted:
                _kbd = null;
                ClearHover();
                if (_ac is null && _alias is null) StartAutoComplete(f.Canvas.DrawStartCoord);
                break;
        }

        if (_alias is not null) return UpdateAlias(f);
        if (_ac is not null) return UpdateAutoComplete(f, mouseMoved);
        if (_kbd is not null) return UpdateKeyboard(f, used, mouseMoved);

        // 空闲（上游 Idling）
        UpdateHover(f);
        if (CanvasFrame.Ctrl)
        {
            // Ctrl+空格：在鼠标处开始自动补全
            if (CanvasFrame.KeyPressed(Keys.Space) && S.AutoCompleteAllow)
            {
                StartAutoComplete(f.MouseCoord);
                return true;
            }
            // Ctrl+E：给鼠标停着的那条图案起别名
            if (CanvasFrame.KeyPressed(Keys.E) && _hoveredEntry is not null)
            {
                StartAlias(_hoveredEntry);
                return true;
            }
            return false;
        }
        if (!S.KeyboardAllow) return false;
        char? c = PressedLetter(drawOnly: true);
        if (c is null) return false;
        _kbd = new KeyboardDrawingState(new[] { KeyboardPlacement.AngleOf(c.Value)!.Value }, used);
        AddOverlays(f);
        return true;
    }

    // ==================== 自动补全 ====================

    private void StartAutoComplete(HexCoord start)
    {
        _ac = new AutoCompleteState(start, HexcessibleIndex.Get(), HexcessibleIndex.IsLocked);
        // 丢掉之前按过的字（泰拉把没人读的字符攒着，第一次读会一股脑吐出来）
        Main.clrInput();
    }

    /// <summary>上游 AutoCompleting 的 onCharType / onKeyPress / onMouseMove / onMousePress。</summary>
    private bool UpdateAutoComplete(CanvasFrame f, bool mouseMoved)
    {
        var ac = _ac!;

        // 右键归本体（关画布）：上游 MC 施法界面右键什么都不做，这里保留本体的「右键关画布」
        if (f.RightPressed)
        {
            ExitAutoComplete(f);
            return false;
        }

        // 上游 onMouseMove：鼠标一动就算「用鼠标」；没打字时鼠标离开起点一段距离就退出
        if (mouseMoved)
        {
            ac.LastInteractWasMouse = true;
            var anchor = f.CoordToPx(ac.Start);
            float breakout = (float)Math.Pow(f.HexSize * 1.75, 2);
            if (ac.NoDistract && f.Canvas.State == DrawState.BetweenPatterns
                && Vector2.DistanceSquared(f.Mouse, anchor) > breakout)
            {
                ExitAutoComplete(f);
                return false;
            }
        }

        // 上游 onMousePress：左键退出（没打字时这一下同时落笔，由 AllowStartDrawing 决定）
        if (f.LeftPressed)
        {
            ExitAutoComplete(f);
            return false;
        }

        // MC 里按一个键先走 keyPressed 再走 charTyped：keyPressed 在「没打字」时什么都不做，否则先记下「用键盘」
        bool distractBefore = !ac.NoDistract;
        if (distractBefore && AnyKeyPressed()) ac.LastInteractWasMouse = false;

        // 打字（输入法也行）：上游 onCharType 先停掉正在开始的那一笔，再把字接到查询后面
        PlayerInput.WritingText = true;
        Main.instance.HandleIME();
        string old = ac.Query;
        bool ctrlBack = CanvasFrame.Ctrl && CanvasFrame.KeyPressed(Keys.Back);
        string typed = Main.GetInputText(old);
        if (ctrlBack)
        {
            // 上游 Ctrl+退格：删掉最后一个词（泰拉的输入框只删一个字）
            if (distractBefore) ac.DeleteWord(S.AutoCompleteAllow);
        }
        else if (typed != old)
        {
            if (typed.Length > old.Length && typed.StartsWith(old, StringComparison.Ordinal))
            {
                f.Canvas.CancelDrawing();
                ac.SetQuery(typed, S.AutoCompleteAllow);
            }
            else if (distractBefore)
            {
                // 退格（上游只在已经打过字 / 用过键盘时处理）
                ac.SetQuery(typed, S.AutoCompleteAllow);
            }
        }

        if (distractBefore)
        {
            if (CanvasFrame.KeyPressed(Keys.Enter) || CanvasFrame.KeyPressed(Keys.Tab))
            {
                // 上游：选中项有签名就交给键盘绘制，从起点、按图案自己的起笔方向开始（大法术没学会时没有签名，不动）
                var chosen = ac.ChosenEntry;
                if (chosen?.Sigs is { } sigs)
                {
                    _kbd = new KeyboardDrawingState(ac.Start, sigs, chosen.Dir, f.IsUsed);
                    _ac = null;
                    return true;
                }
            }
            // 上游 Ctrl+E / F2：给选中项起别名
            if ((CanvasFrame.Ctrl && CanvasFrame.KeyPressed(Keys.E)) || CanvasFrame.KeyPressed(Keys.F2))
            {
                if (ac.ChosenEntry is { } picked)
                {
                    StartAlias(picked);
                    return true;
                }
            }
            if (CanvasFrame.KeyPressed(Keys.Up)) ac.OffsetChosen(-1);
            if (CanvasFrame.KeyPressed(Keys.Down)) ac.OffsetChosen(1);
            if (CanvasFrame.KeyPressed(Keys.Left)) ac.OffsetChosenDoc(-1);
            if (CanvasFrame.KeyPressed(Keys.Right)) ac.OffsetChosenDoc(1);
        }
        return true;
    }

    /// <summary>上游 AutoCompleting.requestExit：停掉正在开始的那一笔，回到空闲。</summary>
    private void ExitAutoComplete(CanvasFrame f)
    {
        f.Canvas.CancelDrawing();
        _ac = null;
    }

    // ==================== 按 N 查书 ====================

    /// <summary>上游：IDLING 只在空闲时；ALWAYS 空闲、手画、键盘绘制都行（补全和改别名时 N 是在打字）。</summary>
    private bool KeyDocsAllowed(CanvasFrame f)
    {
        if (_ac is not null || _alias is not null) return false;
        bool idle = _kbd is null && f.Canvas.State == DrawState.BetweenPatterns;
        return S.KeyDocs switch
        {
            HexcessibleSettings.KeyDocsMode.Idling => idle,
            HexcessibleSettings.KeyDocsMode.Always => idle || _kbd is not null || f.Canvas.State == DrawState.Drawing,
            _ => false,
        };
    }

    /// <summary>
    /// 鼠标停在画好的图案上：翻到书里讲它的那一页；否则打开书首页。书关了回到画布（图案还在）。
    /// 上游按「第几个图案页」翻页（遇到前面有文字页就会翻错），这里按图案页的锚点翻到那一页。
    /// </summary>
    private void OpenDocs(CanvasFrame f)
    {
        var impl = PatternAt(f) is { } rp ? HexcessibleIndex.Get().FromSig(rp.Pattern.Angles)?.Impls.FirstOrDefault() : null;
        int slot = Main.LocalPlayer.selectedItem;
        HexCanvasState.CloseCanvas();
        if (impl is not null && impl.EntryId.Length > 0) HexCanvasState.Book.OpenAt(impl.EntryId, impl.Anchor.Length > 0 ? impl.Anchor : null);
        else HexCanvasState.Book.Open();
        HexCanvasState.ReturnToCanvasSlot = slot;
    }

    /// <summary>上游 getPatternAt：鼠标所在格点是哪条画好的图案的起点或经过的点。</summary>
    private static ResolvedPattern? PatternAt(CanvasFrame f)
    {
        var coord = f.MouseCoord;
        foreach (var rp in f.Canvas.Patterns)
        {
            if (rp.Origin.Equals(coord) || rp.Pattern.Positions(rp.Origin).Contains(coord)) return rp;
        }
        return null;
    }

    // ==================== 改别名 ====================

    private void StartAlias(PatternEntries.Entry entry)
    {
        _alias = new AliasEditState(entry);
        _ac = null;
        ClearHover();
        Main.clrInput();
    }

    /// <summary>上游 AliasChanging：打字改名，Ctrl+退格删词，Enter / Tab 存下（空着 = 存回原名）并回到空闲。鼠标照常归本体。</summary>
    private bool UpdateAlias(CanvasFrame f)
    {
        var alias = _alias!;
        PlayerInput.WritingText = true;
        Main.instance.HandleIME();
        bool ctrlBack = CanvasFrame.Ctrl && CanvasFrame.KeyPressed(Keys.Back);
        string typed = Main.GetInputText(alias.Alias);
        if (ctrlBack) alias.DeleteWord();
        else alias.Alias = typed;

        if (CanvasFrame.KeyPressed(Keys.Enter) || CanvasFrame.KeyPressed(Keys.Tab))
        {
            HexcessibleStore.SetAlias(alias.Id, alias.ValueToStore);
            HexcessibleIndex.InvalidateCaches();
            _alias = null;
        }
        return false;
    }

    // ==================== 空闲：悬停 ====================

    /// <summary>上游 Idling.onRender 前半 + getPatternAt：鼠标所在格点是哪条画好的图案的起点或经过的点。</summary>
    private void UpdateHover(CanvasFrame f)
    {
        var hit = PatternAt(f);
        if (hit is null)
        {
            ClearHover();
        }
        else if (!ReferenceEquals(hit, _hovered))
        {
            _hoverStart = Environment.TickCount64;
            _hovered = hit;
            _hoveredEntry = HexcessibleIndex.Get().FromSig(hit.Pattern.Angles);
        }
    }

    private void ClearHover()
    {
        _hovered = null;
        _hoveredEntry = null;
    }

    // ==================== 键盘绘制 ====================

    private bool UpdateKeyboard(CanvasFrame f, Func<HexCoord, bool> used, bool mouseMoved)
    {
        if (mouseMoved) _kbd!.SetOrigin(f.MouseCoord, used);
        if (!HandleKeys(f, used)) return true;

        if (_kbd is not null && _kbd.Sig.Count == 0) RequestExit(used);

        // 上游 onMousePress：左键施放，右键退出键盘绘制（不关画布）
        if (_kbd is not null && f.LeftPressed)
        {
            Submit(f, used);
            return true;
        }
        if (_kbd is not null && f.RightPressed)
        {
            RequestExit(used);
            return true;
        }

        if (_kbd is not null) AddOverlays(f);
        return _kbd is not null;
    }

    /// <summary>键盘绘制里的按键（上游 onCharType + onKeyPress）。返回 false = 已经交出去（画布可能关了）。</summary>
    private bool HandleKeys(CanvasFrame f, Func<HexCoord, bool> used)
    {
        var kbd = _kbd!;
        char? c = CanvasFrame.Ctrl ? null : PressedLetter(drawOnly: false);
        if (c is { } ch)
        {
            if (char.ToLowerInvariant(ch) == 's') Undo(used);
            else if (S.KeyboardAllow) kbd.Type(ch, used);
        }
        if (_kbd is null) return true;

        if (CanvasFrame.KeyPressed(Keys.Back)) Undo(used);
        if (_kbd is null) return true;

        if (CanvasFrame.KeyPressed(Keys.Enter) || CanvasFrame.KeyPressed(Keys.Tab) || CanvasFrame.KeyPressed(Keys.Space))
        {
            Submit(f, used);
            return false;
        }

        Func<HexCoord, bool> visible = f.IsVisible;
        if (CanvasFrame.KeyPressed(Keys.H) || CanvasFrame.KeyPressed(Keys.Left)) _kbd.MoveOrigin(-1, 0, visible, used);
        if (CanvasFrame.KeyPressed(Keys.J) || CanvasFrame.KeyPressed(Keys.Down)) _kbd.MoveOrigin(0, 1, visible, used);
        if (CanvasFrame.KeyPressed(Keys.K) || CanvasFrame.KeyPressed(Keys.Up)) _kbd.MoveOrigin(0, -1, visible, used);
        if (CanvasFrame.KeyPressed(Keys.L) || CanvasFrame.KeyPressed(Keys.Right)) _kbd.MoveOrigin(1, 0, visible, used);
        if (CanvasFrame.KeyPressed(Keys.R)) _kbd.Rotate(CanvasFrame.Shift ? -1 : 1, used);
        return true;
    }

    /// <summary>上游 removeCharFromSig：关了键盘绘制就直接退出。</summary>
    private void Undo(Func<HexCoord, bool> used)
    {
        if (!S.KeyboardAllow)
        {
            RequestExit(used);
            return;
        }
        _kbd?.Undo(used);
    }

    /// <summary>上游 submit：放得下才放，放完退出（有排队的就接着画下一条）。</summary>
    private void Submit(CanvasFrame f, Func<HexCoord, bool> used)
    {
        var placed = _kbd?.Submit(used);
        if (placed is null) return;
        var next = _kbd!.Next;
        f.Place(placed.Value.Pattern, placed.Value.Start);
        // 求值后栈清空会自动关画布（OnClose 已清状态）
        if (!f.Canvas.IsOpen)
        {
            _kbd = null;
            return;
        }
        _kbd = next;
        _kbd?.Recalculate(used);
    }

    /// <summary>上游 KeyboardDrawing.requestExit：有排队的就换成下一条（位置重新算，免得和刚放的重叠），否则回到空闲。</summary>
    private void RequestExit(Func<HexCoord, bool> used)
    {
        var next = _kbd?.Next;
        next?.Recalculate(used);
        _kbd = next;
    }

    private void AddOverlays(CanvasFrame f)
    {
        var kbd = _kbd!;
        // 上游 renderPattern：虚影画在光标处（放不下也画），实际位置用亮色
        if (S.Ghost)
        {
            f.Canvas.Overlays.Add(new CanvasOverlay(KeyboardPlacement.Pattern(kbd.OriginDir, kbd.Sig), kbd.Origin, Color3, Color4));
        }
        if (kbd.Start is { } start && kbd.StartDir is { } dir)
        {
            f.Canvas.Overlays.Add(new CanvasOverlay(KeyboardPlacement.Pattern(dir, kbd.Sig), start, Color1, Color2));
        }
    }

    public bool ConsumeScroll(int notches)
    {
        // 上游 AutoCompleting.onMouseScroll：offsetChosen(-delta)
        if (_ac is not null)
        {
            _ac.OffsetChosen(-notches);
            return true;
        }
        // 上游 KeyboardDrawing.onMouseScroll：rotate(-delta)
        if (_kbd is null) return false;
        var canvas = HexCanvasState.Canvas;
        var frame = new CanvasFrame(canvas, Main.screenWidth, Main.screenHeight, _lastMouse ?? Vector2.Zero, false, false);
        _kbd.Rotate(-notches, frame.IsUsed);
        return true;
    }

    public void OnClose()
    {
        _kbd = null;
        _ac = null;
        _alias = null;
        ClearHover();
        _lastMouse = null;
        _allowStart = true;
        HexcessibleIndex.InvalidateCaches();
    }

    // ==================== 绘制 ====================

    public void Draw(SpriteBatch sb, CanvasFrame f)
    {
        float u = TooltipBox.Unit;
        if (_kbd is { } kbd)
        {
            if (S.KeyHint) DrawKeyHints(sb, f, kbd);
            var anchor = f.CoordToPx(kbd.End ?? kbd.Origin);
            DrawSigTooltip(sb, anchor.X + 20 * u, anchor.Y, kbd.Sig, kbd.Start is null, S.KeyboardTooltip, kbd.QueuedCount);
        }
        else if (_ac is { } ac)
        {
            DrawAutoComplete(sb, f, ac);
        }
        else if (_alias is { } alias)
        {
            DrawAlias(sb, alias);
        }
        else if (f.Canvas.State == DrawState.Drawing && f.Canvas.WipPattern is { } wip)
        {
            // 上游 MouseDrawing.onRender：手画时鼠标右边两个格距处
            DrawSigTooltip(sb, f.Mouse.X + f.HexSize * 2, f.Mouse.Y, wip.Angles, false, S.MouseDrawTooltip, 0);
        }
        else if (_hovered is { } hovered && Environment.TickCount64 - _hoverStart > 500)
        {
            // 上游 Idling：停够半秒
            DrawSigTooltip(sb, f.Mouse.X, f.Mouse.Y, hovered.Pattern.Angles, false, S.IdleTooltip, 0);
        }
        if (S.ShortcutHints) DrawShortcutHints(sb, f);
    }

    /// <summary>上游 renderNextPointTooltips：下一笔能去的格点方向上，离终点 20 个界面像素标出字母。</summary>
    private static void DrawKeyHints(SpriteBatch sb, CanvasFrame f, KeyboardDrawingState kbd)
    {
        if (kbd.End is not { } end) return;
        var endPx = f.CoordToPx(end);
        float dist = 20 * TooltipBox.Unit;
        foreach (var (letter, pos) in kbd.NextPoints(f.IsUsed))
        {
            var d = f.CoordToPx(pos) - endPx;
            if (d.LengthSquared() < 1e-3f) continue;
            d.Normalize();
            var at = endPx + d * dist;
            Terraria.Utils.DrawBorderString(sb, letter.ToString(), at, KeyHintColor, TooltipBox.TextScale, 0.5f, 0.5f);
        }
    }

    /// <summary>
    /// 上游 KeyboardDrawing.render：签名框；放不下时红字；排队数黄字；「详细」再加一框：蓝色的签名 + 名字，
    /// 下面每页图案页一行深灰色参数（input -> output）。
    /// </summary>
    public static void DrawSigTooltip(SpriteBatch sb, float x, float y, IReadOnlyList<HexAngle> sig, bool failed,
        HexcessibleSettings.TooltipMode tooltip, int queued)
    {
        if (sig.Count == 0 || tooltip == HexcessibleSettings.TooltipMode.Hidden)
        {
            if (failed) TooltipBox.Draw(sb, L("NoSpace"), TooltipBox.Red, x, y);
            return;
        }

        var text = new string(sig.Select(KeyboardPlacement.LetterOf).ToArray());
        if (S.UppercaseSig) text = text.ToUpperInvariant();
        y += TooltipBox.Draw(sb, text, TooltipBox.White, x, y);

        if (failed) y += TooltipBox.Draw(sb, L("NoSpace"), TooltipBox.Red, x, y);
        if (queued > 0) y += TooltipBox.Draw(sb, Language.GetTextValue("Mods.HexCastingTerraria.Hexcessible.CountQueued", queued), TooltipBox.Yellow, x, y);

        if (tooltip != HexcessibleSettings.TooltipMode.Descriptive) return;
        var entry = HexcessibleIndex.Get().FromSig(sig);
        if (entry is null) return;
        var lines = new List<TooltipBox.Line> { new(entry.ToString(), TooltipBox.Blue) };
        foreach (var impl in entry.Impls) lines.Add(new TooltipBox.Line(impl.Args, TooltipBox.DarkGray));
        TooltipBox.Draw(sb, lines, x, y);
    }

    /// <summary>上游 AutoCompleting.onRender：查询框（或一行淡字「输入名称……」），打了字再弹候选与说明。</summary>
    private static void DrawAutoComplete(SpriteBatch sb, CanvasFrame f, AutoCompleteState ac)
    {
        if (!S.AutoCompleteAllow) return;
        float u = TooltipBox.Unit;
        var a = f.CoordToPx(ac.Start);
        float x = a.X, y = a.Y;
        var unlocked = ac.Unlocked();

        // renderQueryTooltip
        var input = ac.Query.Length > 0
            ? new TooltipBox.Line { (ac.Query, TooltipBox.White), (" " + unlocked.Count, TooltipBox.DarkGray) }
            : new TooltipBox.Line(L("StartTyping"), TooltipBox.DarkGray);
        if (!ac.NoDistract) TooltipBox.Draw(sb, input, x, y);
        else TooltipBox.Text(sb, input, x + 12 * u, y - 12 * u);

        if (unlocked.Count == 0 || ac.NoDistract) return;

        // prepareOptions
        var options = new List<TooltipBox.Line>();
        var (from, to) = ac.Window(S.AutoCompleteCount);
        for (int i = from; i < to; i++)
        {
            options.Add(new TooltipBox.Line(unlocked[i].ToString(), i == ac.Chosen ? TooltipBox.Blue : TooltipBox.Gray));
        }
        int lockedN = ac.LockedCount;
        if (lockedN > 0) options.Add(new TooltipBox.Line(Language.GetTextValue("Mods.HexCastingTerraria.Hexcessible.CountLocked", lockedN), TooltipBox.DarkGray));

        // prepareDescription（折行宽度 170 个界面像素）
        var desc = new List<TooltipBox.Line>();
        float wrap = 170 * u;
        if (ac.Chosen < unlocked.Count)
        {
            var opt = unlocked[ac.Chosen];
            if (opt.Sigs is null)
            {
                desc = TooltipBox.Wrap(L("WorldSpecificAutocomplete"), TooltipBox.Red, wrap);
            }
            else if (S.AutoCompleteTooltip == HexcessibleSettings.TooltipMode.Simple)
            {
                desc = TooltipBox.Wrap(string.Join("\n", opt.Impls.Select(i => i.Args)), TooltipBox.DarkGray, wrap);
            }
            else if (S.AutoCompleteTooltip == HexcessibleSettings.TooltipMode.Descriptive && ac.ChosenDoc < opt.Impls.Count)
            {
                var impl = opt.Impls[ac.ChosenDoc];
                desc = TooltipBox.Wrap("[" + (ac.ChosenDoc + 1) + "/" + opt.Impls.Count + "] " + impl.Args, TooltipBox.Gray, wrap);
                desc.AddRange(TooltipBox.Wrap(impl.CleanDesc, TooltipBox.DarkGray, wrap));
            }
        }

        // drawTooltips：放不下就画到上面；说明框在右边放不下就放到左边
        float fontH = TooltipBox.LineHeight;
        float descH = desc.Count * fontH;
        float descW = desc.Count == 0 ? 0 : desc.Max(TooltipBox.Width);
        float optsH = options.Count * fontH;
        float optsW = options.Max(TooltipBox.Width);
        float sw = Main.screenWidth, sh = Main.screenHeight;
        bool renderAbove = sh - y < Math.Max(descH, optsH) + 15 * u;
        bool descLeft = sw - x - optsW < descW + 30 * u;

        float optionsX = x + optsW + 20 * u > sw ? sw - optsW - 20 * u : x;
        float optionsY = renderAbove ? y - options.Count * fontH - 9 * u : y + 17 * u;
        TooltipBox.Draw(sb, options, optionsX, optionsY);

        if (desc.Count == 0) return;
        float descriptionY = renderAbove ? y - desc.Count * fontH - 9 * u : y + 17 * u;
        float descriptionX = descLeft ? optionsX - descW - 9 * u : optionsX + optsW + 9 * u;
        TooltipBox.Draw(sb, desc, descriptionX, descriptionY);
    }

    /// <summary>上游 AliasChanging.onRender：屏幕三分之一宽、一半高处，上面原名（有别名时变灰），下面输入框。</summary>
    private static void DrawAlias(SpriteBatch sb, AliasEditState alias)
    {
        float u = TooltipBox.Unit;
        float x = Main.screenWidth / 3f, y = Main.screenHeight / 2f;
        TooltipBox.Draw(sb, alias.Signature + " " + alias.Original, alias.IsBlank ? TooltipBox.Blue : TooltipBox.Gray, x, y - 1 * u);
        if (alias.IsBlank) TooltipBox.Draw(sb, L("StartTypingAlias"), TooltipBox.DarkGray, x, y + 16 * u);
        else TooltipBox.Draw(sb, alias.Alias, TooltipBox.Blue, x, y + 16 * u);
    }

    /// <summary>上游 DrawStateMixin.renderHints：左下角从下往上，一行「按键 说明」。</summary>
    private void DrawShortcutHints(SpriteBatch sb, CanvasFrame f)
    {
        var hints = new List<(string Keys, string Hint)>();
        string draw = "q/w/e/a/d";
        if (_kbd is not null)
        {
            if (S.KeyboardAllow)
            {
                hints.Add((draw, "DrawStart"));
                hints.Add(("bksp/s", "Undo"));
            }
            hints.Add(("lmb/tab/enter/space", "Cast"));
            hints.Add(("drag/h/j/k/l/" + Arrows(), "Move"));
            hints.Add(("wheel/r/shift-r", "Rotate"));
        }
        else if (_ac is not null)
        {
            hints.Add(("type", "Search"));
            if (!_ac.NoDistract)
            {
                hints.Add(("tab/enter", "Cast"));
                hints.Add(("wheel/up/down", "Scroll"));
                hints.Add(("left/right", "ScrollDefinitions"));
                hints.Add(("ctrl-e", "Alias"));
            }
        }
        else if (_alias is not null)
        {
            hints.Add(("tab/enter", _alias.IsBlank ? "AliasOff" : "Alias"));
        }
        else if (f.Canvas.State == DrawState.BetweenPatterns)
        {
            hints.Add((S.KeyboardAllow ? "lmb/" + draw : "lmb", "DrawStart"));
            if (S.AutoCompleteAllow) hints.Add(("ctrl-space", "AutoComplete"));
            if (_hoveredEntry is not null) hints.Add(("ctrl-e", "Alias"));
        }
        else
        {
            hints.Add(("lmb", "Cast"));
        }

        float u = TooltipBox.Unit;
        float x = 6 * u, y = f.Height - 16 * u;
        foreach (var (keys, hint) in hints)
        {
            TooltipBox.Text(sb, new TooltipBox.Line { (keys + " ", TooltipBox.Gray), (L("Hint." + hint), TooltipBox.DarkGray) }, x, y);
            y -= 10 * u;
        }
    }

    /// <summary>上游写的是 ←/↓/↑/→；字体没有箭头字形时退回字母写法。</summary>
    private static string Arrows()
    {
        var font = FontAssets.MouseText.Value;
        const string arrows = "\u2190/\u2193/\u2191/\u2192";
        foreach (var ch in arrows)
        {
            if (ch != '/' && !font.IsCharacterSupported(ch)) return "left/down/up/right";
        }
        return arrows;
    }

    /// <summary>这一帧按下的字母键：q w e a d（drawOnly）或再加 s。按住 Shift 当大写（上游大小写都认）。</summary>
    private static char? PressedLetter(bool drawOnly)
    {
        foreach (var (key, ch) in Letters)
        {
            if (drawOnly && ch == 's') continue;
            if (CanvasFrame.KeyPressed(key)) return CanvasFrame.Shift ? char.ToUpperInvariant(ch) : ch;
        }
        return null;
    }

    /// <summary>这一帧有没有刚按下的键（上游 keyPressed 事件；修饰键单按也算）。</summary>
    private static bool AnyKeyPressed()
    {
        foreach (var k in Main.keyState.GetPressedKeys())
        {
            if (Main.oldKeyState.IsKeyUp(k)) return true;
        }
        return false;
    }

    private static readonly (Keys, char)[] Letters =
    {
        (Keys.Q, 'q'), (Keys.W, 'w'), (Keys.E, 'e'), (Keys.A, 'a'), (Keys.D, 'd'), (Keys.S, 's'),
    };
}
