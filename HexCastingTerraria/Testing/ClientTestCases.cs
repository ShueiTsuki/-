using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HexCastingTerraria.Client;
using HexCastingTerraria.Config;
using HexCastingTerraria.Content;
using HexCastingTerraria.Content.Buffs;
using HexCastingTerraria.Content.Items;
using HexCastingTerraria.Content.Tiles;
using HexCastingTerraria.Core.Casting.Circles;
using HexCastingTerraria.Core.Casting.Eval;
using HexCastingTerraria.Core.Casting.Iotas;
using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace HexCastingTerraria.Testing;

/// <summary>
/// 客户端自动测试的内容（框架见 <see cref="ClientTestSystem"/>）。
///
/// 场地：出生点上空清出一块 76 格宽的空地（一屏 1280 像素正好装下），背后铺玻璃墙（墙挂物件要有墙，玻璃墙不挡阳光）。
///   最上面一层是施法区：天花板、地板之间 9 格高，玩家站在左边；
///   下面每层 8 格（7 格空间）放模组里全部能放的方块，每件占一格加 1 格间隔，排满一层换下一层。
/// 期望值一律按原版的规则写在这里，不调用被测的换算代码去算期望（否则错了也照样对得上）。
/// </summary>
internal static class ClientTestCases
{
    private const int ArenaWidth = 76;
    private const int SpellBand = 10;
    private const int RowHeight = 8;

    /// <summary>MC 速度单位（方块 / 刻）换成泰拉的像素 / 帧：16 像素 × 20 刻 / 60 帧。</summary>
    private const float McMotionToPixels = 16f / 3f;

    public static IEnumerable<(string, Func<ClientTestRun, IEnumerable<int>>)> Sections(string phase)
    {
        if (phase == "verify")
        {
            return new (string, Func<ClientTestRun, IEnumerable<int>>)[]
            {
                ("环境", CheckEnvironment),
                ("存档读回", VerifySaved),
                ("挖方块", MineArena),
            };
        }
        return new (string, Func<ClientTestRun, IEnumerable<int>>)[]
        {
            ("环境", CheckEnvironment),
            ("贴图", Textures),
            ("场地", BuildArena),
            ("放方块", PlaceAll),
            ("施法", Spells),
            ("网格大小", GridZoom),
            ("存档", SaveState),
        };
    }

    private static Mod ThisMod => global::HexCastingTerraria.HexCastingTerraria.Instance!;

    internal static IEnumerable<int> ModItemTypes() => ThisMod.GetContent<ModItem>().Select(i => i.Type).OrderBy(t => t);

    internal static IEnumerable<int> ModBuffTypes() => ThisMod.GetContent<ModBuff>().Select(b => b.Type).OrderBy(t => t);

    // ── 环境 ──────────────────────────────────────────────────────────

    private static IEnumerable<int> CheckEnvironment(ClientTestRun run)
    {
        var p = Main.LocalPlayer;
        run.Check("进了世界", !Main.gameMenu && p.active);
        run.Check("单机", Main.netMode == NetmodeID.SinglePlayer, $"netMode = {Main.netMode}");
        run.Check("测试角色", p.name == ClientTestSystem.PlayerName, p.name);
        run.Check("游戏语言是简体中文", Language.ActiveCulture.Name == "zh-Hans", Language.ActiveCulture.Name);
        run.Info("world", Main.worldName);
        run.Info("worldSize", $"{Main.maxTilesX} x {Main.maxTilesY}");
        run.Info("spawn", $"{Main.spawnTileX}, {Main.spawnTileY}");
        run.Info("screen", $"{Main.screenWidth} x {Main.screenHeight}");
        run.Info("modVersion", ThisMod.Version.ToString());
        yield break;
    }

    // ── 贴图 ──────────────────────────────────────────────────────────

    private static IEnumerable<int> Textures(ClientTestRun run)
    {
        var mod = ThisMod;
        var missing = new List<string>();
        int count = 0;

        foreach (var item in mod.GetContent<ModItem>())
        {
            count++;
            Main.instance.LoadItem(item.Type);
            if (!Loaded(TextureAssets.Item[item.Type])) missing.Add("物品 " + item.Name);
        }
        foreach (var tile in mod.GetContent<ModTile>())
        {
            count++;
            Main.instance.LoadTiles(tile.Type);
            if (!Loaded(TextureAssets.Tile[tile.Type])) missing.Add("方块 " + tile.Name);
        }
        foreach (var wall in mod.GetContent<ModWall>())
        {
            count++;
            Main.instance.LoadWall(wall.Type);
            if (!Loaded(TextureAssets.Wall[wall.Type])) missing.Add("墙 " + wall.Name);
        }
        foreach (var buff in mod.GetContent<ModBuff>())
        {
            count++;
            if (!Loaded(TextureAssets.Buff[buff.Type])) missing.Add("增益 " + buff.Name);
        }
        foreach (var proj in mod.GetContent<ModProjectile>())
        {
            count++;
            Main.instance.LoadProjectile(proj.Type);
            if (!Loaded(TextureAssets.Projectile[proj.Type])) missing.Add("弹幕 " + proj.Name);
        }
        run.Check($"本模组全部贴图都加载了（{count} 张）", missing.Count == 0, string.Join("、", missing));

        // 运行时用泰拉自带贴图拼出来的几张（VanillaRecolor）：拼图失败时会退回原图，玩家看到的是一瓶小治疗药水
        CheckRecolored(run, "明晰药水", TextureAssets.Item[ModContent.ItemType<EnlargeGridPotion>()], $"Images/Item_{ItemID.LesserHealingPotion}");
        CheckRecolored(run, "蒙翳药水", TextureAssets.Item[ModContent.ItemType<ShrinkGridPotion>()], $"Images/Item_{ItemID.LesserHealingPotion}");
        CheckRecolored(run, "明晰增益图标", TextureAssets.Buff[ModContent.BuffType<EnlargeGrid>()], "Images/Buff");
        CheckRecolored(run, "蒙翳增益图标", TextureAssets.Buff[ModContent.BuffType<ShrinkGrid>()], "Images/Buff");
        CheckRecolored(run, "共振药水", TextureAssets.Item[ModContent.ItemType<ResonancePotion>()], $"Images/Item_{ItemID.MiningPotion}");
        CheckRecolored(run, "共振增益图标", TextureAssets.Buff[ModContent.BuffType<Resonance>()], $"Images/Buff_{BuffID.Mining}");
        CheckRecolored(run, "深板岩（方块）", TextureAssets.Tile[ModContent.TileType<Deepslate>()], $"Images/Tiles_{TileID.Granite}");
        CheckRecolored(run, "深板岩（物品）", TextureAssets.Item[ModContent.ItemType<DeepslateItem>()], $"Images/Item_{ItemID.Granite}");

        // 明晰 / 蒙翳的液体换成原版效果颜色（HexMobEffects：明晰 0xc875ff、蒙翳 0xc0e660）：改过的像素色相要落在效果色附近
        CheckLiquidHue(run, "明晰药水", ModContent.ItemType<EnlargeGridPotion>(), new Color(0xc8, 0x75, 0xff));
        CheckLiquidHue(run, "蒙翳药水", ModContent.ItemType<ShrinkGridPotion>(), new Color(0xc0, 0xe6, 0x60));

        // 蒙翳是减益：图标框是泰拉减益的红框，明晰是增益的蓝框
        var enlargeBuff = Pixels(TextureAssets.Buff[ModContent.BuffType<EnlargeGrid>()].Value);
        var shrinkBuff = Pixels(TextureAssets.Buff[ModContent.BuffType<ShrinkGrid>()].Value);
        var frameBlue = new Color(2, 139, 218);
        var frameRed = new Color(231, 0, 0);
        run.Check("明晰图标是增益的蓝框", enlargeBuff.Any(c => Same(c, frameBlue)) && !enlargeBuff.Any(c => Same(c, frameRed)));
        run.Check("蒙翳图标是减益的红框", shrinkBuff.Any(c => Same(c, frameRed)) && !shrinkBuff.Any(c => Same(c, frameBlue)));

        // 物品 + 增益图标总览截图（顺序记进报告，方便对照）
        var order = new List<string>();
        foreach (int type in ModItemTypes()) order.Add(Lang.GetItemNameValue(type));
        foreach (int type in ModBuffTypes()) order.Add("增益：" + Lang.GetBuffName(type));
        run.Info("galleryOrder", order);
        Main.hideUI = false;
        ClientTestSystem.ShowGallery = true;
        yield return 3;
        foreach (int w in Shot("gallery")) yield return w;
        ClientTestSystem.ShowGallery = false;
    }

    private static bool Loaded(Asset<Texture2D>? asset)
    {
        if (asset == null) return false;
        if (!asset.IsLoaded) asset.Wait?.Invoke();
        return asset.IsLoaded && asset.Value is { Width: > 0, Height: > 0 };
    }

    private static void CheckRecolored(ClientTestRun run, string what, Asset<Texture2D> art, string vanillaPath)
    {
        var source = Main.Assets.Request<Texture2D>(vanillaPath, AssetRequestMode.ImmediateLoad).Value;
        var made = art.Value;
        bool sameSize = made.Width == source.Width && made.Height == source.Height;
        bool changed = sameSize && !Pixels(made).SequenceEqual(Pixels(source));
        run.Check($"{what}是改过色的游戏原图", sameSize && changed,
            sameSize ? (changed ? "" : "和原图一模一样（拼图没生效）") : $"尺寸 {made.Width}x{made.Height}，原图 {source.Width}x{source.Height}");
    }

    private static void CheckLiquidHue(ClientTestRun run, string what, int itemType, Color effect)
    {
        var source = Pixels(Main.Assets.Request<Texture2D>($"Images/Item_{ItemID.LesserHealingPotion}", AssetRequestMode.ImmediateLoad).Value);
        var made = Pixels(TextureAssets.Item[itemType].Value);
        if (made.Length != source.Length)
        {
            run.Check($"{what}的液体是效果色", false, "尺寸和原图不同");
            return;
        }
        double sx = 0, sy = 0;
        int n = 0;
        for (int i = 0; i < made.Length; i++)
        {
            if (made[i] == source[i] || made[i].A == 0) continue;
            var (h, s) = HueSat(made[i]);
            if (s < 0.15) continue;
            sx += Math.Cos(h * Math.PI / 180);
            sy += Math.Sin(h * Math.PI / 180);
            n++;
        }
        double mean = (Math.Atan2(sy, sx) * 180 / Math.PI + 360) % 360;
        double target = HueSat(effect).Hue;
        double diff = Math.Abs(((mean - target) % 360 + 540) % 360 - 180);
        run.Check($"{what}的液体是效果色", n > 0 && diff < 25, $"改过的像素 {n} 个，平均色相 {mean:0}°，效果色 {target:0}°");
    }

    private static Color[] Pixels(Texture2D tex)
    {
        var data = new Color[tex.Width * tex.Height];
        tex.GetData(data);
        return data;
    }

    private static bool Same(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A > 0;

    private static (double Hue, double Sat) HueSat(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        if (d <= 0) return (0, 0);
        double h = max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max <= 0 ? 0 : d / max);
    }

    /// <summary>截一张图：下一帧画完时存盘，等它存好再往下走。</summary>
    private static IEnumerable<int> Shot(string name)
    {
        ClientTestSystem.RequestShot(name);
        int guard = 0;
        while (ClientTestSystem.ShotPending && guard++ < 30) yield return 0;
    }

    // ── 场地 ──────────────────────────────────────────────────────────

    internal sealed class Placement
    {
        public string Item { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string Tile { get; set; } = "";
        public int Style { get; set; }
        public int CellX { get; set; }
        public int Row { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Placed { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string How { get; set; } = "";
        public bool ExpectEntity { get; set; }
    }

    internal sealed class WallPlacement
    {
        public string Item { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string Wall { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
    }

    internal sealed class ArenaState
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Rows { get; set; }
        public List<Placement> Placements { get; set; } = new();
        public List<WallPlacement> Walls { get; set; } = new();
        public int SlateX { get; set; } = -1;
        public int SlateY { get; set; } = -1;
        public int ShelfX { get; set; } = -1;
        public int ShelfY { get; set; } = -1;
        public int ScrollX { get; set; } = -1;
        public int ScrollY { get; set; } = -1;
        public int OldScrollX { get; set; } = -1;
        public int OldScrollY { get; set; } = -1;
        public int FocusSlot { get; set; } = 2;
    }

    private static ArenaState _arena = new();

    private static int SpellFloor => _arena.Top + SpellBand;

    private static int RowCeil(int row) => _arena.Top + SpellBand + row * RowHeight;

    private static IEnumerable<int> BuildArena(ClientTestRun run)
    {
        var mod = ThisMod;
        _arena = new ArenaState();

        // 能放的东西：本模组物品里 createTile 是本模组方块的，按 (方块, 样式) 去重
        var seen = new HashSet<(int, int)>();
        int cellX = 1, row = 0;
        foreach (int type in ModItemTypes())
        {
            var item = new Item();
            item.SetDefaults(type);
            if (item.createTile > -1 && TileLoader.GetTile(item.createTile) is { } mt && mt.Mod == mod)
            {
                if (!seen.Add((item.createTile, item.placeStyle))) continue;
                var data = TileObjectData.GetTileData(item.createTile, item.placeStyle);
                int w = data?.Width ?? 1, h = data?.Height ?? 1;
                if (cellX + w > ArenaWidth - 1)
                {
                    row++;
                    cellX = 1;
                }
                _arena.Placements.Add(new Placement
                {
                    Item = ItemLoader.GetItem(type).Name,
                    ItemName = Lang.GetItemNameValue(type),
                    Tile = mt.Name,
                    Style = item.placeStyle,
                    CellX = cellX,
                    Row = row,
                    Width = w,
                    Height = h,
                    ExpectEntity = data?.HookPostPlaceMyPlayer.hook != null,
                });
                cellX += w + 1;
            }
            else if (item.createWall > 0 && WallLoader.GetWall(item.createWall) is { } mw && mw.Mod == mod)
            {
                _arena.Walls.Add(new WallPlacement { Item = ItemLoader.GetItem(type).Name, ItemName = Lang.GetItemNameValue(type), Wall = mw.Name });
            }
        }
        // 墙排在最后一层之后，每面墙占 3 格宽
        int wallRow = row + 1;
        int wx = 1;
        foreach (var wall in _arena.Walls)
        {
            if (wx + 3 > ArenaWidth - 1)
            {
                wallRow++;
                wx = 1;
            }
            wall.X = wx;
            wall.Y = wallRow;
            wx += 4;
        }
        _arena.Rows = (_arena.Walls.Count > 0 ? wallRow : row) + 1;

        int height = SpellBand + _arena.Rows * RowHeight + 1;
        _arena.Left = Main.spawnTileX - ArenaWidth / 2;
        _arena.Top = Math.Max(Main.spawnTileY - 30 - height, (int)(Main.worldSurface * 0.35) + 4);
        run.Info("arena", $"左上角 ({_arena.Left}, {_arena.Top})，{ArenaWidth} x {height} 格，{_arena.Rows} 层");
        run.Check("场地在世界里", WorldGen.InWorld(_arena.Left - 3, _arena.Top - 3) && WorldGen.InWorld(_arena.Left + ArenaWidth + 3, _arena.Top + height + 3));

        // 清空（方块、墙、液体）→ 玻璃墙 → 各层的天花板 / 地板（石头）
        for (int x = _arena.Left - 3; x < _arena.Left + ArenaWidth + 3; x++)
        {
            for (int y = _arena.Top - 3; y < _arena.Top + height + 3; y++)
            {
                var t = Main.tile[x, y];
                t.ClearEverything();
                if (x >= _arena.Left && x < _arena.Left + ArenaWidth && y >= _arena.Top && y < _arena.Top + height)
                {
                    t.WallType = WallID.Glass;
                }
            }
        }
        for (int x = _arena.Left; x < _arena.Left + ArenaWidth; x++)
        {
            SetSolid(x, _arena.Top, TileID.Stone);
            for (int r = 0; r <= _arena.Rows; r++) SetSolid(x, RowCeil(r), TileID.Stone);
        }
        WorldGen.RangeFrame(_arena.Left - 3, _arena.Top - 3, _arena.Left + ArenaWidth + 3, _arena.Top + height + 3);

        // 场上别留怪和掉落物
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            if (Main.npc[i].active && !Main.npc[i].townNPC) Main.npc[i].active = false;
        }
        for (int i = 0; i < Main.maxItems; i++) Main.item[i].active = false;

        // 玩家站到施法区左边，面朝右
        var p = Main.LocalPlayer;
        p.position = new Vector2((_arena.Left + 4) * 16, SpellFloor * 16 - p.height);
        p.velocity = Vector2.Zero;
        p.fallStart = (int)(p.position.Y / 16);
        p.direction = 1;
        ClientTestSystem.MouseOffset = new Vector2(400, 0);
        yield return 20;
        run.Check("玩家站在施法区地板上", Math.Abs(p.Bottom.Y - SpellFloor * 16) < 1f && p.velocity.Y == 0, $"脚底 {p.Bottom.Y / 16:0.00}，地板 {SpellFloor}");
    }

    private static void SetSolid(int x, int y, int type)
    {
        var t = Main.tile[x, y];
        t.ClearTile();
        t.HasTile = true;
        t.TileType = (ushort)type;
    }

    // ── 放方块 ────────────────────────────────────────────────────────

    private static IEnumerable<int> PlaceAll(ClientTestRun run)
    {
        foreach (var pl in _arena.Placements)
        {
            int left = _arena.Left + pl.CellX;
            int ceil = RowCeil(pl.Row);
            int floor = RowCeil(pl.Row + 1);
            int type = TileType(pl.Tile);
            var (ok, x, y, how) = TryPlace(type, pl.Style, left, ceil, floor);
            pl.Placed = ok;
            pl.X = x;
            pl.Y = y;
            pl.How = how;
            run.Check($"放得下：{pl.ItemName}", ok, ok ? how : $"{pl.Tile} 样式 {pl.Style}，{pl.Width}x{pl.Height}");
            if (ok && pl.ExpectEntity)
            {
                bool te = TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var e) && e is ModTileEntity { } m && m.Mod == ThisMod;
                run.Check($"放下以后有图格实体：{pl.ItemName}", te, $"左上角 ({x}, {y})");
            }
        }

        foreach (var wall in _arena.Walls)
        {
            int type = WallTypeOf(wall.Wall);
            int left = _arena.Left + wall.X;
            int ceil = RowCeil(wall.Y);
            bool ok = true;
            for (int x = left; x < left + 3; x++)
            {
                for (int y = ceil + 2; y < ceil + 5; y++)
                {
                    var t = Main.tile[x, y];
                    t.WallType = WallID.None;
                    WorldGen.PlaceWall(x, y, type, mute: true);
                    ok &= Main.tile[x, y].WallType == type;
                }
            }
            wall.X = left + 1;
            wall.Y = ceil + 3;
            run.Check($"放得下墙：{wall.ItemName}", ok);
        }
        WorldGen.RangeFrame(_arena.Left, _arena.Top, _arena.Left + ArenaWidth, RowCeil(_arena.Rows));

        foreach (int w in ArenaShots("arena")) yield return w;
    }

    private static IEnumerable<int> ArenaShots(string prefix)
    {
        Main.hideUI = true;
        int perShot = 5;
        for (int r = 0, n = 0; r < _arena.Rows; r += perShot, n++)
        {
            int rows = Math.Min(perShot, _arena.Rows - r);
            float cy = (RowCeil(r) + rows * RowHeight / 2f) * 16f;
            ClientTestSystem.Camera = new Vector2((_arena.Left + ArenaWidth / 2f) * 16f, cy);
            yield return 40;
            foreach (int w in Shot($"{prefix}-{n}")) yield return w;
        }
        ClientTestSystem.Camera = null;
        Main.hideUI = false;
    }

    private static int TileType(string name) => ThisMod.TryFind<ModTile>(name, out var t) ? t.Type : -1;

    private static int WallTypeOf(string name) => ThisMod.TryFind<ModWall>(name, out var w) ? w.Type : -1;

    /// <summary>
    /// 照玩家放置的路子放：有 TileObjectData 的走 TileObject.CanPlace / Place，再调放置后的钩子（图格实体就是这一步放的，
    /// tML 1.4.4 曾经在这里什么都不放）；单格方块走 WorldGen.PlaceTile。依次试：站在地板上、挂在天花板下、贴在墙上；
    /// 上下都要靠的（门）先在正上方补一块天花板；限定了底下方块的，把底下换成第一种允许的方块再试。
    /// </summary>
    private static (bool Ok, int X, int Y, string How) TryPlace(int type, int style, int left, int ceil, int floor)
    {
        var data = TileObjectData.GetTileData(type, style);
        if (data == null)
        {
            WorldGen.PlaceTile(left, floor - 1, type, mute: true, forced: false, plr: Main.myPlayer, style: style);
            bool ok = Main.tile[left, floor - 1].HasTile && Main.tile[left, floor - 1].TileType == type;
            return (ok, left, floor - 1, "单格方块");
        }

        if (data.AnchorTop.type != AnchorType.None && data.AnchorBottom.type != AnchorType.None)
        {
            for (int x = left; x < left + data.Width; x++) SetSolid(x, floor - data.Height - 1, TileID.Stone);
            WorldGen.RangeFrame(left, floor - data.Height - 2, left + data.Width, floor - data.Height);
        }

        foreach (var (top, how) in new[] { (floor - data.Height, "地板上"), (ceil + 1, "天花板下"), (ceil + 2, "墙上") })
        {
            var r = PlaceObject(type, style, left, top, data);
            if (r.Ok) return (true, r.X, r.Y, how);
        }

        if (data.AnchorValidTiles is { Length: > 0 } valid)
        {
            for (int x = left; x < left + data.Width; x++) SetSolid(x, floor, valid[0]);
            WorldGen.RangeFrame(left, floor - 1, left + data.Width, floor + 1);
            var r = PlaceObject(type, style, left, floor - data.Height, data);
            if (r.Ok) return (true, r.X, r.Y, "地板上（底下换成 " + TileName(valid[0]) + "）");
        }
        return (false, left, floor - 1, "");
    }

    private static (bool Ok, int X, int Y) PlaceObject(int type, int style, int left, int top, TileObjectData data)
    {
        int x = left + data.Origin.X;
        int y = top + data.Origin.Y;
        if (!TileObject.CanPlace(x, y, type, style, 1, out var obj)) return (false, 0, 0);
        if (!TileObject.Place(obj)) return (false, 0, 0);
        TileObjectData.CallPostPlacementPlayerHook(x, y, type, style, 1, obj.alternate, obj);
        return (true, obj.xCoord, obj.yCoord);
    }

    private static string TileName(int type) => TileLoader.GetTile(type)?.Name ?? TileID.Search.GetName(type);

    // ── 施法（泰拉世界里真的生效） ──────────────────────────────────────

    private static HexPattern Pat(string id)
    {
        var def = PatternRegistry.FindById("hexcasting:" + id) ?? throw new InvalidOperationException("注册表里没有图案 hexcasting:" + id);
        return PatternRegistry.PatternInThisWorld(def);
    }

    private static bool Cast(ClientTestRun run, string id)
    {
        HexVmState.EvaluatePattern(Main.LocalPlayer, Pat(id));
        return run.Check($"{id} 执行成功", HexVmState.LastResolution == ResolvedPatternType.Evaluated,
            $"{HexVmState.LastResolution} {HexVmState.LastError}");
    }

    private static void Push(Iota iota) => HexVmState.PushIota(Main.LocalPlayer, iota);

    private static Iota? Top => HexVmState.Stack.Count > 0 ? HexVmState.Stack[^1] : null;

    /// <summary>泰拉图格 (tx, ty) 那一格方块的中心，换成法术坐标：x 不变，y 朝上，世界底 = 0（方块下标 = 世界高 − 1 − ty）。</summary>
    private static VectorIota BlockCenter(int tx, int ty) => new(tx + 0.5, Main.maxTilesY - ty - 0.5, 0);

    /// <summary>世界像素换成法术坐标。</summary>
    private static (double X, double Y) SpellPoint(Vector2 px) => (px.X / 16.0, Main.maxTilesY - px.Y / 16.0);

    private static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

    private static string Vec(VectorIota? v) => v == null ? "无" : $"({v.X:0.####}, {v.Y:0.####}, {v.Z:0.####})";

    private static IEnumerable<int> Spells(ClientTestRun run)
    {
        var p = Main.LocalPlayer;
        int floor = SpellFloor;
        int left = _arena.Left;

        // 背包：法杖在快捷栏第 1 格（拿在手上），右边一格放 10 块土（放置方块从这里拿），媒质是背包里的紫水晶粉
        for (int i = 0; i < p.inventory.Length; i++) p.inventory[i].TurnToAir();
        p.inventory[0].SetDefaults(ModContent.ItemType<OakStaff>());
        p.inventory[1].SetDefaults(ItemID.DirtBlock);
        p.inventory[1].stack = 10;
        p.inventory[49].SetDefaults(ModContent.ItemType<AmethystDust>());
        p.inventory[49].stack = 999;
        p.selectedItem = 0;

        // 射线和挖掘的靶子：施法区里一根 6 格高的石柱
        int colX = left + 12;
        for (int y = floor - 6; y < floor; y++) SetSolid(colX, y, TileID.Stone);
        WorldGen.RangeFrame(colX - 1, floor - 7, colX + 1, floor);
        yield return 5;

        double h = Main.maxTilesY;
        int dustBefore = p.inventory[49].stack;

        // 施法者本人
        HexVmState.Reset();
        Cast(run, "get_caster");
        run.Check("施法者 = 玩家本人", Top is EntityIota { Target: EntityIota.EntityKind.Player } e0 && e0.Index == p.whoAmI, Top?.ToString() ?? "空栈");

        // 脚底位置：原版 position() = 脚底中心；法术坐标 y 朝上
        Cast(run, "entity_pos/foot");
        var foot = Top as VectorIota;
        var (fx, fy) = SpellPoint(p.Bottom);
        run.Check("脚底位置", foot != null && Near(foot.X, fx, 1e-3) && Near(foot.Y, fy, 1e-3) && foot.Z == 0,
            $"得到 {Vec(foot)}，期望 ({fx:0.####}, {fy:0.####}, 0)");

        // 视线：鼠标在玩家正右方，原版 lookAngle 是单位向量
        HexVmState.Reset();
        Cast(run, "get_caster");
        Cast(run, "get_entity_look");
        var look = Top as VectorIota;
        run.Check("视线朝右", look != null && Near(look.X, 1, 1e-3) && Near(look.Y, 0, 1e-2), Vec(look));

        // 射线：从眼睛朝 +x 打，打中石柱上和眼睛同一行的那格，原版给方块中心（z 按 A9 取 0）
        HexVmState.Reset();
        Cast(run, "get_caster");
        Cast(run, "entity_pos/eye");
        var eye = Top as VectorIota;
        Push(new VectorIota(1, 0, 0));
        Cast(run, "raycast");
        int eyeRow = eye == null ? 0 : (int)Math.Floor(h - eye.Y);
        var hitExpected = BlockCenter(colX, eyeRow);
        var hit = Top as VectorIota;
        run.Check("射线打中石柱", hit != null && Near(hit.X, hitExpected.X, 1e-6) && Near(hit.Y, hitExpected.Y, 1e-6) && hit.Z == 0,
            $"得到 {Vec(hit)}，期望 {Vec(hitExpected)}（眼睛 {Vec(eye)}）");

        // 破坏方块：那一格没了，掉出石块
        HexVmState.Reset();
        var before = ActiveItems();
        Push(BlockCenter(colX, eyeRow));
        Cast(run, "break_block");
        run.Check("破坏方块：方块没了", !Main.tile[colX, eyeRow].HasTile);
        run.Check("破坏方块：掉出石块", NewItems(before).Any(i => Main.item[i].type == ItemID.StoneBlock), Describe(NewItems(before)));

        // 放置、造水、召唤的靶子放在离玩家远一点的地方（20 格开外，还在施法范围里）：闪现会落在玩家右边 2 格，那里要空着
        // 放置方块：从法杖右边一格拿土，放在空地上
        HexVmState.Reset();
        int px = left + 22;
        Push(BlockCenter(px, floor - 1));
        Cast(run, "place_block");
        run.Check("放置方块：放下了土", Main.tile[px, floor - 1].HasTile && Main.tile[px, floor - 1].TileType == TileID.Dirt);
        run.Check("放置方块：法杖右边一格少了一块土", p.inventory[1].type == ItemID.DirtBlock && p.inventory[1].stack == 9, $"剩 {p.inventory[1].stack}");

        // 造水
        HexVmState.Reset();
        int wx = left + 24;
        Push(BlockCenter(wx, floor - 1));
        Cast(run, "create_water");
        run.Check("造水：那一格有水", Main.tile[wx, floor - 1].LiquidAmount > 0 && Main.tile[wx, floor - 1].LiquidType == LiquidID.Water,
            $"液体 {Main.tile[wx, floor - 1].LiquidAmount}");

        // 召唤方块
        HexVmState.Reset();
        int cx = left + 26;
        Push(BlockCenter(cx, floor - 5));
        Cast(run, "conjure_block");
        run.Check("召唤方块：出现了召唤方块", Main.tile[cx, floor - 5].HasTile && Main.tile[cx, floor - 5].TileType == ModContent.TileType<ConjuredBlock>());

        // 推动掉落物：原版 add_motion 对任何实体都生效（ItemEntity 也一样），速度单位方块 / 刻
        int gel = Item.NewItem(new EntitySource_Misc("HexClientTest"), (left + 30) * 16, (floor - 2) * 16, 16, 16, ItemID.Gel);
        Main.item[gel].noGrabDelay = 600;
        var v0 = Main.item[gel].velocity;
        HexVmState.Reset();
        Push(new EntityIota(EntityIota.EntityKind.Item, gel));
        Push(new VectorIota(0, 1, 0));
        Cast(run, "add_motion");
        var dv = Main.item[gel].velocity - v0;
        run.Check("推动掉落物：向上加了 1 方块/刻", Near(dv.X, 0, 1e-3) && Near(dv.Y, -McMotionToPixels, 1e-3), $"速度变化 ({dv.X:0.###}, {dv.Y:0.###}) 像素/帧");
        Main.item[gel].active = false;

        // 闪现：沿视线走 2 格（原版 OpBlink：位移 = lookAngle × 距离）
        HexVmState.Reset();
        Cast(run, "get_caster");
        Push(new DoubleIota(2));
        var c0 = p.Center;
        Cast(run, "blink");
        var moved = p.Center - c0;
        run.Check("闪现：沿视线右移 2 格", Near(moved.X, 32, 0.6) && Near(moved.Y, 0, 0.6), $"移动了 ({moved.X:0.##}, {moved.Y:0.##}) 像素");

        // 推自己
        HexVmState.Reset();
        Cast(run, "get_caster");
        Push(new VectorIota(1, 0, 0));
        var pv0 = p.velocity;
        Cast(run, "add_motion");
        var pdv = p.velocity - pv0;
        run.Check("推自己：向右加了 1 方块/刻", Near(pdv.X, McMotionToPixels, 1e-3) && Near(pdv.Y, 0, 1e-3), $"速度变化 ({pdv.X:0.###}, {pdv.Y:0.###}) 像素/帧");

        run.Check("施法扣了背包里的紫水晶粉", p.inventory[49].stack < dustBefore, $"{dustBefore} → {p.inventory[49].stack}");
        HexVmState.Reset();

        yield return 30;
        Main.hideUI = true;
        ClientTestSystem.Camera = new Vector2((left + 20) * 16f, (floor - 4) * 16f);
        yield return 20;
        foreach (int w in Shot("spells")) yield return w;
        ClientTestSystem.Camera = null;
        Main.hideUI = false;

        // 放回施法区左边
        p.position = new Vector2((left + 4) * 16, floor * 16 - p.height);
        p.velocity = Vector2.Zero;
        yield return 10;
    }

    private static HashSet<int> ActiveItems()
    {
        var set = new HashSet<int>();
        for (int i = 0; i < Main.maxItems; i++)
        {
            if (Main.item[i].active) set.Add(i);
        }
        return set;
    }

    private static List<int> NewItems(HashSet<int> before)
    {
        var list = new List<int>();
        for (int i = 0; i < Main.maxItems; i++)
        {
            if (Main.item[i].active && !before.Contains(i)) list.Add(i);
        }
        return list;
    }

    private static string Describe(IEnumerable<int> items)
    {
        var parts = items.Select(i => $"{Main.item[i].Name} x{Main.item[i].stack}").ToList();
        return parts.Count == 0 ? "什么都没掉" : string.Join("、", parts);
    }

    // ── 网格大小 ──────────────────────────────────────────────────────

    private static IEnumerable<int> GridZoom(ClientTestRun run)
    {
        var p = Main.LocalPlayer;
        int enlarge = ModContent.BuffType<EnlargeGrid>();
        int shrink = ModContent.BuffType<ShrinkGrid>();
        int lens = ModContent.ItemType<ScryingLens>();
        p.selectedItem = 0;
        p.inventory[1].TurnToAir();

        // 原版 GRID_ZOOM 属性：基础 1.0；探知透镜 MULTIPLY_BASE +0.33（基础那份加 0.33 倍）；明晰 / 蒙翳 MULTIPLY_TOTAL ×1.25 / ×0.8
        void Buffs(bool e, bool s)
        {
            p.ClearBuff(enlarge);
            p.ClearBuff(shrink);
            if (e) p.AddBuff(enlarge, 600);
            if (s) p.AddBuff(shrink, 600);
        }
        void Expect(string what, double expected)
        {
            double got = HexGridZoom.Of(p);
            run.Check($"网格大小：{what}", Near(got, expected, 1e-4), $"得到 {got:0.####}，期望 {expected:0.####}");
        }

        Buffs(false, false);
        Expect("什么都没有", 1.0);
        Buffs(true, false);
        Expect("明晰", 1.25);
        Buffs(false, true);
        Expect("蒙翳", 0.8);
        Buffs(true, true);
        Expect("明晰 + 蒙翳", 1.0);
        Buffs(false, false);
        p.inventory[1].SetDefaults(lens);
        Expect("探知透镜（法杖右边一格）", 1.33);
        Buffs(true, false);
        Expect("探知透镜 + 明晰", 1.33 * 1.25);
        Buffs(false, false);
        p.inventory[1].TurnToAir();
        yield return 2;

        // 画布：打开时按当前药效算格距，开着的时候药效变了也跟着变（原版每帧算 hexSize）
        // 原版只在鼠标周围 3 格画网格点：鼠标挪到玩家左下方的空地上（右上角是小地图），小地图也先收起来
        float cfg = HexClientConfig.Instance.GridZoom;
        int mapStyle = Main.mapStyle;
        Main.mapStyle = 0;
        ClientTestSystem.MouseOffset = new Vector2(-220, 40);
        yield return 3;
        HexStaff.OpenCanvas();
        yield return 5;
        var canvas = HexCanvasState.Canvas;
        run.Check("法杖打开画布", canvas.IsOpen);
        run.Check("画布网格：没有药效", Near(canvas.Zoom, cfg, 1e-4), $"缩放 {canvas.Zoom:0.####}，设置 {cfg}");
        foreach (int w in Shot("canvas-normal")) yield return w;

        Buffs(true, false);
        yield return 3;
        run.Check("画布网格：喝了明晰，开着的画布跟着变", Near(canvas.Zoom, cfg * 1.25, 1e-4), $"缩放 {canvas.Zoom:0.####}");
        foreach (int w in Shot("canvas-enlarge")) yield return w;

        Buffs(false, true);
        yield return 3;
        run.Check("画布网格：喝了蒙翳", Near(canvas.Zoom, cfg * 0.8, 1e-4), $"缩放 {canvas.Zoom:0.####}");
        foreach (int w in Shot("canvas-shrink")) yield return w;

        Buffs(false, false);
        HexCanvasState.CloseCanvas();
        yield return 3;
        run.Check("画布关上了", !canvas.IsOpen);
        Main.mapStyle = mapStyle;
        ClientTestSystem.MouseOffset = new Vector2(400, 0);
    }

    // ── 存档（setup 写，verify 读回来比） ────────────────────────────────

    /// <summary>存进核心的数据：各种 iota 都来一个。</summary>
    private static Iota PersistIota() => new ListIota(
        new DoubleIota(42),
        new VectorIota(1.5, -2, 3),
        new PatternIota(Pat("get_caster")),
        BooleanIota.True,
        NullIota.Instance,
        new ListIota(new DoubleIota(-0.25)));

    private static HexPattern PersistPattern() => Pat("add");

    private static Iota ShelfIota() => new VectorIota(7, 8, 9);

    /// <summary>镜头放大 2 倍、对准一格方块的中心拍一张（界面藏起来）。这一格在截图正中，32 像素见方。</summary>
    private static IEnumerable<int> ZoomedShot(int tx, int ty, string name)
    {
        Main.hideUI = true;
        float zoom = Main.GameZoomTarget;
        Main.GameZoomTarget = 2f;
        ClientTestSystem.Camera = new Vector2(tx * 16 + 8, ty * 16 + 8);
        yield return 30;
        foreach (int w in Shot(name)) yield return w;
        ClientTestSystem.Camera = null;
        Main.GameZoomTarget = zoom;
        Main.hideUI = false;
    }

    /// <summary>
    /// 两张截图正中 2×half 像素见方里明显变了的像素数。默认 28 像素：放大 2 倍时正好在那一格方块里面，背景动不到；
    /// 3×3 的挂板放大后 96 像素见方，取中间 80。
    /// </summary>
    private static int ChangedAroundCenter(Color[]? a, Color[]? b, int half = 14)
    {
        if (a == null || b == null || a.Length != b.Length) return -1;
        int w = ClientTestSystem.LastShotWidth, h = a.Length / w;
        int n = 0;
        for (int y = h / 2 - half; y < h / 2 + half; y++)
        {
            for (int x = w / 2 - half; x < w / 2 + half; x++)
            {
                Color p = a[y * w + x], q = b[y * w + x];
                if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 60) n++;
            }
        }
        return n;
    }

    private static string StatePath(ClientTestRun run) => Path.Combine(run.OutDir, "state.json");

    private static IEnumerable<int> SaveState(ClientTestRun run)
    {
        var p = Main.LocalPlayer;

        // 核心放在背包第 3 格，写进一个列表
        var focus = p.inventory[_arena.FocusSlot];
        focus.SetDefaults(ModContent.ItemType<Focus>());
        bool wrote = focus.ModItem is ItemIotaStorage storage && storage.WriteIota(PersistIota(), simulate: false);
        run.Check("核心写进了数据", wrote);

        // 石板刻上图案、朝右；刻之前、刻之后各拍一张放大的，石板那一格的像素要变（图案真的画出来了）
        var slate = _arena.Placements.FirstOrDefault(pl => pl.Tile == nameof(HexSlate) && pl.Placed);
        var te = slate == null ? null : HexSlateEntity.FindAt(slate.X, slate.Y);
        run.Check("找到场地上的石板", te != null);
        if (te != null)
        {
            _arena.SlateX = slate!.X;
            _arena.SlateY = slate.Y;
            te.Pattern = null;
            foreach (int w in ZoomedShot(slate.X, slate.Y, "slate-blank")) yield return w;
            var blank = ClientTestSystem.LastShotPixels;
            te.Pattern = PersistPattern();
            te.SetNormal(CircleDir.Right);
            foreach (int w in ZoomedShot(slate.X, slate.Y, "slate")) yield return w;
            int changed = ChangedAroundCenter(blank, ClientTestSystem.LastShotPixels);
            run.Check("石板上画出了刻的图案", changed >= 20, $"石板那一格变了 {changed} 个像素");
        }

        // 阿卡夏书架：存一条（键图案 + 数据），键图案画在书架正面
        var shelf = _arena.Placements.FirstOrDefault(pl => pl.Tile == nameof(AkashicBookshelf) && pl.Placed);
        run.Check("找到场地上的阿卡夏书架", shelf != null);
        if (shelf != null)
        {
            _arena.ShelfX = shelf.X;
            _arena.ShelfY = shelf.Y;
            foreach (int w in ZoomedShot(shelf.X, shelf.Y, "shelf-blank")) yield return w;
            var blank = ClientTestSystem.LastShotPixels;
            AkashicBookshelfEntity.SetMapping(shelf.X, shelf.Y, PersistPattern(), ShelfIota());
            run.Check("书架存进了一条", AkashicBookshelfEntity.FindAt(shelf.X, shelf.Y)?.Pattern != null);
            foreach (int w in ZoomedShot(shelf.X, shelf.Y, "shelf")) yield return w;
            int changed = ChangedAroundCenter(blank, ClientTestSystem.LastShotPixels);
            run.Check("书架上画出了键图案", changed >= 20, $"书架那一格变了 {changed} 个像素");
        }

        // 大型挂轴框（3×3，原版 blockSize 3）挂上图案：图案画满整张卷轴
        var board = _arena.Placements.FirstOrDefault(pl => pl.Tile == nameof(WallScrollLarge) && pl.Placed);
        var scrollEntity = board == null ? null : WallScrollEntity.FindAt(board.X, board.Y);
        run.Check("场地上的大型挂轴框有图格实体", scrollEntity != null);
        if (board != null && scrollEntity != null)
        {
            _arena.ScrollX = board.X;
            _arena.ScrollY = board.Y;
            foreach (int w in ZoomedShot(board.X + 1, board.Y + 1, "scroll-blank")) yield return w;
            var blank = ClientTestSystem.LastShotPixels;
            scrollEntity.Pattern = PersistPattern();
            foreach (int w in ZoomedShot(board.X + 1, board.Y + 1, "scroll")) yield return w;
            int changed = ChangedAroundCenter(blank, ClientTestSystem.LastShotPixels, 40);
            run.Check("挂轴框上画出了挂着的图案", changed >= 20, $"挂板中间变了 {changed} 个像素");
        }

        // 旧尺寸的小挂轴框：2026-10-02 之前小 / 中 / 大是 2×2 / 3×3 / 4×4 格。照旧的样子铺一块 2×2、挂上图案，
        // 下一段读档时应该被换成 1×1（TileEntityRepair.ShrinkOldWallScrolls），图案不丢
        int ox = _arena.Left + 50, oy = SpellFloor - 5;
        for (int dx = 0; dx < 2; dx++)
        {
            for (int dy = 0; dy < 2; dy++)
            {
                var t = Main.tile[ox + dx, oy + dy];
                t.ClearTile();
                t.HasTile = true;
                t.TileType = (ushort)ModContent.TileType<WallScrollSmall>();
                t.TileFrameX = (short)(dx * 18);
                t.TileFrameY = (short)(dy * 18);
            }
        }
        int oldId = ModContent.GetInstance<WallScrollEntity>().Place(ox, oy);
        if (TileEntity.ByID.TryGetValue(oldId, out var oldTe) && oldTe is WallScrollEntity oldScroll) oldScroll.Pattern = PersistPattern();
        run.Check("铺了一块旧尺寸的小挂轴框", WallScrollEntity.FindAt(ox, oy)?.Pattern != null);
        _arena.OldScrollX = ox;
        _arena.OldScrollY = oy;

        File.WriteAllText(StatePath(run), JsonSerializer.Serialize(_arena, new JsonSerializerOptions { WriteIndented = true }));

        var started = DateTime.Now.AddSeconds(-1);
        WorldFile.SaveWorld();
        Player.SavePlayer(Main.ActivePlayerFileData);
        string world = Main.ActiveWorldFileData.Path;
        string player = Main.ActivePlayerFileData.Path;
        run.Check("世界存档写好了", File.Exists(world) && File.GetLastWriteTime(world) >= started, world);
        run.Check("模组的世界数据写好了", File.Exists(Path.ChangeExtension(world, ".twld")) && File.GetLastWriteTime(Path.ChangeExtension(world, ".twld")) >= started);
        run.Check("角色存档写好了", File.Exists(player) && File.GetLastWriteTime(player) >= started, player);
        yield break;
    }

    private static IEnumerable<int> VerifySaved(ClientTestRun run)
    {
        var path = StatePath(run);
        if (!run.Check("有上一段留下的场地记录", File.Exists(path), path)) yield break;
        _arena = JsonSerializer.Deserialize<ArenaState>(File.ReadAllText(path)) ?? new ArenaState();
        var p = Main.LocalPlayer;

        // 场地上放的东西都还在，图格实体也在
        var lost = new List<string>();
        var lostEntity = new List<string>();
        foreach (var pl in _arena.Placements.Where(pl => pl.Placed))
        {
            int type = TileType(pl.Tile);
            if (!Main.tile[pl.X, pl.Y].HasTile || Main.tile[pl.X, pl.Y].TileType != type) lost.Add(pl.ItemName);
            if (pl.ExpectEntity && !(TileEntity.ByPosition.TryGetValue(new Point16(pl.X, pl.Y), out var e) && e is ModTileEntity { } m && m.Mod == ThisMod))
            {
                lostEntity.Add(pl.ItemName);
            }
        }
        run.Check($"读档后方块都在（{_arena.Placements.Count(pl => pl.Placed)} 件）", lost.Count == 0, string.Join("、", lost));
        run.Check("读档后图格实体都在", lostEntity.Count == 0, string.Join("、", lostEntity));
        var lostWalls = _arena.Walls.Where(w => Main.tile[w.X, w.Y].WallType != WallTypeOf(w.Wall)).Select(w => w.ItemName).ToList();
        run.Check("读档后墙都在", lostWalls.Count == 0, string.Join("、", lostWalls));

        // 核心里的数据
        var stored = (p.inventory[_arena.FocusSlot].ModItem as ItemIotaStorage)?.Read();
        run.Check("读档后核心里的数据一样", stored != null && stored.ValueEquals(PersistIota()), stored?.ToString() ?? "核心是空的或者不见了");

        // 石板上的图案和朝向
        var te = HexSlateEntity.FindAt(_arena.SlateX, _arena.SlateY);
        var expected = PersistPattern();
        run.Check("读档后石板上的图案一样", te?.Pattern != null && te.Pattern.SigsEqual(expected) && te.Pattern.StartDir == expected.StartDir,
            te?.Pattern?.ToString() ?? "石板没了或者是空的");
        run.Check("读档后石板朝向一样", te?.Normal == CircleDir.Right, te?.Normal.ToString() ?? "");

        // 阿卡夏书架上存的那一条
        var shelf = AkashicBookshelfEntity.FindAt(_arena.ShelfX, _arena.ShelfY);
        run.Check("读档后书架上的键图案一样", shelf?.Pattern != null && shelf.Pattern.SigsEqual(expected) && shelf.Pattern.StartDir == expected.StartDir,
            shelf?.Pattern?.ToString() ?? "书架上没有东西");
        run.Check("读档后书架上的数据一样", shelf?.Datum != null && shelf.Datum.ValueEquals(ShelfIota()), shelf?.Datum?.ToString() ?? "");

        // 挂轴框上挂着的图案
        var scroll = WallScrollEntity.FindAt(_arena.ScrollX, _arena.ScrollY);
        run.Check("读档后挂轴框上的图案一样", scroll?.Pattern != null && scroll.Pattern.SigsEqual(expected), scroll?.Pattern?.ToString() ?? "挂板上没有图案");

        // 旧尺寸的小挂轴框换成了 1×1：左上角那格是新的帧，多出来的三格拆掉了，图案还在
        int ox = _arena.OldScrollX, oy = _arena.OldScrollY, small = ModContent.TileType<WallScrollSmall>();
        bool shrunk = Main.tile[ox, oy].HasTile && Main.tile[ox, oy].TileType == small
                      && Main.tile[ox, oy].TileFrameX == 0 && Main.tile[ox, oy].TileFrameY == 0
                      && !(Main.tile[ox + 1, oy].HasTile && Main.tile[ox + 1, oy].TileType == small)
                      && !(Main.tile[ox, oy + 1].HasTile && Main.tile[ox, oy + 1].TileType == small)
                      && !(Main.tile[ox + 1, oy + 1].HasTile && Main.tile[ox + 1, oy + 1].TileType == small);
        run.Check("读档后旧尺寸的小挂轴框换成了 1×1", shrunk);
        var old = WallScrollEntity.FindAt(ox, oy);
        run.Check("换尺寸以后挂着的图案还在", old?.Pattern != null && old.Pattern.SigsEqual(expected), old?.Pattern?.ToString() ?? "");
        if (old != null)
        {
            _arena.Placements.Add(new Placement
            {
                Item = nameof(WallScrollFrameSmall), ItemName = "旧尺寸换过来的小型挂轴框", Tile = nameof(WallScrollSmall),
                Placed = true, X = ox, Y = oy, Width = 1, Height = 1,
            });
        }

        foreach (int w in ArenaShots("arena-reloaded")) yield return w;
    }

    // ── 挖方块（verify 段最后做，场地挖完就没了） ───────────────────────

    private static IEnumerable<int> MineArena(ClientTestRun run)
    {
        foreach (var pl in _arena.Placements.Where(pl => pl.Placed))
        {
            int tileType = TileType(pl.Tile);
            var modTile = TileLoader.GetTile(tileType);
            int itemType = ThisMod.TryFind<ModItem>(pl.Item, out var mi) ? mi.Type : -1;
            var before = ActiveItems();
            WorldGen.KillTile(pl.X, pl.Y);
            var drops = NewItems(before);
            bool gone = !Main.tile[pl.X, pl.Y].HasTile || Main.tile[pl.X, pl.Y].TileType != tileType;
            run.Check($"挖掉：{pl.ItemName}", gone);

            if (pl.Tile == nameof(QuenchedAllay))
            {
                CheckQuenchedShards(run, "淬灵晶块，没人挖", drops);
            }
            else if (pl.X == _arena.ScrollX && pl.Y == _arena.ScrollY || pl.X == _arena.OldScrollX && pl.Y == _arena.OldScrollY)
            {
                // 原版 EntityWallScroll.dropItem：挂着的卷轴带着图案掉出来；挂轴框（移植版的两段式才有）照常掉
                int scrollType = pl.Tile == nameof(WallScrollLarge) ? ModContent.ItemType<ScrollLarge>() : ModContent.ItemType<ScrollSmall>();
                bool frame = drops.Any(i => Main.item[i].type == itemType);
                bool scroll = drops.Any(i => Main.item[i].type == scrollType && Main.item[i].ModItem is ItemIotaStorage st
                                             && st.Read() is PatternIota pi && pi.Pattern.SigsEqual(PersistPattern()));
                run.Check($"挖掉挂着图案的{pl.ItemName}：掉出挂轴框和带着图案的卷轴", frame && scroll, Describe(drops));
            }
            else if (pl.Tile == nameof(HexSlate))
            {
                // 原版：刻着图案的石板掉「有图案的石板」，图案跟着物品走
                var slateItem = drops.Select(i => Main.item[i].ModItem).OfType<HexSlateItem>().FirstOrDefault();
                run.Check("挖石板：掉出的石板带着图案", slateItem?.Pattern != null && slateItem.Pattern.SigsEqual(PersistPattern()), Describe(drops));
            }
            else if (modTile != null && !HasCustomDrops(modTile))
            {
                run.Check($"挖掉以后掉出自己：{pl.ItemName}", drops.Any(i => Main.item[i].type == itemType), Describe(drops));
            }
            else
            {
                run.Info("drops:" + pl.Item, Describe(drops));
            }
            foreach (int i in drops) Main.item[i].active = false;
            if (pl == _arena.Placements[^1] || drops.Count > 0) yield return 0;
        }

        foreach (var wall in _arena.Walls)
        {
            int itemType = ThisMod.TryFind<ModItem>(wall.Item, out var mi) ? mi.Type : -1;
            var before = ActiveItems();
            WorldGen.KillWall(wall.X, wall.Y);
            var drops = NewItems(before);
            run.Check($"挖掉墙：{wall.ItemName}", Main.tile[wall.X, wall.Y].WallType == WallID.None);
            run.Check($"挖掉墙以后掉出自己：{wall.ItemName}", drops.Any(i => Main.item[i].type == itemType), Describe(drops));
            foreach (int i in drops) Main.item[i].active = false;
        }
        yield return 5;

        // 玩家拿镐挖淬灵晶块：普通的掉碎片；喝了共振药水掉方块本身（原版的精准采集）。
        // 「谁在挖」照 TileMiner：本人瞄着这一格、正在挥镐
        var p = Main.LocalPlayer;
        p.inventory[0].SetDefaults(ItemID.CopperPickaxe);
        p.selectedItem = 0;
        int qx = _arena.Left + 40, qy = SpellFloor - 1;
        foreach (bool resonant in new[] { false, true })
        {
            p.ClearBuff(ModContent.BuffType<Resonance>());
            if (resonant) p.AddBuff(ModContent.BuffType<Resonance>(), 600);
            WorldGen.PlaceTile(qx, qy, ModContent.TileType<QuenchedAllay>(), mute: true, forced: true);
            run.Check("又放了一块淬灵晶块", Main.tile[qx, qy].HasTile && Main.tile[qx, qy].TileType == ModContent.TileType<QuenchedAllay>());
            Player.tileTargetX = qx;
            Player.tileTargetY = qy;
            p.itemAnimation = 10;
            var before = ActiveItems();
            WorldGen.KillTile(qx, qy);
            var drops = NewItems(before);
            if (resonant)
            {
                bool block = drops.Count == 1 && Main.item[drops[0]].type == ModContent.ItemType<QuenchedAllayItem>() && Main.item[drops[0]].stack == 1;
                run.Check("喝了共振药水拿镐挖淬灵晶块：掉方块本身", block, Describe(drops));
            }
            else
            {
                CheckQuenchedShards(run, "淬灵晶块，拿铜镐挖", drops);
            }
            foreach (int i in drops) Main.item[i].active = false;
            p.itemAnimation = 0;
            yield return 2;
        }
        p.ClearBuff(ModContent.BuffType<Resonance>());
    }

    /// <summary>原版掉落表 quenched_allay.json：2~4 片碎片，时运按 0.25 / 0.5 / 0.75 / 1.0 的概率再多 1 片，所以是 2~5 片。</summary>
    private static void CheckQuenchedShards(ClientTestRun run, string what, List<int> drops)
    {
        int shard = ModContent.ItemType<QuenchedAllayShard>();
        int count = drops.Where(i => Main.item[i].type == shard).Sum(i => Main.item[i].stack);
        bool onlyShards = drops.All(i => Main.item[i].type == shard);
        run.Check($"{what}：掉 2~5 片淬灵晶碎片", onlyShards && count is >= 2 and <= 5, Describe(drops));
    }

    /// <summary>方块自己改了掉落（覆写了掉落相关的钩子）就不按「掉出放它的物品」来查，掉了什么记进报告。</summary>
    private static bool HasCustomDrops(ModTile tile)
    {
        foreach (var m in tile.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.Name is "GetItemDrops" or "CanDrop" or "Drop" or "KillMultiTile" && m.DeclaringType != null
                && m.DeclaringType.IsSubclassOf(typeof(ModTile)))
            {
                return true;
            }
        }
        return false;
    }
}
