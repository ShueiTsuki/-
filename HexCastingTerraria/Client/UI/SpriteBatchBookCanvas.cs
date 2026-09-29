using System;
using System.Collections.Generic;
using HexCastingTerraria.Core.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// 把 <see cref="IBookCanvas"/> 落到 tModLoader 的 <see cref="SpriteBatch"/> 上 ——
/// 这是「离屏预览」与「游戏内实际渲染」之间唯一的桥。
///
/// ## 为什么必须有这一层
///
/// 皮肤（<see cref="PatchouliSkin"/> / <see cref="VanillaSkin"/>）只依赖
/// <see cref="IBookCanvas"/>，所以它们能放在 Core/、能被 drawtest 编译、
/// 也就能**不开游戏就出图**。真正碰 XNA 的只有这个文件。
/// 两边共用同一份皮肤代码 —— 这样"我看过的图"和"游戏里长什么样"才是同一套几何与配色，
/// 而不是两套各画各的。
///
/// ## 文本
///
/// 用 <c>Utils.DrawBorderString</c>：它自带泰拉那套 1px 描边/投影，
/// 手写 <c>spriteBatch.DrawString</c> 拿不到同样的观感。
/// 量宽用 <c>FontAssets.MouseText</c>，与绘制同一套字体（不能一个量一个画）。
/// </summary>
public sealed class SpriteBatchBookCanvas : IBookCanvas
{
    private readonly List<RectF> _clips = new();
    private readonly Dictionary<string, Texture2D?> _textureCache = new();

    public void FillRect(RectF rect, Color32 color)
    {
        if (rect.W <= 0f || rect.H <= 0f || color.A == 0) { return; }

        var src = new RectF(0, 0, 1, 1);
        if (!Clamp(ref rect, ref src)) { return; }

        Main.spriteBatch.Draw(HexPixel.Value, ToRect(rect), ToColor(color));
    }

    public void StrokeRect(RectF rect, Color32 color, int thickness = 1)
    {
        if (thickness < 1) { thickness = 1; }
        float t = thickness;
        FillRect(new RectF(rect.X, rect.Y, rect.W, t), color);
        FillRect(new RectF(rect.X, rect.Bottom - t, rect.W, t), color);
        FillRect(new RectF(rect.X, rect.Y, t, rect.H), color);
        FillRect(new RectF(rect.Right - t, rect.Y, t, rect.H), color);
    }

    public void HLine(float x, float y, float width, Color32 color, int thickness = 1)
        => FillRect(new RectF(x, y, width, thickness < 1 ? 1 : thickness), color);

    public float DrawText(string text, float x, float y, Color32 color,
                          BookTextAlign align = BookTextAlign.Left,
                          bool bold = false, bool italic = false, int scale = 1)
    {
        if (string.IsNullOrEmpty(text)) { return 0f; }
        if (scale < 1) { scale = 1; }

        float w = MeasureText(text, bold, italic, scale);
        float px = align switch
        {
            BookTextAlign.Center => x - (w / 2f),
            BookTextAlign.Right => x - w,
            _ => x,
        };

        // ⚠️ 文字也要吃裁剪。
        // 之前注释里写着"文本没法部分裁剪"，但**列表行全是文字** —— 于是裁剪对列表等于没做，
        // 第 8 行之后照样画到书外面去（实机截图里看得一清二楚）。
        // 做法：**整段完全落在裁剪区内才画**。列表场景下这正是想要的
        //（半截行宁可不画），而排版本来就保证正文在页内。
        float th = FontAssets.MouseText.Value.LineSpacing * scale;
        float tx = align switch
        {
            BookTextAlign.Center => x - (w / 2f),
            BookTextAlign.Right => x - w,
            _ => x,
        };
        var box = new RectF(tx, y, w, th);
        var c = _clip;
        bool inside = box.X >= c.X - 0.5f && box.Y >= c.Y - 0.5f
                      && box.Right <= c.Right + 0.5f && box.Bottom <= c.Bottom + 0.5f;
        if (!inside) { return w; }

        // 自带 1px 描边（泰拉所有 UI 文字都是这个观感）
        Terraria.Utils.DrawBorderString(Main.spriteBatch, text, new Vector2(tx, y),
                                        ToColor(color), scale);
        return w;
    }

    public int MeasureText(string text, bool bold = false, bool italic = false, int scale = 1)
    {
        if (string.IsNullOrEmpty(text)) { return 0; }
        if (scale < 1) { scale = 1; }
        return (int)(FontAssets.MouseText.Value.MeasureString(text).X * scale);
    }

    public void DrawImage(string asset, RectF src, RectF dst, Color32 tint)
    {
        var tex = GetTexture(asset);
        if (tex is null) { return; }

        var s = src;
        var d = dst;
        if (!Clamp(ref d, ref s)) { return; }

        Main.spriteBatch.Draw(tex,
            ToRect(d),
            new Rectangle((int)System.MathF.Round(s.X), (int)System.MathF.Round(s.Y),
                          System.Math.Max(1, (int)System.MathF.Round(s.W)),
                          System.Math.Max(1, (int)System.MathF.Round(s.H))),
            ToColor(tint));
    }

    /// <summary>
    /// 按名字取贴图并缓存。
    ///
    /// 名字与 Core/ 侧的小写约定对应（`book` / `bookmark` / `panel_bg` …），
    /// 这里映射到模组资源或原版资源。**取不到返回 null 而不是抛** ——
    /// 一张贴图缺失不该让整个界面炸掉（上一轮就是因为加载失败把用户的游戏搞崩过）。
    /// </summary>
    private Texture2D? GetTexture(string asset)
    {
        if (_textureCache.TryGetValue(asset, out var cached)) { return cached; }

        Texture2D? tex = null;
        try
        {
            var path = asset switch
            {
                // 帕秋莉图集（原图集切片在 Core 侧给坐标，这里只给整张）
                "book" or "bookmark" or "buttons" => "HexCastingTerraria/Client/UI/BookAtlas/book_brown",

                // 原版面板与物品格（真贴图，由 _tools/ui_sheet.ps1 对应的那批）
                "panel_bg" => "Images/UI/PanelBackground",
                "panel_border" => "Images/UI/PanelBorder",
                "panel_inner" => "Images/UI/InnerPanelBackground",
                "button_backing" => "Images/UI/ButtonBacking",
                "slot_back" => "Images/UI/Bestiary/Slot_Back",
                "slot_front" => "Images/UI/Bestiary/Slot_Front",
                "slot_overlay" => "Images/UI/Bestiary/Slot_Overlay",
                "slot_selection" => "Images/UI/Bestiary/Slot_Selection",
                _ => null,
            };

            if (path is not null)
            {
                // ⚠️ **模组资源必须走 ModContent，不能走 Main.Assets。**
                //
                // `Main.Assets` 只装原版资源（Images/…）。上一版我拿它去请求
                // `HexCastingTerraria/Client/UI/BookAtlas/book_brown` —— 找不到，
                // 而 `Main.Assets.Request` 遇到不存在的名字会**直接抛致命错误**（弹框崩游戏），
                // 外面的 try/catch 拦不住（异常在异步加载路径里浮出来）。
                // 症状就是：书皮、物品格全没画（DrawImage 提前返回），然后崩。
                var isModAsset = path.StartsWith("HexCastingTerraria/", StringComparison.Ordinal);

                tex = isModAsset
                    ? ModContent.Request<Texture2D>(path, ReLogic.Content.AssetRequestMode.ImmediateLoad)?.Value
                    : Main.Assets.Request<Texture2D>(path, ReLogic.Content.AssetRequestMode.ImmediateLoad)?.Value;
            }
        }
        catch (Exception)
        {
            tex = null;   // 缺失就当没画，绝不抛
        }

        _textureCache[asset] = tex;
        return tex;
    }

    // ── 裁剪 ────────────────────────────────────────────────────────
    //
    // ⚠️ 这里**故意不用 ScissorRectangle**。
    //
    // 一开始的写法是 PushClip 里调 `sb.End()` + `sb.Begin(..., Main.UIScaleMatrix)` 再设
    // ScissorRectangle。问题有两层：
    //   ① 它会**在调用方的 SpriteBatch 中途重建批处理** —— 调用方自己的 Begin 参数
    //      （矩阵、混合状态）与我的不一定相同，重建之后剩下的 UI 全按我的参数画；
    //   ② 调用方随后还会再 `End()` 一次 —— 一旦配不上就是直接崩。
    // 这两条都只有进游戏才会暴露，而我没法自己测。
    //
    // 所以改成「**按裁剪矩形钳制目标矩形**」：矩形与贴图的目标区域超出裁剪区就裁掉，
    // 贴图按同一比例裁源矩形（这样拉伸关系不变）。效果与 Scissor 对矩形绘制完全一致，
    // 而**完全不碰 SpriteBatch 状态** —— 零崩溃风险。
    //
    // 唯一的差别：**文本没法部分裁剪**（要逐像素遮罩）。这里的处理是「整段在外面就跳过，
    // 部分相交就照画」—— 排版本来就保证了正文在页内，越界的只有超长列表，
    // 而列表是矩形槽位，钳得住。

    private RectF _clip = new(0, 0, float.MaxValue, float.MaxValue);

    public void PushClip(RectF rect)
    {
        var cur = _clip;
        float x = System.MathF.Max(cur.X, rect.X);
        float y = System.MathF.Max(cur.Y, rect.Y);
        float r = System.MathF.Min(cur.Right, rect.Right);
        float b = System.MathF.Min(cur.Bottom, rect.Bottom);
        _clips.Add(_clip);
        _clip = new RectF(x, y, System.MathF.Max(0f, r - x), System.MathF.Max(0f, b - y));
    }

    public void PopClip()
    {
        if (_clips.Count == 0) { return; }
        _clip = _clips[_clips.Count - 1];
        _clips.RemoveAt(_clips.Count - 1);
    }

    /// <summary>把目标矩形钳到当前裁剪区。完全在外面返回 false。</summary>
    private bool Clamp(ref RectF dst, ref RectF src)
    {
        var c = _clip;
        if (dst.Right <= c.X || dst.X >= c.Right || dst.Bottom <= c.Y || dst.Y >= c.Bottom) { return false; }

        float nx = System.MathF.Max(dst.X, c.X);
        float ny = System.MathF.Max(dst.Y, c.Y);
        float nr = System.MathF.Min(dst.Right, c.Right);
        float nb = System.MathF.Min(dst.Bottom, c.Bottom);
        if (nr <= nx || nb <= ny) { return false; }

        // 源矩形按同一比例收缩，保持拉伸关系不变
        if (dst.W > 0f && dst.H > 0f && src.W > 0f && src.H > 0f)
        {
            float kx = src.W / dst.W;
            float ky = src.H / dst.H;
            src = new RectF(src.X + ((nx - dst.X) * kx), src.Y + ((ny - dst.Y) * ky),
                            (nr - nx) * kx, (nb - ny) * ky);
        }

        dst = new RectF(nx, ny, nr - nx, nb - ny);
        return true;
    }

    private static Rectangle ToRect(RectF r)
        => new((int)System.MathF.Round(r.X), (int)System.MathF.Round(r.Y),
               System.Math.Max(1, (int)System.MathF.Round(r.W)),
               System.Math.Max(1, (int)System.MathF.Round(r.H)));

    private static Color ToColor(Color32 c) => new(c.R, c.G, c.B, c.A);
}
