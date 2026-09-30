using System.Collections.Generic;
using HexCastingTerraria.Core.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Client.UI;

/// <summary>
/// <see cref="IBookCanvas"/> 的游戏内实现：全部用 <c>Main.spriteBatch</c> 画（不切到图元绘制，
/// 所以不需要中途打断界面层的批次 —— 上一版书 UI 的「世界渲染异常」就出在批次状态没还原）。
/// </summary>
public sealed class SpriteBatchBookCanvas : IBookCanvas
{
    private const string AtlasDir = "HexCastingTerraria/Client/UI/BookAtlas/";
    private readonly Dictionary<string, Asset<Texture2D>> _textures = new();
    private readonly Dictionary<string, int> _itemTypes = new();
    private static Texture2D? _circle;

    private static DynamicSpriteFont Font => FontAssets.MouseText.Value;

    /// <summary>基准行高（小字体）。渲染器按它算缩放，真正画的时候再挑合适的字体（见 <see cref="Pick"/>）。</summary>
    public float LineHeight => Font.LineSpacing;

    // ── 清晰度 ────────────────────────────────────────────────────
    //
    // 书是像素贴图 + 文字，两者要的采样方式相反：
    //   - 像素贴图（书页、物品图标）整数倍放大 → 点采样，边缘才锐利
    //   - 文字（泰拉的字体是位图字体）缩放时 → 线性采样，不然缩小后锯齿、放大后马赛克
    // 所以按绘制内容切采样器（批次只在真的要换时才重开）。
    // 另外泰拉的小字体（MouseText）行高只有 20 多像素，书在 1080p 下一行要 40 像素 ——
    // 放大近 2 倍就是「码率低」的糊字。所以要放大时换成大字体（DeathText）再**缩小**着画。

    private SamplerState? _sampler;

    /// <summary>这一帧的变换矩阵（HexBook 在开画前设置）。</summary>
    public Matrix Transform { get; set; } = Matrix.Identity;

    public void BeginFrame() => _sampler = null;

    private void Use(SamplerState s)
    {
        if (ReferenceEquals(_sampler, s)) { return; }
        if (_sampler is not null) { Main.spriteBatch.End(); }
        else { Main.spriteBatch.End(); }
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, s, DepthStencilState.None,
            RasterizerState.CullCounterClockwise, null, Transform);
        _sampler = s;
    }

    /// <summary>按要画的像素大小挑字体：超过小字体原尺寸就换大字体缩小画。</summary>
    private static (DynamicSpriteFont Font, float Scale) Pick(float scale)
    {
        if (scale <= 1.02f) { return (Font, scale); }
        var big = FontAssets.DeathText.Value;
        return (big, scale * Font.LineSpacing / big.LineSpacing);
    }

    public void DrawImage(string texture, RectF src, RectF dst, Color32 tint)
    {
        var tex = Texture(texture);
        if (tex is null) { return; }
        Use(SamplerState.PointClamp);
        Main.spriteBatch.Draw(tex, ToRect(dst),
            new Rectangle((int)src.X, (int)src.Y, (int)src.W, (int)src.H), ToColor(tint));
    }

    public void DrawItem(string itemKey, RectF dst, float alpha)
    {
        int type = ItemType(itemKey);
        if (type <= 0) { return; }
        Main.instance.LoadItem(type);
        Use(SamplerState.PointClamp);
        var tex = TextureAssets.Item[type].Value;
        var frame = Main.itemAnimations[type] is { } anim ? anim.GetFrame(tex) : tex.Frame();
        // 图标适配进格子，保持比例；小图标不放大超过格子（和物品栏一样）
        float scale = System.MathF.Min(dst.W / frame.Width, dst.H / frame.Height);
        var size = new Vector2(frame.Width, frame.Height) * scale;
        var pos = new Vector2(dst.X + ((dst.W - size.X) / 2f), dst.Y + ((dst.H - size.Y) / 2f));
        Main.spriteBatch.Draw(tex, pos, frame, Color.White * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    public void FillRect(RectF rect, Color32 color)
    {
        Use(SamplerState.PointClamp);
        Main.spriteBatch.Draw(HexPixel.Value, ToRect(rect), ToColor(color));
    }

    public void DrawLine(float x1, float y1, float x2, float y2, float width, Color32 color)
    {
        Use(SamplerState.LinearClamp);
        HexPixel.DrawLine(Main.spriteBatch, new Vector2(x1, y1), new Vector2(x2, y2), width, ToColor(color));
    }

    public void FillCircle(float cx, float cy, float radius, Color32 color)
    {
        Use(SamplerState.LinearClamp);
        var tex = Circle();
        float s = radius * 2f / tex.Width;
        Main.spriteBatch.Draw(tex, new Vector2(cx, cy), null, ToColor(color), 0f,
            new Vector2(tex.Width / 2f, tex.Height / 2f), s, SpriteEffects.None, 0f);
    }

    public float DrawText(string text, float x, float y, Color32 color, float scale, bool bold)
    {
        if (string.IsNullOrEmpty(text)) { return 0f; }
        // 纸面上的墨色文字：**不描边**（泰拉 UI 默认的 DrawBorderString 在纸上很难看）
        var c = ToColor(color);
        var (font, s) = Pick(scale);
        Use(SamplerState.LinearClamp);
        // 取整到像素：文字落在半像素上会整体发虚
        var pos = new Vector2(System.MathF.Round(x), System.MathF.Round(y));
        Main.spriteBatch.DrawString(font, text, pos, c, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
        if (bold)
        {
            Main.spriteBatch.DrawString(font, text, pos + new Vector2(System.MathF.Max(1f, scale), 0f), c, 0f,
                Vector2.Zero, s, SpriteEffects.None, 0f);
        }
        return MeasureText(text, scale, bold);
    }

    public float MeasureText(string text, float scale, bool bold)
    {
        if (string.IsNullOrEmpty(text)) { return 0f; }
        var (font, s) = Pick(scale);
        return (font.MeasureString(text).X * s) + (bold ? System.MathF.Max(1f, scale) : 0f);
    }

    /// <summary>物品键（<c>Mod:类名</c> / <c>Terraria:ItemID 字段名</c> / <c>Id:数字</c>）→ 物品类型；查不到返回 0。</summary>
    public int ItemType(string key)
    {
        if (string.IsNullOrEmpty(key)) { return 0; }
        if (_itemTypes.TryGetValue(key, out int t)) { return t; }
        t = 0;
        int colon = key.IndexOf(':');
        if (colon > 0)
        {
            string name = key.Substring(colon + 1);
            if (key.StartsWith("Id:", System.StringComparison.Ordinal))
            {
                int.TryParse(name, out t);
            }
            else if (key.StartsWith("Mod:", System.StringComparison.Ordinal))
            {
                if (ModContent.TryFind<ModItem>("HexCastingTerraria", name, out var mi)) { t = mi.Type; }
            }
            else if (ItemID.Search.TryGetId(name, out int id))
            {
                t = id;
            }
        }
        _itemTypes[key] = t;
        return t;
    }

    private Texture2D? Texture(string name)
    {
        if (!_textures.TryGetValue(name, out var asset))
        {
            string file = name switch
            {
                BookTextures.Book => "patchi_book",
                BookTextures.Crafting => "crafting",
                BookTextures.Filler => "patchi_filler",
                _ => name,
            };
            asset = ModContent.Request<Texture2D>(AtlasDir + file, AssetRequestMode.ImmediateLoad);
            _textures[name] = asset;
        }
        return asset.IsLoaded ? asset.Value : null;
    }

    /// <summary>抗锯齿圆（64×64），首次用到时在主线程生成。</summary>
    private static Texture2D Circle()
    {
        if (_circle is { IsDisposed: false }) { return _circle; }
        const int n = 64;
        var data = new Color[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                float a = MathHelper.Clamp((n / 2f) - d, 0f, 1f);
                data[(y * n) + x] = Color.White * a;
            }
        }
        _circle = new Texture2D(Main.graphics.GraphicsDevice, n, n);
        _circle.SetData(data);
        return _circle;
    }

    private static Rectangle ToRect(RectF r)
        => new((int)System.MathF.Round(r.X), (int)System.MathF.Round(r.Y),
               (int)System.MathF.Round(r.W), (int)System.MathF.Round(r.H));

    // 贴图与字体用的是预乘 alpha 的 AlphaBlend：颜色要乘上 alpha
    private static Color ToColor(Color32 c) => new Color(c.R, c.G, c.B) * (c.A / 255f);
}
