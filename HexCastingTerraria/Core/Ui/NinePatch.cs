namespace HexCastingTerraria.Core.Ui;

/// <summary>
/// 9 宫格（nine-patch）绘制。
///
/// ## 为什么必须有这个原语
///
/// 泰拉的面板与物品格**全是 9 宫格贴图** —— `UI_PanelBackground`(28×28)、
/// `UI_PanelBorder`(28×28)、`UI_Bestiary_Slot_Back`(70×70) 等等，
/// 四角不拉伸、四边单向拉伸、中间双向拉伸。
///
/// 我第一版原版皮肤是**手画渐变矩形**顶上去的，出了对照图才看清：
/// 泰拉的做法是「纯白 9 宫格底 + 纯白 9 宫格描边，两层叠起来由代码染色」——
/// 跟手画渐变从根上不是一回事。（见 `_tools/vanilla_ui_sheet.png` 第 1、2 张。）
///
/// ## 为什么放 Core/ 并且只用 DrawImage 实现
///
/// 它不需要新画布原语 —— 切九块、各调一次 <see cref="IBookCanvas.DrawImage"/> 就行。
/// 于是**离屏渲染与游戏内渲染自动共用这一份实现**，不会两边各写一遍然后慢慢对不上。
/// </summary>
public static class NinePatch
{
    /// <summary>
    /// 把 <paramref name="src"/> 这块贴图按 9 宫格铺满 <paramref name="dst"/>。
    /// </summary>
    /// <param name="corner">四角保留的像素尺寸（源图坐标）。泰拉面板常用 6~12。</param>
    public static void Draw(IBookCanvas canvas, string asset, RectF src, RectF dst, float corner, Color32 tint)
    {
        if (dst.W <= 0f || dst.H <= 0f || src.W <= 0f || src.H <= 0f) { return; }

        // 角不能超过源图或目标的一半，否则中间段会变成负数
        float c = corner;
        c = System.MathF.Min(c, System.MathF.Min(src.W / 2f, src.H / 2f));
        c = System.MathF.Min(c, System.MathF.Min(dst.W / 2f, dst.H / 2f));
        if (c <= 0f) { canvas.DrawImage(asset, src, dst, tint); return; }

        // 源：左/中/右 三列宽，上/中/下 三行高
        float sx0 = src.X, sx1 = src.X + c, sx2 = src.Right - c;
        float sw0 = c, sw1 = src.W - (2f * c), sw2 = c;
        float sy0 = src.Y, sy1 = src.Y + c, sy2 = src.Bottom - c;
        float sh0 = c, sh1 = src.H - (2f * c), sh2 = c;

        // 目标同理
        float dx0 = dst.X, dx1 = dst.X + c, dx2 = dst.Right - c;
        float dw0 = c, dw1 = dst.W - (2f * c), dw2 = c;
        float dy0 = dst.Y, dy1 = dst.Y + c, dy2 = dst.Bottom - c;
        float dh0 = c, dh1 = dst.H - (2f * c), dh2 = c;

        var sx = new[] { sx0, sx1, sx2 };
        var sw = new[] { sw0, sw1, sw2 };
        var sy = new[] { sy0, sy1, sy2 };
        var sh = new[] { sh0, sh1, sh2 };
        var dx = new[] { dx0, dx1, dx2 };
        var dw = new[] { dw0, dw1, dw2 };
        var dy = new[] { dy0, dy1, dy2 };
        var dh = new[] { dh0, dh1, dh2 };

        for (int r = 0; r < 3; r++)
        {
            for (int col = 0; col < 3; col++)
            {
                // 中间那一格若源尺寸为 0（贴图太小），跳过而不是画成负宽
                if (sw[col] <= 0f || sh[r] <= 0f || dw[col] <= 0f || dh[r] <= 0f) { continue; }

                canvas.DrawImage(asset,
                    new RectF(sx[col], sy[r], sw[col], sh[r]),
                    new RectF(dx[col], dy[r], dw[col], dh[r]),
                    tint);
            }
        }
    }
}
