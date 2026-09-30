using System.Collections.Generic;
using HexCastingTerraria.Client.UI;
using HexCastingTerraria.Core.Casting.Math;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;

namespace HexCastingTerraria.Client;

/// <summary>
/// 画布这一帧的情况，交给 <see cref="ICanvasExtension"/>。坐标一律是屏幕像素（和画布同一套）。
/// </summary>
public sealed class CanvasFrame
{
    internal CanvasFrame(HexCanvas canvas, float width, float height, Vector2 mouse, bool leftPressed, bool rightPressed)
    {
        Canvas = canvas;
        Width = width;
        Height = height;
        Mouse = mouse;
        LeftPressed = leftPressed;
        RightPressed = rightPressed;
    }

    public HexCanvas Canvas { get; }
    public float Width { get; }
    public float Height { get; }
    public Vector2 Mouse { get; }

    /// <summary>这一帧左键刚按下 / 右键刚按下。</summary>
    public bool LeftPressed { get; }
    public bool RightPressed { get; }

    public HexCoord MouseCoord => Canvas.PxToCoord(Mouse, Width, Height);

    public Vector2 CoordToPx(HexCoord c) => Canvas.CoordToPx(c, Width, Height);

    public HexCoord PxToCoord(Vector2 px) => Canvas.PxToCoord(px, Width, Height);

    public float HexSize => Canvas.HexSize(Width, Height);

    /// <summary>格点在屏幕里吗（上游 CastRef.isVisible）。</summary>
    public bool IsVisible(HexCoord c)
    {
        var p = CoordToPx(c);
        return p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;
    }

    /// <summary>格点不能再用：已被占用或在屏幕外（上游 CastRef.isUsed）。</summary>
    public bool IsUsed(HexCoord c) => Canvas.IsUsed(c) || !IsVisible(c);

    /// <summary>把一整条图案放在 origin 并送去求值（和手画收笔同一条路：求值、提示、栈清空时自动关画布）。</summary>
    public void Place(HexPattern pattern, HexCoord origin)
    {
        var rp = Canvas.PlacePattern(pattern, origin);
        Content.SpellSounds.Play("casting.pattern.start");
        HexClientSystem.Submit(rp);
    }

    public static bool KeyPressed(Keys k) => Main.keyState.IsKeyDown(k) && Main.oldKeyState.IsKeyUp(k);

    public static bool Shift => Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);

    public static bool Ctrl => Main.keyState.IsKeyDown(Keys.LeftControl) || Main.keyState.IsKeyDown(Keys.RightControl);
}

/// <summary>
/// 画布的扩展点：附属（Hexcessible 等）接管画布的一部分输入与绘制。上游 Hexcessible 用 Mixin 注入 GuiSpellcasting 的
/// mouseClicked / mouseScrolled / keyPressed / render，这里是同样的几个口子。
/// </summary>
public interface ICanvasExtension
{
    /// <summary>现在生效吗（读开关）。不生效的扩展一律不调用。</summary>
    bool Enabled { get; }

    /// <summary>
    /// 每帧（画布开着）在本体处理鼠标之前调用。返回 true = 这一帧的输入归扩展（本体不落笔、右键不关画布）。
    /// </summary>
    bool Update(CanvasFrame frame);

    /// <summary>本体能不能开始手画一笔（上游 allowStartDrawing）。</summary>
    bool AllowStartDrawing { get; }

    /// <summary>滚轮；返回 true = 吃掉（本体不拿它翻法术书 / 拨算盘）。</summary>
    bool ConsumeScroll(int notches);

    /// <summary>在画布笔迹之后画自己的东西（屏幕像素坐标）。</summary>
    void Draw(SpriteBatch sb, CanvasFrame frame);

    /// <summary>画布关了。</summary>
    void OnClose();
}

/// <summary>已登记的画布扩展（附属 OnLoad 时加，OnUnload 时删）。</summary>
public static class CanvasExtensions
{
    public static readonly List<ICanvasExtension> All = new();

    public static IEnumerable<ICanvasExtension> Active
    {
        get
        {
            foreach (var e in All)
            {
                if (e.Enabled) yield return e;
            }
        }
    }
}
