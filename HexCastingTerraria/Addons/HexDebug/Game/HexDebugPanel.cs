using System.Collections.Generic;
using System.Linq;
using HexCastingTerraria.Addons.HexDebug.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace HexCastingTerraria.Addons.HexDebug.Game;

/// <summary>
/// 游戏内调试面板（偏差：上游把这些交给外部编辑器 VS Code 通过 DAP 显示）。拿着调试杖 / 运行杖、或运行杖画布开着时显示：
/// 线程与状态、源码（当前行高亮，点行号设 / 取消断点）、栈、渡鸦之思、状态三项、调用栈、输出。
/// </summary>
public sealed class HexDebugPanel : AddonSystem
{
    public override string AddonId => "hexdebug";

    private const float Width = 460f;
    private const float Scale = 0.75f;
    private const int SourceRows = 12;

    private static readonly Color Back = new Color(20, 12, 30) * 0.88f;
    private static readonly Color Border = new(120, 80, 200);
    private static readonly Color Head = new(200, 170, 255);
    private static readonly Color Text = new(225, 220, 235);
    private static readonly Color Dim = new(150, 145, 165);
    private static readonly Color Err = new(255, 110, 110);
    private static readonly Color Current = new Color(120, 90, 30) * 0.8f;
    private static readonly Color BreakDot = new(230, 60, 60);

    /// <summary>这一帧画出来的源码行（点击用）：屏幕区域、源码编号、行。</summary>
    private static readonly List<(Rectangle Rect, int SourceRef, int Line)> Rows = new();
    private static Rectangle _bounds;

    private static HexDebugView? Shown()
    {
        if (Main.dedServ || Main.gameMenu || HexDebugClient.Threads.Count == 0) return null;
        int held = HexDebugClient.HeldThread();
        if (held < 0) return null;
        return HexDebugClient.Threads.TryGetValue(held, out var v) ? v : null;
    }

    public override void PostUpdateInput()
    {
        if (Shown() is null)
        {
            _bounds = Rectangle.Empty;
            return;
        }
        var mouse = new Point(Main.mouseX, Main.mouseY);
        if (!_bounds.Contains(mouse)) return;
        Main.LocalPlayer.mouseInterface = true;
        if (Main.mouseLeft && Main.mouseLeftRelease)
        {
            foreach (var (rect, src, line) in Rows)
            {
                if (!rect.Contains(mouse)) continue;
                HexDebugNet.ToServer(HexDebugNet.Msg.ToggleBreakpoint, w =>
                {
                    w.Write(src);
                    w.Write(line);
                });
                Main.mouseLeftRelease = false;
                break;
            }
        }
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
        if (index < 0) index = layers.Count;
        layers.Insert(index, new LegacyGameInterfaceLayer("HexCastingTerraria: HexDebug Panel", () =>
        {
            if (Shown() is { } v) Draw(Main.spriteBatch, v);
            return true;
        }, InterfaceScaleType.UI));
    }

    private static string T(string key, params object[] args) => HexDebugText.Get("Panel." + key, args);

    private static void Draw(SpriteBatch sb, HexDebugView v)
    {
        var font = FontAssets.MouseText.Value;
        float lineH = font.MeasureString("A").Y * Scale;
        float x = Main.screenWidth / Main.UIScale - Width - 16f;
        float y = 280f;
        var lines = new List<(string Text, Color Color, float Indent, int SourceLine)>();

        // 标题与状态
        lines.Add(("HexDebug · " + HexDebugText.Thread(v.ThreadId, v.Name), Head, 0, -1));
        string state = T("State." + v.State) + (v.Reason is { } r ? T("Because", T("Reason." + r)) : "");
        lines.Add((state, Text, 0, -1));

        // 源码：当前行附近一屏
        lines.Add((T("Source", v.SourceRef), Head, 0, -1));
        if (v.SourceLines.Count == 0)
        {
            lines.Add((T("Empty"), Dim, 8, -1));
        }
        else
        {
            int first = System.Math.Clamp(v.CurrentLine - SourceRows / 3, 0, System.Math.Max(0, v.SourceLines.Count - SourceRows));
            int last = System.Math.Min(v.SourceLines.Count, first + SourceRows);
            for (int i = first; i < last; i++) lines.Add((i.ToString().PadLeft(3) + "  " + v.SourceLines[i], Text, 8, i));
        }

        // 栈（栈顶在前）、渡鸦之思、状态三项
        lines.Add((T("Stack", v.Stack.Count), Head, 0, -1));
        foreach (var s in v.Stack.Take(6)) lines.Add((s, Text, 8, -1));
        if (v.Stack.Count > 6) lines.Add((T("More", v.Stack.Count - 6), Dim, 8, -1));
        lines.Add((T("Ravenmind", v.Ravenmind), Text, 0, -1));
        lines.Add((T("State3", v.OpsConsumed, v.EscapeNext ? T("Yes") : T("No"), v.ParenCount), Dim, 0, -1));

        // 调用栈（当前在前）
        lines.Add((T("Frames"), Head, 0, -1));
        foreach (var (name, where, isVirtual) in v.Frames.Take(4)) lines.Add(((isVirtual ? "  " : "") + name + "  " + where, isVirtual ? Dim : Text, 8, -1));

        // 输出（这个线程最近几条）
        var outs = HexDebugClient.Output.Where(o => o.Thread == v.ThreadId).TakeLast(4).ToList();
        if (outs.Count > 0)
        {
            lines.Add((T("Output"), Head, 0, -1));
            foreach (var (_, text, cat) in outs) lines.Add((text, cat == OutputCategory.Stderr ? Err : Text, 8, -1));
        }
        lines.Add((T("Hint"), Dim, 0, -1));

        float h = lines.Count * lineH + 16f;
        _bounds = new Rectangle((int)x, (int)y, (int)Width, (int)h);
        var px = TextureAssets.MagicPixel.Value;
        sb.Draw(px, _bounds, Back);
        sb.Draw(px, new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, 2), Border);
        sb.Draw(px, new Rectangle(_bounds.X, _bounds.Bottom - 2, _bounds.Width, 2), Border);
        sb.Draw(px, new Rectangle(_bounds.X, _bounds.Y, 2, _bounds.Height), Border);
        sb.Draw(px, new Rectangle(_bounds.Right - 2, _bounds.Y, 2, _bounds.Height), Border);

        Rows.Clear();
        float ty = y + 8f;
        foreach (var (text, color, indent, line) in lines)
        {
            float tx = x + 10f + indent;
            if (line >= 0)
            {
                var row = new Rectangle((int)x + 4, (int)ty, (int)Width - 8, (int)lineH);
                Rows.Add((row, v.SourceRef, line));
                if (line == v.CurrentLine) sb.Draw(px, row, Current);
                if (v.BreakpointLines.Contains(line))
                {
                    sb.Draw(px, new Rectangle((int)x + 7, (int)(ty + lineH / 2 - 3), 6, 6), BreakDot);
                }
            }
            global::HexCastingTerraria.Client.UI.RichText.DrawTagged(sb, text, new Vector2(tx, ty), color, Scale, Width - 20f - indent);
            ty += lineH;
        }
    }
}

/// <summary>死了、下线、退出世界时结束调试（上游 onDeath / onRemove）。</summary>
public sealed class HexDebugPlayer : AddonPlayer
{
    public override string AddonId => "hexdebug";

    public override void Kill(double damage, int hitDirection, bool pvp, Terraria.DataStructures.PlayerDeathReason damageSource)
    {
        if (Main.netMode != Terraria.ID.NetmodeID.MultiplayerClient) HexDebugSessions.TerminateAll(Player.whoAmI, forget: false);
    }

    public override void PlayerDisconnect()
    {
        if (Main.netMode == Terraria.ID.NetmodeID.Server) HexDebugSessions.TerminateAll(Player.whoAmI, forget: true);
    }

    public override void OnEnterWorld()
    {
        HexDebugClient.Clear();
        HexDebugProxy.Reconfigure();   // 上游 CLIENT_PLAYER_JOIN 时打开端口
    }
}

public sealed class HexDebugWorld : AddonSystem
{
    public override string AddonId => "hexdebug";

    public override void OnWorldUnload()
    {
        HexDebugSessions.Clear();
        HexDebugClient.Clear();
    }
}
