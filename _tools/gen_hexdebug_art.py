# 从上游 HexDebug 0.9.0 的 jar（MIT）取物品贴图，放大 2 倍（最近邻，与本体 32x32 物品图一致）写进附属目录。
# 用法：python _tools/gen_hexdebug_art.py
import io
import os
import zipfile

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JAR = 'D:/DeepSeekHarness/mod/hexdebug-fabric-0.9.0+1.20.1.jar'
OUT = os.path.join(ROOT, 'HexCastingTerraria', 'Addons', 'HexDebug', 'Assets')
PREFIX = 'assets/hexdebug/textures/item/'

NAMES = {
    'debugger.png': 'Debugger.png',
    'quenched_debugger.png': 'QuenchedDebugger.png',
    'evaluator.png': 'Evaluator.png',
    'quenched_evaluator.png': 'QuenchedEvaluator.png',
    'debugger/has_hex/full.png': 'Overlay_HasHex.png',
    'debugger/debug_state/debugging.png': 'Overlay_Debugging.png',
    'evaluator/eval_state/modified.png': 'Overlay_Modified.png',
}
for state in ('debugging', 'not_debugging'):
    for mode in ('continue', 'over', 'in', 'out', 'restart', 'stop'):
        NAMES['debugger/step_mode/%s/%s.png' % (state, mode)] = 'Step_%s_%s.png' % (state, mode)

BLOCK = 'assets/hexdebug/textures/block/'


def block(name):
    return Image.open(io.BytesIO(z.read(BLOCK + name))).convert('RGBA')


def tile_sheet(frames, dst):
    # 泰拉方块贴图：每帧 16x16，帧之间空 2 像素（横排）
    sheet = Image.new('RGBA', (18 * len(frames) - 2, 16), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        sheet.paste(f.resize((16, 16), Image.NEAREST), (18 * i, 0))
    sheet.save(os.path.join(OUT, dst))


def item_icon(img, dst):
    img.resize((32, 32), Image.NEAREST).save(os.path.join(OUT, dst))


os.makedirs(OUT, exist_ok=True)
z = zipfile.ZipFile(JAR)
for src, dst in NAMES.items():
    im = Image.open(io.BytesIO(z.read(PREFIX + src))).convert('RGBA')
    im = im.resize((im.width * 2, im.height * 2), Image.NEAREST)
    im.save(os.path.join(OUT, dst))
# 剪接台 / 制念台（MC 方块的正面；制念台：未融注 dim、已融注 lit 两帧）
tile_sheet([block('splicing_table/front.png')], 'SplicingTable.png')
item_icon(block('splicing_table/front.png'), 'SplicingTableItem.png')
tile_sheet([block('enlightened_splicing_table/front/dim.png'), block('enlightened_splicing_table/front/lit.png')], 'EnlightenedSplicingTable.png')
item_icon(block('enlightened_splicing_table/front/dim.png'), 'EnlightenedSplicingTableItem.png')
# 核心框架：空 / 装着东西两帧
tile_sheet([block('focus_holder/empty.png'), block('focus_holder/full.png')], 'FocusHolder.png')
item_icon(block('focus_holder/empty.png'), 'FocusHolderItem.png')
print('wrote', len(NAMES) + 6, 'files to', OUT)
