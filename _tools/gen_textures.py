"""从原版 HexMod 贴图生成模组的全部贴图（可重复运行）。

规则见 TERRARIA_RENDERING_NOTES.md：
  - 物品：原版 16 像素 → 最近邻 ×2 = 32 像素（泰拉的「2 倍像素」风格，正好卡背包 32 上限）
  - 自动选帧的实心方块：288×270 标准图集，每一帧都铺满完整的一块；原版有随机样式的按列轮换
  - 固定帧的方块：16×16 一帧，帧间隔 18（多状态横排）
  - 有状态的物品：状态图集（每格 32×32），运行时按实例状态取格（Content/Items/ItemStateArt.cs）
  - 原版没有 / MC 原生物品不在 jar 里的：保留现有贴图，只修图集格式

颜料（染色剂）的动画贴图在 _tools/gen_pigments.py。
紫水晶种植盆（移植版新增，原版没有贴图，用原版方块贴图拼）在 _tools/gen_planter_art.py。

来源：本地原版 jar（hexwork/jar/assets/hexcasting/textures，HexMod v0.11.4，MIT）。
"""
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JAR = os.path.join(os.path.dirname(ROOT), 'hexwork', 'jar', 'assets', 'hexcasting', 'textures')
MOD = os.path.join(ROOT, 'HexCastingTerraria', 'Content')

written = []


def src(name):
    im = Image.open(os.path.join(JAR, name + '.png')).convert('RGBA')
    return im.crop((0, 0, 16, 16))


def x2(im):
    return im.resize((im.width * 2, im.height * 2), Image.NEAREST)


def save(im, *path):
    p = os.path.join(MOD, *path)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    im.save(p)
    written.append(os.path.relpath(p, ROOT))


def block_sheet(faces):
    """288×270 标准方块图集：16 列 × 15 行、间隔 18；每一帧都是完整的一块，随机样式按列轮换。"""
    sheet = Image.new('RGBA', (288, 270), (0, 0, 0, 0))
    for r in range(15):
        for c in range(16):
            sheet.paste(faces[c % len(faces)], (c * 18, r * 18))
    return sheet


def frames(faces):
    """固定帧：16×16 横排，间隔 18。"""
    sheet = Image.new('RGBA', (18 * len(faces) - 2, 16), (0, 0, 0, 0))
    for i, f in enumerate(faces):
        sheet.paste(f, (i * 18, 0))
    return sheet


def grid(cells, cols):
    """状态图集：每格 32×32，按行排。"""
    rows = (len(cells) + cols - 1) // cols
    sheet = Image.new('RGBA', (cols * 32, rows * 32), (0, 0, 0, 0))
    for i, c in enumerate(cells):
        if c is not None:
            sheet.paste(c, ((i % cols) * 32, (i // cols) * 32))
    return sheet


def existing_face(*path):
    """保留的旧贴图：已经是图集就取 (0,0) 那一帧，否则整张（16×16）。"""
    im = Image.open(os.path.join(MOD, *path)).convert('RGBA')
    return im.crop((0, 0, 16, 16))


# ── 自动选帧的方块（原版贴图）──────────────────────────────────────
Q = lambda base: [src(f'{base}_{i}') for i in range(4)]
FRAMED = {
    'SlateBlock': [src('block/slate')],
    'SlateTiles': [src('block/deco/slate_tiles')],
    'SlateBricks': [src('block/deco/slate_bricks')],
    'SlateBricksSmall': [src('block/deco/slate_bricks_small')],
    'SlatePillar': [src('block/deco/slate_pillar_side')],
    'SlateAmethystTiles': [src('block/deco/slate_amethyst_tiles')],
    'SlateAmethystBricks': [src(f'block/deco/slate_amethyst_bricks_{i}') for i in range(3)],
    'SlateAmethystBricksSmall': [src(f'block/deco/slate_amethyst_bricks_small_{i}') for i in range(3)],
    'SlateAmethystPillar': [src('block/deco/slate_amethyst_pillar_side')],
    'AmethystTiles': [src('block/deco/amethyst_tiles')],
    'AmethystBricks': [src('block/deco/amethyst_bricks')],
    'AmethystBricksSmall': [src('block/deco/amethyst_bricks_small')],
    'AmethystPillar': [src('block/deco/amethyst_pillar_side')],
    'QuenchedAllay': Q('block/quenched_allay'),
    'QuenchedAllayTiles': Q('block/deco/quenched_allay_tiles'),
    'QuenchedAllayBricks': Q('block/deco/quenched_allay_bricks'),
    'QuenchedAllayBricksSmall': Q('block/deco/quenched_allay_bricks_small'),
    'EdifiedLog': [src('block/edified_log')],
    'EdifiedLogAmethyst': [src('block/deco/edified_log_amethyst')],
    'EdifiedLogAventurine': [src('block/deco/edified_log_aventurine')],
    'EdifiedLogCitrine': [src('block/deco/edified_log_citrine')],
    'EdifiedLogPurple': [src('block/deco/edified_log_purple')],
    'StrippedEdifiedLog': [src('block/stripped_edified_log')],
    'EdifiedWood': [src('block/edified_log')],              # 原版 edified_wood 六面都是树皮
    'StrippedEdifiedWood': [src('block/stripped_edified_log')],
    # 原版三种木板按权重 3 : 3 : 1 随机
    'EdifiedPlanks': [src('block/edified_planks')] * 3 + [src('block/edified_planks_2')] * 3 + [src('block/edified_planks_3')],
    'EdifiedPanel': [src('block/edified_panel')],
    'EdifiedTile': [src('block/edified_tile')],
    'AmethystEdifiedLeaves': [src('block/amethyst_edified_leaves')],
    'AventurineEdifiedLeaves': [src('block/aventurine_edified_leaves')],
    'CitrineEdifiedLeaves': [src('block/citrine_edified_leaves')],
    'AmethystDustBlock': [src('block/amethyst_dust_block')],
    'AkashicBookshelf': [src('block/akashic_bookshelf')],
    'AkashicLigature': [src('block/akashic_ligature')],
    'AkashicRecord': [src('block/akashic_record')],          # 原版是整块方块，不是摆件
}
# 自动选帧、但原版没有对应贴图（MC 原生方块 / 移植版自创）：保留旧图，改成图集
FRAMED_KEEP = ['GeodeCore', 'AmethystBudSmall', 'AmethystBudMedium', 'AmethystBudLarge', 'AmethystCluster',
               'ConjuredBlock', 'ConjuredLight']

for name, faces in FRAMED.items():
    save(block_sheet(faces), 'Tiles', name + '.png')
    save(x2(faces[0]), 'Items', 'Blocks', name + '.png')          # 物品图标：第一种样式 ×2
for name in FRAMED_KEEP:
    face = existing_face('Tiles', name + '.png')
    save(block_sheet([face]), 'Tiles', name + '.png')

# ── 固定帧的方块 ──────────────────────────────────────────────────
C = 'block/circle/'


def slate_glyph():
    """原版「已写入石板」物品图标上那几点紫色刻痕：和空白石板图标逐像素比出来的 [(x, y, 颜色)]。"""
    blank, written = src('item/slate_blank'), src('item/slate_written')
    return [(x, y, written.getpixel((x, y))) for y in range(16) for x in range(16)
            if written.getpixel((x, y)) != blank.getpixel((x, y))]


def slate_sheet():
    """
    石板图集：列 = [空白, 刻了图案]，行 = 贴法 [背景墙, 地面, 天花板, 左边方块, 右边方块]（Content/Tiles/HexSlate.cs 的 SlateAttach）。
    原版石板是 1/16 格厚的薄板，贴在一个方块面上：贴背景墙时正面朝着屏幕，画整格；贴在旁边方块上时从侧面看，
    画成 4 像素厚的薄板（原版 1 像素在泰拉里看不见，用户定加厚到 4 像素），取原版贴图边上那 4 行 / 列。
    刻了图案：整格的正中放原版「已写入石板」图标上的刻痕；薄板上在中间点两点刻痕的颜色。
    原版是把图案直接画在方块上，泰拉一格太小看不清，改成两种样子 + 鼠标悬停显示图案（用户定，2026-10-02）。
    """
    face = src('block/slate')
    glyph = slate_glyph()
    bright = [c for (_, _, c) in glyph if sum(c[:3]) > 600][:2]
    sheet = Image.new('RGBA', (34, 18 * 5 - 2), (0, 0, 0, 0))
    for row, side in enumerate(['wall', 'floor', 'ceiling', 'left', 'right']):
        for col in range(2):
            cell = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
            if side == 'wall':
                cell.paste(face, (0, 0))
                if col == 1:
                    for (x, y, c) in glyph:
                        if 0 <= y + 2 < 16:
                            cell.putpixel((x, y + 2), c)
            else:
                horizontal = side in ('floor', 'ceiling')
                strip = face.crop((0, 0, 16, 4)) if horizontal else face.crop((0, 0, 4, 16))
                at = {'floor': (0, 12), 'ceiling': (0, 0), 'left': (0, 0), 'right': (12, 0)}[side]
                cell.paste(strip, at)
                if col == 1:
                    for k, c in enumerate(bright):
                        cell.putpixel((at[0] + 7 + k, at[1] + 1 + k) if horizontal else (at[0] + 1 + k, at[1] + 7 + k), c)
            sheet.paste(cell, (col * 18, row * 18))
    return sheet


def rotated(im, facing):
    """原版的 top 面：图的上边是出口（facing）那一侧。泰拉看法术环 = 从环的上方往下看，按出口方向转。"""
    return {'up': im, 'right': im.rotate(-90), 'down': im.rotate(180), 'left': im.rotate(90)}[facing]


def circle_sheet(tops):
    """
    促动石 / 导向石图集：列 = 状态（tops 的顺序），行 = 出口方向 [上, 右, 下, 左]（只有画面里的四个方向）。
    以前用的是原版的正面（front，有「脸」的那一面），看上去像朝着屏幕，再画一个小箭头标出口（用户指出不该有朝向屏幕的样子）。
    """
    sheet = Image.new('RGBA', (18 * len(tops) - 2, 18 * 4 - 2), (0, 0, 0, 0))
    for row, facing in enumerate(['up', 'right', 'down', 'left']):
        for col, t in enumerate(tops):
            sheet.paste(rotated(t, facing), (col * 18, row * 18))
    return sheet


CIRCLE = {
    'HexImpetus': ([C + 'impetus/rightclick/top_dim', C + 'impetus/rightclick/top_lit'], C + 'impetus/rightclick/front_dim'),
    'HexImpetusLook': ([C + 'impetus/look/top_dim', C + 'impetus/look/top_lit'], C + 'impetus/look/front_dim'),
    'HexImpetusRedstone': ([C + 'impetus/redstone/top_dim', C + 'impetus/redstone/top_lit'], C + 'impetus/redstone/front_dim'),
    'HexImpetusEmpty': ([C + 'impetus/empty/top_dim', C + 'impetus/empty/top_lit'], C + 'impetus/empty/front_dim'),
    'HexDirectrixEmpty': ([C + 'directrix/empty/top_dim', C + 'directrix/empty/top_lit'], C + 'directrix/empty/front_dim'),
    # 布尔：[都不亮, 真, 假]（原版 STATE neither / true / false：媒质经过时按取到的值亮一头，环停下回到都不亮）
    'HexDirectrixBoolean': ([C + 'directrix/boolean/top_neither', C + 'directrix/boolean/top_true', C + 'directrix/boolean/top_false'],
                            C + 'directrix/boolean/front_dim_false'),
    # 红石：[没通电, 通电]（通电时亮出口那一头）
    'HexDirectrixRedstone': ([C + 'directrix/redstone/top_unpowered', C + 'directrix/redstone/top_powered'],
                             C + 'directrix/redstone/front_dim_unpowered'),
}
for name, (tops, front) in CIRCLE.items():
    save(circle_sheet([src(t) for t in tops]), 'Tiles', name + '.png')
    # 物品图标还用原版的正面：原版物品是带正面的方块模型，一眼认得出是哪种
    save(x2(src(front)), 'Items', 'Blocks', name + '.png')

save(slate_sheet(), 'Tiles', 'HexSlate.png')
save(x2(src('block/slate')), 'Items', 'Blocks', 'HexSlate.png')

FIXED = {
    'ScrollPaper': [src('block/scroll_paper')],
    'AncientScrollPaper': [src('block/ancient_scroll_paper')],
    'ScrollPaperLantern': [src('block/scroll_paper_lantern_side')],
    'AncientScrollPaperLantern': [src('block/ancient_scroll_paper_lantern_side')],
}
for name, faces in FIXED.items():
    save(frames(faces), 'Tiles', name + '.png')
    save(x2(faces[0]), 'Items', 'Blocks', name + '.png')
# 阿卡夏书架「有书」的书脊叠层（原版 akashic_bookshelf_overlay_1..4，灰色，画的时候按 iota 类型着色）
save(frames([src(f'block/akashic_bookshelf_overlay_{i}') for i in range(1, 5)]), 'Tiles', 'AkashicBookshelf_Books.png')
save(x2(existing_face('Tiles', 'AmethystSconce.png')), 'Items', 'Blocks', 'AmethystSconce.png')
# 促动石物品沿用各自类名的贴图路径
for tile, item in [('HexImpetus', 'HexImpetusItem'), ('HexImpetusLook', 'HexImpetusLookItem'),
                   ('HexImpetusRedstone', 'HexImpetusRedstoneItem'), ('HexImpetusEmpty', 'HexImpetusEmptyItem')]:
    save(x2(src(CIRCLE[tile][1])), 'Items', item + '.png')

# ── 物品（原版 ×2）────────────────────────────────────────────────
ITEMS = {
    'Abacus': 'item/abacus',
    'AmethystDust': 'item/amethyst_dust',
    'ChargedAmethyst': 'item/charged_amethyst',
    'JewelerHammer': 'item/jeweler_hammer',
    'ScryingLens': 'item/lens',
    'HexBookItem': 'item/patchouli_book',
    'LoreFragment': 'item/lore_fragment',
    'ScrollSmall': 'item/scroll_pristine_small',
    'ScrollMedium': 'item/scroll_pristine_medium',
    'ScrollLarge': 'item/scroll_pristine_large',
    'ScrollLargeAncient': 'item/scroll_ancient_large',
    'ThoughtKnot': 'item/thought_knot',
    'QuenchedAllayShard': 'item/quenched_shard_0',
    'MediaFlask': 'item/phial/phial_small_0',
    'Focus': 'item/cad/0_focus_empty',
    'Spellbook': 'item/cad/0_spellbook_empty',
    'Cypher': 'item/cad/0_cypher',
    'Trinket': 'item/cad/0_trinket',
    'Artifact': 'item/cad/0_artifact',
    'HexSlateItem': 'item/slate_blank',
    'AkashicRecordItem': 'block/akashic_record',
    # 法杖：泰拉木种 → 原版木种（颜色 / 出身最接近的一种）
    'OakStaff': 'item/staff/oak',
    'BorealStaff': 'item/staff/spruce',          # 针叶林
    'PalmStaff': 'item/staff/acacia',            # 海边 / 沙漠 ↔ 热带草原
    'MahoganyStaff': 'item/staff/jungle',        # 丛林
    'EbonwoodStaff': 'item/staff/warped',        # 腐化 ↔ 诡异菌（下界的「邪恶」木）
    'ShadewoodStaff': 'item/staff/crimson',      # 猩红 ↔ 绯红菌
    'PearlwoodStaff': 'item/staff/birch',        # 神圣的浅色木 ↔ 白桦
    'DynastyStaff': 'item/staff/bamboo',         # 东方 ↔ 竹
    'SpookyStaff': 'item/staff/dark_oak',        # 阴森 ↔ 深色橡木
    'AshStaff': 'item/staff/mangrove',           # 灰烬木（红褐）↔ 红树
    'CherryStaff': 'item/staff/cherry',
    'EdifiedStaff': 'item/staff/edified',
    'QuenchedStaff': 'item/staff/quenched_0',
    'MindspliceStaff': 'item/staff/mindsplice',
    'DevStaff': 'item/staff/old',                # 原版彩蛋「老法杖」
    'SubSandwich': 'item/sub_sandwich',
    'CreativeUnlocker': 'item/creative_unlocker',
}
for name, path in ITEMS.items():
    save(x2(src(path)), 'Items', name + '.png')


def composite(base, overlay):
    im = base.copy()
    im.alpha_composite(overlay)
    return im


save(x2(composite(src('item/cad/0_ancient_cypher'), src('item/cad/0_ancient_cypher_overlay'))), 'Items', 'AncientCypher.png')

# ── 状态图集（运行时按实例状态取格，见 ItemStateArt.cs）──────────────
# 核心 / 法术书：每行一个变体，列 = [空, 有内容, 有内容·叠层, 密封, 密封·叠层]；叠层按 iota 类型着色
for kind, name in [('focus', 'Focus'), ('spellbook', 'Spellbook')]:
    cells = []
    for v in range(8):
        cells += [x2(src(f'item/cad/{v}_{kind}_empty')),
                  x2(src(f'item/cad/{v}_{kind}_filled')), x2(src(f'item/cad/{v}_{kind}_filled_overlay')),
                  x2(src(f'item/cad/{v}_{kind}_sealed')), x2(src(f'item/cad/{v}_{kind}_sealed_overlay'))]
    save(grid(cells, 5), 'Items', 'States', name + '.png')
# 符纸 / 缀品 / 造物 / 远古杂件：每行一个变体，列 = [本体, 已封咒术时的叠层]
for kind, name in [('cypher', 'Cypher'), ('trinket', 'Trinket'), ('artifact', 'Artifact'), ('ancient_cypher', 'AncientCypher')]:
    cells = []
    for v in range(8):
        cells += [x2(src(f'item/cad/{v}_{kind}')), x2(src(f'item/cad/{v}_{kind}_overlay'))]
    save(grid(cells, 2), 'Items', 'States', name + '.png')
# 媒质之瓶：行 = 大小（small → largest），列 = 满度 0..4
sizes = ['small', 'medium', 'large', 'larger', 'largest']
save(grid([x2(src(f'item/phial/phial_{s}_{f}')) for s in sizes for f in range(5)], 5), 'Items', 'States', 'MediaFlask.png')
# 结念绳：[本体, 写入后的叠层]；石板：[空, 写了]
save(grid([x2(src('item/thought_knot')), x2(src('item/thought_knot_overlay'))], 2), 'Items', 'States', 'ThoughtKnot.png')
# 石板物品：[空白, 有图案]（原版 slate_blank / slate_written，见 Content/Items/HexSlateItem.cs）
save(grid([x2(src('item/slate_blank')), x2(src('item/slate_written'))], 2), 'Items', 'States', 'HexSlate.png')

# 卷轴提示框的底图（原版 gui/scroll.png、scroll_ancient.png，48 → ×2 = 96）
for name, path in [('ScrollTooltip', 'gui/scroll'), ('ScrollTooltipAncient', 'gui/scroll_ancient'), ('SlateTooltip', 'gui/slate')]:
    im = Image.open(os.path.join(JAR, path + '.png')).convert('RGBA')
    save(x2(im), 'Items', 'States', name + '.png')

# ── 启迪木家具（Content/Tiles/EdifiedFurnitureTiles.cs）─────────────────
# 门：原版上下两块（16×32）→ 泰拉门 1×3（16×48）：上半整块 + 下半的门板部分拉长 + 下半整块
up, lo = src('block/edified_door_upper'), src('block/edified_door_lower')
door = Image.new('RGBA', (16, 48), (0, 0, 0, 0))
door.paste(up, (0, 0))
for y in range(16):                       # 中段：下半块去掉底框的那几行，逐行拉满 16 行
    door.paste(lo.crop((0, min(y * 12 // 16, 11), 16, min(y * 12 // 16, 11) + 1)), (0, 16 + y))
door.paste(lo, (0, 32))


def outline(img):
    """悬停描边（<类名>_Highlight.png，TileID.Sets.HasOutlines 要）：贴图最外一圈不透明像素涂白，其余透明。
    按整件物体（整扇门）算，拼成图集后格子之间不会多出接缝线。"""
    out = Image.new('RGBA', img.size, (0, 0, 0, 0))
    w, h = img.size
    for y in range(h):
        for x in range(w):
            if img.getpixel((x, y))[3] == 0:
                continue
            if any(not (0 <= x + dx < w and 0 <= y + dy < h) or img.getpixel((x + dx, y + dy))[3] == 0
                   for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                out.putpixel((x, y), (255, 255, 255, 255))
    return out


def door_sheets(door_img):
    """(关着的门图集, 开着的门图集)：关 = 3 个随机样式 × 3 格；开 = [门轴侧边, 翻过去的门板] 往右 / 往左。"""
    closed_ = Image.new('RGBA', (54, 54), (0, 0, 0, 0))
    for c in range(3):
        for r in range(3):
            closed_.paste(door_img.crop((0, r * 16, 16, r * 16 + 16)), (c * 18, r * 18))
    edge_ = Image.new('RGBA', (16, 48), (0, 0, 0, 0))
    edge_.paste(door_img.crop((0, 0, 3, 48)), (0, 0))
    panel_ = door_img.transpose(Image.FLIP_LEFT_RIGHT)
    opened_ = Image.new('RGBA', (72, 54), (0, 0, 0, 0))
    for r in range(3):
        box = (0, r * 16, 16, r * 16 + 16)
        opened_.paste(edge_.crop(box), (0, r * 18))
        opened_.paste(panel_.crop(box), (18, r * 18))
        opened_.paste(door_img.crop(box), (36, r * 18))
        opened_.paste(edge_.transpose(Image.FLIP_LEFT_RIGHT).crop(box), (54, r * 18))
    return closed_, opened_


closed, opened = door_sheets(door)
save(closed, 'Tiles', 'EdifiedDoorClosed.png')
save(opened, 'Tiles', 'EdifiedDoorOpen.png')
hl_closed, hl_opened = door_sheets(outline(door))
save(hl_closed, 'Tiles', 'EdifiedDoorClosed_Highlight.png')
save(hl_opened, 'Tiles', 'EdifiedDoorOpen_Highlight.png')
save(x2(src('item/edified_door')), 'Items', 'EdifiedDoorItem.png')

planks = src('block/edified_planks')
dark = (37, 24, 64, 255)


def fence_pixel(u, v):
    """栅栏图案（世界对齐、16 周期）：中间一根立柱 + 上下两道横杆，木纹取启迪木板；外轮廓一圈深色。"""
    def solid(a, b):
        a, b = a % 16, b % 16
        return 6 <= a <= 9 or 3 <= b <= 5 or 10 <= b <= 12
    a, b = u % 16, v % 16
    if solid(a, b):
        return planks.getpixel((a, b))
    if any(solid(a + dx, b + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
        return dark
    return (0, 0, 0, 0)


# 栅栏墙：泰拉墙图集 36×36 一格（32×32 画面，向四周各伸 8 像素压到邻格上）。
# 帧的选法见 Terraria.Framing.WallFrame：邻格有墙的方向（上 1 / 左 2 / 右 4 / 下 8）才往那边伸；
# (列, 行) 表照它的 wallFrameLookup 的前三个随机样式。
WALL_LOOKUP = {0: [(9, 3), (10, 3), (11, 3)], 1: [(6, 3), (7, 3), (8, 3)], 2: [(12, 0), (12, 1), (12, 2)],
               3: [(1, 4), (3, 4), (5, 4)], 4: [(9, 0), (9, 1), (9, 2)], 5: [(0, 4), (2, 4), (4, 4)],
               6: [(6, 4), (7, 4), (8, 4)], 7: [(1, 2), (2, 2), (3, 2)], 8: [(6, 0), (7, 0), (8, 0)],
               9: [(5, 0), (5, 1), (5, 2)], 10: [(1, 3), (3, 3), (5, 3)], 11: [(4, 0), (4, 1), (4, 2)],
               12: [(0, 3), (2, 3), (4, 3)], 13: [(0, 0), (0, 1), (0, 2)], 14: [(1, 0), (2, 0), (3, 0)],
               15: [(1, 1), (2, 1), (3, 1)], 16: [(6, 1), (7, 1), (8, 1)], 17: [(6, 2), (7, 2), (8, 2)],
               18: [(10, 0), (10, 1), (10, 2)], 19: [(11, 0), (11, 1), (11, 2)]}
wall = Image.new('RGBA', (13 * 36, 7 * 36), (0, 0, 0, 0))
for style, cells in WALL_LOOKUP.items():
    mask = min(style, 15)
    up_, left_, right_, down_ = mask & 1, mask & 2, mask & 4, mask & 8
    frame = Image.new('RGBA', (32, 32), (0, 0, 0, 0))
    for py in range(32):
        for px in range(32):
            u, v = px - 8, py - 8
            if (u < 0 and not left_) or (u > 15 and not right_) or (v < 0 and not up_) or (v > 15 and not down_):
                continue
            frame.putpixel((px, py), fence_pixel(u, v))
    for col, row in cells:
        wall.paste(frame, (col * 36, row * 36))
save(wall, 'Tiles', 'EdifiedFence.png')
icon = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
for y in range(16):
    for x in range(16):
        post = 2 <= x <= 4 or 11 <= x <= 13
        rail = (5 <= y <= 6 or 10 <= y <= 11) and 4 < x < 11
        if (post and 1 <= y <= 15) or rail:
            icon.putpixel((x, y), planks.getpixel((x, y)))
save(x2(icon), 'Items', 'EdifiedFenceItem.png')


def plank_rect(img, x0, y0, w, h):
    for y in range(y0, y0 + h):
        for x in range(x0, x0 + w):
            edge_ = x in (x0, x0 + w - 1) or y in (y0, y0 + h - 1)
            img.putpixel((x, y), dark if edge_ else planks.getpixel((x % 16, y % 16)))


# 按钮：泰拉开关的 4 种贴法（列）= 地上 / 贴左边方块 / 贴右边方块 / 贴背景墙；两行（泰拉开关的开 / 关，按钮都一样）
button = Image.new('RGBA', (72, 36), (0, 0, 0, 0))
for row in range(2):
    plank_rect(button, 0 * 18 + 5, row * 18 + 12, 6, 4)     # 地上：按钮朝上
    plank_rect(button, 1 * 18 + 0, row * 18 + 5, 3, 6)      # 贴左边方块
    plank_rect(button, 2 * 18 + 13, row * 18 + 5, 3, 6)     # 贴右边方块
    plank_rect(button, 3 * 18 + 5, row * 18 + 6, 6, 4)      # 贴墙：正面
save(button, 'Tiles', 'EdifiedButton.png')
save(outline(button), 'Tiles', 'EdifiedButton_Highlight.png')   # 按钮每格互不相连，整张算即可
bi = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
plank_rect(bi, 4, 5, 8, 6)
save(x2(bi), 'Items', 'EdifiedButtonItem.png')

# 压力板：泰拉压力板 16×18 一格（往下画 2 像素，压住地面）；原版 14 像素宽的薄板
plate = Image.new('RGBA', (16, 18), (0, 0, 0, 0))
plank_rect(plate, 1, 12, 14, 3)
save(plate, 'Tiles', 'EdifiedPressurePlate.png')
pi = Image.new('RGBA', (16, 16), (0, 0, 0, 0))
plank_rect(pi, 1, 10, 14, 3)
save(x2(pi), 'Items', 'EdifiedPressurePlateItem.png')

# 增益：明晰 / 蒙翳（原版 mob_effect 图标，18×18）。泰拉的增益图标是 32×32、后面衬一个蓝框（减益是红框），
# 那个框用游戏自带的空白底图 Images/Buff 在启动时拼上（Content/Items/GridPotions.cs 的 GridPotionArt），这里只放图标：
# 原尺寸、居中，四周留给底框
for effect, name in (('enlarge_grid', 'EnlargeGrid'), ('shrink_grid', 'ShrinkGrid')):
    eye = Image.open(os.path.join(JAR, 'mob_effect', effect + '.png')).convert('RGBA')
    buff = Image.new('RGBA', (32, 32), (0, 0, 0, 0))
    buff.alpha_composite(eye, ((32 - eye.width) // 2, (32 - eye.height) // 2))
    save(buff, 'Buffs', name + '.png')

# 壁挂卷轴（原版 WallScrollRenderer）：卷轴底图铺满整张，小 / 中 / 大 = 1 / 2 / 3 格见方，
# 底图分别是 block/scroll_paper（16）、entity/scroll_medium（32）、entity/scroll_large（48），一格正好 16 像素、一比一。
# 方块图集按泰拉多格方块的格式切开：每格 16×16、间隔 18。
# 远古卷轴挂上去时换成做旧的底图（原版 isAncient → *_ancient_*），整张存着，由 WallScrollTile.SpecialDraw 盖在上面。
# 挂轴框物品（移植版的两段式挂法才有，原版没有）的图标就用这张底图：小号 ×2，中、大号原尺寸。
def whole(name):
    return Image.open(os.path.join(JAR, name + '.png')).convert('RGBA')


def tile_sheet(im):
    n = im.width // 16
    sheet = Image.new('RGBA', (18 * n - 2, 18 * n - 2), (0, 0, 0, 0))
    for fy in range(n):
        for fx in range(n):
            sheet.paste(im.crop((fx * 16, fy * 16, fx * 16 + 16, fy * 16 + 16)), (fx * 18, fy * 18))
    return sheet


for size, pristine, ancient in (('Small', 'block/scroll_paper', 'block/ancient_scroll_paper'),
                                ('Medium', 'entity/scroll_medium', 'entity/scroll_ancient_medium'),
                                ('Large', 'entity/scroll_large', 'entity/scroll_ancient_large')):
    bg = whole(pristine)
    save(tile_sheet(bg), 'Tiles', 'WallScroll' + size + '.png')
    save(whole(ancient), 'Tiles', 'WallScrollAncient' + size + '.png')
    save(x2(bg) if bg.width == 16 else bg, 'Items', 'WallScrollFrame' + size + '.png')

print(f'写出 {len(written)} 张')
