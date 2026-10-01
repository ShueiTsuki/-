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

os.makedirs(OUT, exist_ok=True)
z = zipfile.ZipFile(JAR)
for src, dst in NAMES.items():
    im = Image.open(io.BytesIO(z.read(PREFIX + src))).convert('RGBA')
    im = im.resize((im.width * 2, im.height * 2), Image.NEAREST)
    im.save(os.path.join(OUT, dst))
print('wrote', len(NAMES), 'files to', OUT)
