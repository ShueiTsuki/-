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

    public float LineHeight => Font.LineSpacing;

    public void DrawImage(string texture, RectF src, RectF dst, Color32 tint)
    {
        var tex = Texture(texture);
        if (tex is null) { return; }
        Main.spriteBatch.Draw(tex, ToRect(dst),
            new Rectangle((int)src.X, (int)src.Y, (int)src.W, (int)src.H), ToColor(tint));
    }

    public void DrawItem(string itemKey, RectF dst, float alpha)
    {
        int type = ItemType(itemKey);
        if (type <= 0) { return; }
        Main.instance.LoadItem(type);
        var tex = TextureAssets.Item[type].Value;
        var frame = Main.itemAnimations[type] is { } anim ? anim.GetFrame(tex) : tex.Frame();
        // 图标适配进格子，保持比例；小图标不放大超过格子（和物品栏一样）
        float scale = System.MathF.Min(dst.W / frame.Width, dst.H / frame.Height);
        var size = new Vector2(frame.Width, frame.Height) * scale;
        var pos = new Vector2(dst.X + ((dst.W - size.X) / 2f), dst.Y + ((dst.H - size.Y) / 2f));
        Main.spriteBatch.Draw(tex, pos, frame, Color.White * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    public void FillRect(RectF rect, Color32 color)
        => Main.spriteBatch.Draw(HexPixel.Value, ToRect(rect), ToColor(color));

    public void DrawLine(float x1, float y1, float x2, float y2, float width, Color32 color)
        => HexPixel.DrawLine(Main.spriteBatch, new Vector2(x1, y1), new Vector2(x2, y2), width, ToColor(color));

    public void FillCircle(float cx, float cy, float radius, Color32 color)
    {
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
        Main.spriteBatch.DrawString(Font, text, new Vector2(x, y), c, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        if (bold)
        {
            Main.spriteBatch.DrawString(Font, text, new Vector2(x + System.MathF.Max(1f, scale), y), c, 0f,
                Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
        return MeasureText(text, scale, bold);
    }

    public float MeasureText(string text, float scale, bool bold)
        => string.IsNullOrEmpty(text) ? 0f : (Font.MeasureString(text).X * scale) + (bold ? System.MathF.Max(1f, scale) : 0f);

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
