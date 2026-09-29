using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace HexCastingTerraria.Client;

/// <summary>
/// 画自己的哨卫（原版 HexAdditionalRenderers.renderSentinel）：一个内接于单位球的**正二十面体线框**，
/// 绕竖轴自转、上下浮动；大哨卫（扩展施法范围的那种）再绕水平轴慢慢翻滚。
/// 只有主人看得见，隔着墙也看得见（原版关了深度测试）。
///
/// 3D → 2D：原版是真三维的线框，这里做正交投影 —— 丢掉 z，屏幕 y 向下所以 y 取反。
/// 旋转照原版算（绕 y 自转的时候，投影出来就是那种「转动的多面体」观感）。
///
/// 尺寸：原版缩放 0.5 格。泰拉的角色比 MC 大一圈（42 像素 ≈ 2.6 格 vs MC 1.8 格），
/// 按 1 格 = 16 像素直接换算只有 8 像素半径、线条糊成一团；按角色比例放大到 12 像素半径（记在 AUDIT_VS_ORIGINAL）。
/// </summary>
internal static class SentinelRenderer
{
    private const float RadiusPx = 12f;
    private const float LineWidth = 2f;

    private static readonly Vector3 Top = new(0, 1, 0);
    private static readonly Vector3 Bottom = new(0, -1, 0);
    private static readonly Vector3[] TopRing = new Vector3[5];
    private static readonly Vector3[] BottomRing = new Vector3[5];

    static SentinelRenderer()
    {
        float theta = (float)System.Math.Atan2(0.5, 1);
        for (int i = 0; i < 5; i++)
        {
            float phi = i / 5f * MathHelper.TwoPi;
            var v = new Vector3(
                (float)(System.Math.Cos(theta) * System.Math.Cos(phi)),
                (float)System.Math.Sin(theta),
                (float)(System.Math.Cos(theta) * System.Math.Sin(phi)));
            TopRing[i] = v;
            BottomRing[i] = -v;
        }
    }

    /// <summary>在「游戏缩放」的界面层里调用（世界坐标 − screenPosition 即屏幕坐标）。</summary>
    public static void Draw(SpriteBatch sb)
    {
        if (Main.dedServ || Main.gameMenu) return;
        var player = Main.LocalPlayer;
        if (player is not { active: true }) return;
        if (Content.HexPlayer.Get(player).Sentinel is not { } s) return;

        // 原版 ClientTickCounter.getTotal() / 2（MC 刻，20/秒）；泰拉 60 帧/秒 → ÷3
        float time = Main.GameUpdateCount / 3f / 2f;
        const float bobSpeed = 1f / 20, magnitude = 0.1f, spinSpeed = 1f / 30;

        // 哨卫存的是法术坐标（+Y 朝上，单位格）
        var center = Content.HexSpaceWorld.ToWorldPixels(s.X, s.Y + System.Math.Sin(bobSpeed * time) * magnitude)
                     - Main.screenPosition;

        var rot = Matrix.CreateRotationY(spinSpeed * time);
        if (s.Great)
        {
            // 原版 mulPose(Y) 之后再 mulPose(X)：顶点先绕 X，再绕 Y
            rot = Matrix.CreateRotationX(spinSpeed * time / 8f) * rot;
        }

        var color = HexPigment.Current;
        Vector2 P(Vector3 v)
        {
            var r = Vector3.Transform(v, rot);
            return center + new Vector2(r.X, -r.Y) * RadiusPx;
        }
        void L(Vector3 a, Vector3 b) => HexPixel.DrawLine(sb, P(a), P(b), LineWidth, color);

        for (int side = 0; side <= 1; side++)
        {
            var ring = side == 0 ? BottomRing : TopRing;
            var apex = side == 0 ? Bottom : Top;
            for (int i = 0; i < 5; i++) L(apex, ring[i]);                 // 顶 / 底的伞骨
            for (int i = 0; i < 5; i++) L(ring[i % 5], ring[(i + 1) % 5]); // 环
        }
        for (int i = 0; i < 5; i++)                                          // 中间一圈三角
        {
            var bottom = BottomRing[i];
            L(TopRing[(i + 2) % 5], bottom);
            L(bottom, TopRing[(i + 3) % 5]);
        }
    }
}
