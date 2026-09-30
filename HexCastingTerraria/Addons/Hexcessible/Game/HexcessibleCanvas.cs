using System;
using System.Collections.Generic;
using HexCastingTerraria.Addons.Hexcessible.Core;
using HexCastingTerraria.Client;
using HexCastingTerraria.Client.UI;
using HexCastingTerraria.Config;
using HexCastingTerraria.Core.Casting.Math;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria.GameContent;
using Terraria.Localization;

namespace HexCastingTerraria.Addons.Hexcessible.Game;

/// <summary>
/// Hexcessible 在画布上的状态机（上游 drawstate/DrawState + Idling + MouseDrawing + KeyboardDrawing，
/// 以及 mixin/DrawStateMixin 的接线与左下角快捷键提示）。
/// 现在有：空闲 → 按 q w e a d 进入键盘绘制；手画时的提示。自动补全 / 别名 / 悬停说明随后续功能加。
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
    private Vector2? _lastMouse;

    private static HexcessibleSettings S => HexcessibleSettings.Current;

    private static string L(string key) => Language.GetTextValue("Mods.HexCastingTerraria.Hexcessible." + key);

    public bool Enabled => HexAddonsClientConfig.Instance?.Hexcessible == true;

    /// <summary>上游 allowStartDrawing：键盘绘制里签名非空时不让鼠标落笔。</summary>
    public bool AllowStartDrawing => _kbd is null || _kbd.Sig.Count == 0;

    public bool Update(CanvasFrame f)
    {
        Func<HexCoord, bool> used = f.IsUsed;
        bool mouseMoved = _lastMouse is { } last && last != f.Mouse;
        _lastMouse = f.Mouse;

        // 上游 updateRequired：手画开始（JUSTSTARTED / DRAWING）就不再是键盘绘制
        if (f.Canvas.State != DrawState.BetweenPatterns)
        {
            _kbd = null;
            return false;
        }

        if (_kbd is null)
        {
            // 上游 Idling.onCharType：q w e a d 开始键盘绘制
            if (!S.KeyboardAllow) return false;
            char? c = PressedLetter(drawOnly: true);
            if (c is null) return false;
            _kbd = new KeyboardDrawingState(new[] { KeyboardPlacement.AngleOf(c.Value)!.Value }, used);
            mouseMoved = false;
        }
        else
        {
            if (mouseMoved) _kbd.SetOrigin(f.MouseCoord, used);
            if (!HandleKeys(f, used)) return true;
        }

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
        char? c = PressedLetter(drawOnly: false);
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
        // 上游 onMouseScroll：rotate(-delta)
        if (_kbd is null) return false;
        var canvas = HexCanvasState.Canvas;
        var frame = new CanvasFrame(canvas, Terraria.Main.screenWidth, Terraria.Main.screenHeight, _lastMouse ?? Vector2.Zero, false, false);
        _kbd.Rotate(-notches, frame.IsUsed);
        return true;
    }

    public void Draw(SpriteBatch sb, CanvasFrame f)
    {
        if (_kbd is { } kbd)
        {
            if (S.KeyHint) DrawKeyHints(sb, f, kbd);
            var anchor = f.CoordToPx(kbd.End ?? kbd.Origin);
            DrawSigTooltip(sb, anchor.X + f.HexSize, anchor.Y, kbd.Sig, kbd.Start is null, S.KeyboardTooltip, kbd.QueuedCount);
        }
        if (S.ShortcutHints) DrawShortcutHints(sb, f);
    }

    public void OnClose()
    {
        _kbd = null;
        _lastMouse = null;
    }

    /// <summary>上游 renderNextPointTooltips：下一笔能去的格点方向上，离终点一小段标出字母。</summary>
    private static void DrawKeyHints(SpriteBatch sb, CanvasFrame f, KeyboardDrawingState kbd)
    {
        if (kbd.End is not { } end) return;
        var endPx = f.CoordToPx(end);
        float dist = f.HexSize;
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

        var text = new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(sig, KeyboardPlacement.LetterOf)));
        if (S.UppercaseSig) text = text.ToUpperInvariant();
        y += TooltipBox.Draw(sb, text, TooltipBox.White, x, y);

        if (failed) y += TooltipBox.Draw(sb, L("NoSpace"), TooltipBox.Red, x, y);
        if (queued > 0) y += TooltipBox.Draw(sb, Language.GetTextValue("Mods.HexCastingTerraria.Hexcessible.CountQueued", queued), TooltipBox.Yellow, x, y);

        if (tooltip != HexcessibleSettings.TooltipMode.Descriptive) return;
        var entry = PatternEntries.FromSig(sig, HexBook.Document);
        if (entry is null) return;
        var lines = new List<(string, Color)> { (entry.ToString(), TooltipBox.Blue) };
        foreach (var args in entry.Args) lines.Add((args, TooltipBox.DarkGray));
        TooltipBox.Draw(sb, lines, x, y);
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
        else if (f.Canvas.State == DrawState.BetweenPatterns)
        {
            hints.Add((S.KeyboardAllow ? "lmb/" + draw : "lmb", "DrawStart"));
        }
        else
        {
            hints.Add(("lmb", "Cast"));
        }

        float x = 6, y = f.Height - 16;
        float lineH = TooltipBox.Measure("A").Y;
        foreach (var (keys, hint) in hints)
        {
            var k = keys + " ";
            var top = y - lineH;
            Terraria.Utils.DrawBorderString(sb, k, new Vector2(x, top), TooltipBox.Gray, TooltipBox.TextScale);
            Terraria.Utils.DrawBorderString(sb, L("Hint." + hint), new Vector2(x + TooltipBox.Measure(k).X, top), TooltipBox.DarkGray, TooltipBox.TextScale);
            y -= lineH;
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

    private static readonly (Keys, char)[] Letters =
    {
        (Keys.Q, 'q'), (Keys.W, 'w'), (Keys.E, 'e'), (Keys.A, 'a'), (Keys.D, 'd'), (Keys.S, 's'),
    };
}
