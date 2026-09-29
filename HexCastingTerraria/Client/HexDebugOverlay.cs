using System.Collections.Generic;
using HexCastingTerraria.Core;
using HexCastingTerraria.Core.Casting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using HexCastingTerraria.Config;

namespace HexCastingTerraria.Client;

/// <summary>
/// 开发者调试叠加层：把「看不见但必须确认」的东西画出来。
///
/// 为什么单独一个文件：这些绘制**只在调试开关打开时**才跑，
/// 混进 `HexClientSystem` 会让那个已经很长的主循环更难读。
/// 全部由 `HexClientConfig` 里的开关控制，默认关闭。
///
/// 三个叠加层对应的都是「靠猜会浪费一整天」的场景：
///   - 实体编号：`get_entity` / `zone_entity` 要的就是「种类#索引」，
///     而这个索引是**运行时分配**的，不看根本不知道
///   - 瞄准射线：射线类图案打在哪一格，直接决定法术效果
///   - 法术环状态：环跑到第几格、还剩多少媒质，出问题时要一眼看到
/// </summary>
internal static class HexDebugOverlay
{
    /// <summary>画所有调试叠加层。在界面层里调用。</summary>
    public static void Draw(SpriteBatch sb, float screenW, float screenH, Vector2 mouse)
    {
        if (Main.dedServ || Main.gameMenu) return;

        var config = HexClientConfig.Instance;

        if (config.ShowEntityIds) DrawEntityIds(sb, mouse);
        if (config.ShowAimRay) DrawAimRay(sb);
        if (config.ShowCircleDebug) DrawCircleState(sb, screenW);
    }

    /// <summary>鼠标指向的实体编号。</summary>
    private static void DrawEntityIds(SpriteBatch sb, Vector2 mouse)
    {
        var world = Main.MouseWorld;
        string? label = null;

        // 玩家（鼠标附近 32px 内最近的一个）
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            var p = Main.player[i];
            if (p is not { active: true }) continue;
            if (Vector2.DistanceSquared(p.Center, world) > 32f * 32f) continue;

            label = $"Player#{i}";
            break;
        }

        // NPC
        if (label == null)
        {
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                var n = Main.npc[i];
                if (n is not { active: true }) continue;
                if (!n.Hitbox.Contains(world.ToPoint())) continue;

                label = $"Npc#{i}  netID={n.netID}  {(n.townNPC ? "城镇" : n.CountsAsACritter ? "小动物" : "怪物")}";
                break;
            }
        }

        // 弹幕
        if (label == null)
        {
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                var pr = Main.projectile[i];
                if (pr is not { active: true }) continue;
                if (!pr.Hitbox.Contains(world.ToPoint())) continue;

                label = $"Projectile#{i}  type={pr.type}";
                break;
            }
        }

        // 掉落物
        if (label == null)
        {
            for (int i = 0; i < Main.maxItems; i++)
            {
                var it = Main.item[i];
                if (it is not { active: true }) continue;

                var box = new Rectangle((int)it.position.X, (int)it.position.Y, 16, 16);
                if (!box.Contains(world.ToPoint())) continue;

                label = $"Item#{i}  type={it.type}";
                break;
            }
        }

        // 图格编号（没有实体时也给一个，方便对坐标）
        int tx = (int)(world.X / HexUnits.PixelsPerTile);
        int ty = (int)(world.Y / HexUnits.PixelsPerTile);
        string tileLabel = $"Tile ({tx}, {ty})  " +
                           (WorldGen.InWorld(tx, ty, 1) && Main.tile[tx, ty].HasTile
                               ? $"type={Main.tile[tx, ty].TileType}"
                               : "空");

        var pos = mouse + new Vector2(18f, 18f);
        Terraria.Utils.DrawBorderString(sb, tileLabel, pos, new Color(200, 220, 255), 0.7f);

        if (label != null)
        {
            Terraria.Utils.DrawBorderString(sb, label, pos + new Vector2(0f, 18f),
                new Color(255, 230, 150), 0.75f);
        }
    }

    /// <summary>瞄准射线与命中格。</summary>
    private static void DrawAimRay(SpriteBatch sb)
    {
        var player = Main.LocalPlayer;
        if (player is not { active: true }) return;

        float ox = player.Center.X / HexUnits.PixelsPerTile;
        float oy = player.Center.Y / HexUnits.PixelsPerTile;

        var aim = Main.MouseWorld - player.Center;
        if (aim.LengthSquared() < 1e-4f) return;
        aim.Normalize();

        var hit = Core.World.TileRaycast.Cast(
            Content.TerrariaCastingWorld.SolidAt,
            ox, oy, aim.X, aim.Y, HexUnits.RaycastDistanceTiles);

        var from = player.Center - Main.screenPosition;
        Vector2 to = hit is { } h
            ? new Vector2((h.TileX + 0.5f) * HexUnits.PixelsPerTile,
                          (h.TileY + 0.5f) * HexUnits.PixelsPerTile) - Main.screenPosition
            : from + aim * HexUnits.RaycastDistancePixels;

        // 线段用 1x1 像素纹理铺：泰拉没有现成的画线 API
        DrawLine(sb, from, to, new Color(255, 120, 120, 160));

        if (hit is { } h2)
        {
            var tl = new Vector2(h2.TileX * HexUnits.PixelsPerTile,
                                 h2.TileY * HexUnits.PixelsPerTile) - Main.screenPosition;
            var rect = new Rectangle((int)tl.X, (int)tl.Y,
                (int)HexUnits.PixelsPerTile, (int)HexUnits.PixelsPerTile);

            DrawRectOutline(sb, rect, new Color(255, 90, 90, 220));
            Terraria.Utils.DrawBorderString(sb,
                $"命中 ({h2.TileX}, {h2.TileY})",
                tl + new Vector2(0f, -18f), new Color(255, 160, 160), 0.7f);
        }
        else
        {
            Terraria.Utils.DrawBorderString(sb, "射线未命中（到达最大射程）",
                to + new Vector2(0f, -18f), new Color(255, 160, 160), 0.7f);
        }
    }

    /// <summary>正在运行的法术环状态。</summary>
    private static void DrawCircleState(SpriteBatch sb, float screenW)
    {
        float y = screenW > 0 ? 220f : 220f;   // 放在 HUD 下方，避免和媒质环打架
        float x = 18f;
        bool any = false;

        foreach (var pair in TileEntity.ByID)
        {
            if (pair.Value is not Content.Tiles.HexImpetusEntity impetus) continue;
            if (!impetus.IsRunning) continue;

            if (!any)
            {
                Terraria.Utils.DrawBorderString(sb, "── 法术环 ──",
                    new Vector2(x, y), new Color(200, 220, 255), 0.75f);
                y += 20f;
                any = true;
            }

            string line = $"({impetus.Position.X},{impetus.Position.Y})  " +
                          $"当前格 ({impetus.CurrentX},{impetus.CurrentY})  " +
                          $"已走 {impetus.ReachedCount}  媒质 {Core.Media.MediaConstants.Format(impetus.Media)}";

            Terraria.Utils.DrawBorderString(sb, line, new Vector2(x, y),
                new Color(255, 230, 170), 0.7f);
            y += 17f;
        }

        if (!any)
        {
            Terraria.Utils.DrawBorderString(sb, "（当前没有正在运行的法术环）",
                new Vector2(x, y), new Color(150, 150, 170), 0.65f);
        }
    }

    // ── 画线/画框的小工具（泰拉没有现成的）────────────────────────

    private static void DrawLine(SpriteBatch sb, Vector2 a, Vector2 b, Color color, float width = 1f)
    {
        var delta = b - a;
        float length = delta.Length();
        if (length < 0.5f) return;

        sb.Draw(HexPixel.Value, a, null, color, delta.ToRotation(),
            new Vector2(0f, 0.5f), new Vector2(length, width), SpriteEffects.None, 0f);
    }

    private static void DrawRectOutline(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
    {
        sb.Draw(HexPixel.Value, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        sb.Draw(HexPixel.Value, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        sb.Draw(HexPixel.Value, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        sb.Draw(HexPixel.Value, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }
}
