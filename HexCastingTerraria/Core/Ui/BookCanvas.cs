namespace HexCastingTerraria.Core.Ui;

// ── 书的绘制契约 ──────────────────────────────────────────────────────
//
// 渲染器（PatchouliRenderer）只依赖这里的接口，所以它在 Core（零 XNA），
// 同一份代码既驱动游戏内绘制（Client/UI/SpriteBatchBookCanvas），也驱动离线出图（tests/drawtest）。
// 坐标一律是**像素**；换算 Patchouli 的 GUI 单位由渲染器负责。

public readonly struct RectF
{
    public readonly float X;
    public readonly float Y;
    public readonly float W;
    public readonly float H;

    public RectF(float x, float y, float w, float h)
    {
        X = x;
        Y = y;
        W = w;
        H = h;
    }

    public float Right => X + W;
    public float Bottom => Y + H;

    public bool Contains(float px, float py) => px >= X && px < Right && py >= Y && py < Bottom;

    public override string ToString() => $"({X:0.#}, {Y:0.#}, {W:0.#}×{H:0.#})";
}

public readonly struct Color32
{
    public Color32(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public byte A { get; }

    public static Color32 Rgb(int rgb, byte a = 255) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, a);

    public Color32 WithAlpha(byte a) => new(R, G, B, a);
}

/// <summary>书用到的贴图。与原版同名同尺寸，源矩形用原版贴图坐标。</summary>
public static class BookTextures
{
    /// <summary>书体图集 512×256（咒法学的 patchi_book.png）。</summary>
    public const string Book = "book";
    /// <summary>配方框图集 128×256（Patchouli 的 crafting.png）。</summary>
    public const string Crafting = "crafting";
    /// <summary>空白页装饰 128×128（咒法学的 patchi_filler.png）。</summary>
    public const string Filler = "filler";
}

public interface IBookCanvas
{
    /// <summary>字体在缩放 1 时的行高（像素）。渲染器据此把文字缩放到 Patchouli 的 9 单位行高。</summary>
    float LineHeight { get; }

    void DrawImage(string texture, RectF src, RectF dst, Color32 tint);

    /// <summary>在 <paramref name="dst"/> 里居中画一个物品图标（保持比例）。物品键见 <see cref="IBookData"/>。</summary>
    void DrawItem(string itemKey, RectF dst, float alpha);

    void FillRect(RectF rect, Color32 color);

    void DrawLine(float x1, float y1, float x2, float y2, float width, Color32 color);

    void FillCircle(float cx, float cy, float radius, Color32 color);

    /// <summary>画一段不换行的文字，返回宽度（像素）。</summary>
    float DrawText(string text, float x, float y, Color32 color, float scale, bool bold);

    float MeasureText(string text, float scale, bool bold);
}

/// <summary>一条配方（泰拉的真实配方，不是 MC 的有序合成）。</summary>
public sealed class BookRecipe
{
    public string Result { get; set; } = string.Empty;
    public int ResultCount { get; set; } = 1;
    public System.Collections.Generic.List<(string Item, int Count)> Ingredients { get; } = new();
    /// <summary>合成站的物品键（工作台、铁砧……）；空 = 徒手。</summary>
    public string Station { get; set; } = string.Empty;
}

/// <summary>
/// 书需要、但 Core 拿不到的游戏数据（物品名、配方）。游戏内由 Client 实现，离线测试给假数据。
///
/// 物品键格式：<c>Mod:类名</c>（本模组物品）或 <c>Terraria:ItemID 字段名</c>（原版物品）。
/// </summary>
public interface IBookData
{
    string ItemName(string itemKey);
    BookRecipe? FindRecipe(string resultItemKey);

    /// <summary>
    /// 条目的解锁条件（原版 advancement，如 <c>hexcasting:root</c>）是否已达成。
    /// 空字符串 = 没有条件。未解锁的条目照 Patchouli 显示成锁，点不开。
    /// </summary>
    bool IsUnlocked(string advancement);
}
