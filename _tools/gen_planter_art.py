"""紫水晶种植盆（移植版新增，原版没有）的贴图（可重复运行）。

原版没有这个方块，所以没有现成贴图；这里只拿原版 HexMod 的方块贴图拼：
  - 盆身：板岩 `block/slate`（自带左上亮、右下暗的倒角），左右各收 1 像素、底边再收 1 像素，像个盆
  - 盆口：紫水晶粉块 `block/amethyst_dust_block` 的一段当「土」，晶簇就长在这上面
  - 口沿：磨制紫水晶 `block/amethyst_polished` 最亮的那一行，下面压一道暗线
  - 正面嵌一块紫水晶：板岩紫水晶瓦 `block/deco/slate_amethyst_tiles` 里那块紫水晶的内芯（6×6）

产出（格式同兄弟方块 GeodeCore，见 gen_textures.py）：
  - Content/Tiles/AmethystPlanter.png：288×270 自动选帧图集，每一帧都是同一个盆
  - Content/Items/Blocks/AmethystPlanter.png：物品图标，最近邻 ×2 = 32×32

来源：本地原版 jar（hexwork/jar/assets/hexcasting/textures，HexMod v0.11.4，MIT）。不用泰拉原版贴图。
用法：python _tools/gen_planter_art.py
"""
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JAR = os.path.join(os.path.dirname(ROOT), 'hexwork', 'jar', 'assets', 'hexcasting', 'textures')
MOD = os.path.join(ROOT, 'HexCastingTerraria', 'Content')

CLEAR = (0, 0, 0, 0)
LIGHT = (0x55, 0x45, 0x5e, 255)   # 板岩的亮边色
DARK = (0x19, 0x14, 0x25, 255)    # 板岩的暗边色


def src(name):
    return Image.open(os.path.join(JAR, name + '.png')).convert('RGBA').crop((0, 0, 16, 16))


def block_sheet(face):
    """288×270 标准方块图集：16 列 × 15 行、间隔 18，每一帧都是完整的一块（同 gen_textures.block_sheet）。"""
    sheet = Image.new('RGBA', (288, 270), CLEAR)
    for r in range(15):
        for c in range(16):
            sheet.paste(face, (c * 18, r * 18))
    return sheet


def planter_face():
    slate = src('block/slate')
    dust = src('block/amethyst_dust_block')
    polished = src('block/amethyst_polished')
    inlay = src('block/deco/slate_amethyst_tiles').crop((9, 1, 15, 7))

    f = Image.new('RGBA', (16, 16), CLEAR)
    # 盆口的「土」：y 0..2，取粉块中段那几行（亮暗条纹最匀）；左右两列是盆沿
    for y in range(3):
        for x in range(16):
            f.putpixel((x, y), dust.getpixel((x, y + 6)))
        f.putpixel((0, y), LIGHT)
        f.putpixel((15, y), DARK)
    # 口沿：亮紫一行 + 暗线一行
    for x in range(16):
        f.putpixel((x, 3), polished.getpixel((x, 0)))
        f.putpixel((x, 4), DARK)
    # 盆身：y 5..15，左右各收 1 像素，最底一行再收 1 像素
    for y in range(5, 16):
        inset = 2 if y == 15 else 1
        for x in range(inset, 16 - inset):
            f.putpixel((x, y), DARK if y == 15 else slate.getpixel((x, y)))
        f.putpixel((inset, y), LIGHT if y < 15 else DARK)
        f.putpixel((15 - inset, y), DARK)
    # 正面的紫水晶嵌片
    f.alpha_composite(inlay, ((16 - inlay.width) // 2, 6))
    return f


def save(im, *path):
    p = os.path.join(MOD, *path)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    im.save(p)
    print('写出', os.path.relpath(p, ROOT))


face = planter_face()
save(block_sheet(face), 'Tiles', 'AmethystPlanter.png')
save(face.resize((32, 32), Image.NEAREST), 'Items', 'Blocks', 'AmethystPlanter.png')
