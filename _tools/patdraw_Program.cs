using HexCastingTerraria.Core.Casting.Math;
using HexCastingTerraria.Core.Registry;

// 把一条图案画成 ASCII 六边形网格图，供人照着画。
//
// 为什么需要：图案是**二维图形**，用文字描述（"起始东北，角度序列 wqaqwd"）
// 对人来说不可读；而打开游戏翻书去比对又太慢。
// 这里直接把路径铺在字符网格上：方向箭头标出起点与走法。
//
// 用法：
//   dotnet run -- project <签名> [起始方向]
//   dotnet run -- list <图案id片段>          # 按 id 查签名
//   dotnet run -- spell <示例名>             # 打印一条完整法术的所有图案

var patterns = GeneratedPatternData.All;

if (args.Length == 0)
{
    Console.WriteLine("用法： patgen <签名> [起始方向]   |  patgen find <id片段>  |  patgen spell <名字>");
    return;
}

// ── find：按 id 找签名 ──
if (args[0] == "find")
{
    string needle = args.Length > 1 ? args[1] : "";
    foreach (var p in patterns.Where(p => p.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine($"{p.Id,-40} 签名 {p.Angles,-24} 起始 {p.StartDir}");
    }
    return;
}

// ── spell：打印一条完整法术 ──
if (args[0] == "spell")
{
    string name = args.Length > 1 ? args[1] : "blink";
    var (title, steps) = Spells(name);
    Console.WriteLine($"===== {title} =====");
    Console.WriteLine();
    int n = 1;
    foreach (var id in steps)
    {
        var def = patterns.FirstOrDefault(p => p.Id == id);
        if (def.Id == null)
        {
            Console.WriteLine($"[{n}] {id}  —— **没有这条图案，id 写错了**");
            n++;
            continue;
        }

        Console.WriteLine($"[{n}] {def.Id}   起始方向：{def.StartDir}   签名：{def.Angles}");
        Draw(def.Angles, def.StartDir);
        Console.WriteLine();
        n++;
    }
    return;
}

// ── 直接给签名 ──
{
    string sig = args[0];
    HexDir start = args.Length > 1 ? Enum.Parse<HexDir>(args[1], true) : HexDir.East;
    Draw(sig, start);
}

// ─────────────────────────────────────────────────────────────────────

// 六边形网格的 6 个方向：用「双宽字符画」表示（x 方向 ×2，这样六个方向看起来才均匀）
static (int X, int Y) Step(HexDir dir) => dir switch
{
    HexDir.East => (2, 0),
    HexDir.SouthEast => (1, 1),
    HexDir.SouthWest => (-1, 1),
    HexDir.West => (-2, 0),
    HexDir.NorthWest => (-1, -1),
    HexDir.NorthEast => (1, -1),
    _ => (0, 0),
};

// 六边形网格画成字符图。
//
// 注意：每个「格子行」占**两行字符**：斜向的边（六个方向里有四个是斜的）
// 才能画成真正的斜线。只占一行的话，斜边会被压成水平线，
// 画出来的形状和游戏里完全不是一回事 —— 那还不如不画。
static void Draw(string signature, HexDir startDir)
{
    var (pts, _) = Walk(signature, startDir);

    // 格点 -> 字符坐标：x 直接放大 2 倍（六边形横向间距更大），y 放大 2 倍留出斜线行
    var screen = pts.Select(p => (X: p.X, Y: p.Y * 2)).ToList();

    int minX = screen.Min(p => p.X), maxX = screen.Max(p => p.X);
    int minY = screen.Min(p => p.Y), maxY = screen.Max(p => p.Y);

    const int pad = 2;
    int w = maxX - minX + pad * 2 + 3;
    int h = maxY - minY + pad * 2 + 1;

    var grid = new char[h, w];
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            grid[y, x] = ' ';

    // 连线：按方向决定用哪种笔画
    for (int i = 0; i < screen.Count - 1; i++)
    {
        var a = screen[i];
        var b = screen[i + 1];
        int dx = b.X - a.X;
        int dy = b.Y - a.Y;

        char stroke = dy == 0 ? '-' : (dx > 0 && dy < 0) ? '/' : (dx < 0 && dy > 0) ? '/' : '\\';

        if (Math.Abs(dy) == 2 && Math.Abs(dx) == 1)
        {
            // 斜边：在中点那一行画一个斜线字符
            int mx = a.X + dx - minX + pad;
            int my = a.Y + dy / 2 - minY + pad;
            if (grid[my, mx] == ' ') grid[my, mx] = stroke;
        }
        else if (dy == 0)
        {
            int my = a.Y - minY + pad;
            int mx = a.X + dx / 2 - minX + pad;
            if (grid[my, mx] == ' ') grid[my, mx] = '-';
        }
    }

    // 格点
    for (int i = 0; i < screen.Count; i++)
    {
        int x = screen[i].X - minX + pad;
        int y = screen[i].Y - minY + pad;
        grid[y, x] = i == 0 ? 'S' : (i == screen.Count - 1 ? 'E' : 'o');
    }

    Console.WriteLine($"   （起始方向 {startDir}：S=起笔，E=收笔，o=经过的格点）");
    for (int y = 0; y < h; y++)
    {
        var sb = new System.Text.StringBuilder("   ");
        for (int x = 0; x < w; x++) sb.Append(grid[y, x]);
        Console.WriteLine(sb.ToString().TrimEnd());
    }
}

static (List<(int X, int Y)> Points, List<HexDir> Dirs) Walk(string signature, HexDir startDir)
{
    var pts = new List<(int X, int Y)> { (0, 0) };
    var dirs = new List<HexDir>();

    var current = startDir;
    var pos = (X: 0, Y: 0);

    foreach (char c in signature)
    {
        var angle = HexAngleExtensions.FromChar(c) ?? throw new ArgumentException($"非法角度字符 {c}");
        current = current.RotatedBy(angle);
        var d = Step(current);
        pos = (pos.X + d.X, pos.Y + d.Y);
        pts.Add(pos);
        dirs.Add(current);
    }

    return (pts, dirs);
}

static (string Title, string[] Steps) Spells(string name) => name switch
{
    "jump" => ("自我推进（向上跳很高）", new[]
    {
        "hexcasting:get_caster",
        // 注意：泰拉的 +Y 是**向下**（与 MC 相反），所以「向上」要用 ny
        "hexcasting:const/vec/ny",
        "hexcasting:add_motion",
    }),
    "blink" => ("闪现（朝鼠标方向瞬移 3 格）", new[]
    {
        "hexcasting:get_caster",
        "hexcasting:const/double/3",
        "hexcasting:blink",
    }),
    "mine" => ("隔空挖方块（挖你看着的那一格）", new[]
    {
        "hexcasting:get_caster",
        "hexcasting:entity_pos/eye",
        "hexcasting:get_entity_look",
        "hexcasting:raycast",
        "hexcasting:break_block",
    }),
    "water" => ("凭空造水（鼠标指的位置）", new[]
    {
        "hexcasting:get_caster",
        "hexcasting:entity_pos/eye",
        "hexcasting:get_entity_look",
        "hexcasting:raycast",
        "hexcasting:create_water",
    }),
    "hello" => ("打印圆周率", new[]
    {
        "hexcasting:const/double/pi",
        "hexcasting:print",
    }),
    "here" => ("报出自己眼睛的坐标", new[]
    {
        "hexcasting:get_caster",
        "hexcasting:entity_pos/eye",
    }),
    _ => ($"未知示例 {name}（可选：jump / blink / mine / water / hello / here）", Array.Empty<string>()),
};
