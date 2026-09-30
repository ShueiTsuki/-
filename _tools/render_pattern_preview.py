"""把 drawtest 在 DRAWTEST_RENDER=1 时写出的静态图案三角形画成 PNG（离线看图用）。
用法：python _tools/render_pattern_preview.py <pattern_art.json> <输出.png>
"""
import json
import sys
from PIL import Image, ImageDraw

tris = json.load(open(sys.argv[1]))
S = 3  # 放大 3 倍看细节
W = int(max(v[0] for v in tris) + 20) * S
H = int(max(v[1] for v in tris) + 20) * S
img = Image.new('RGBA', (W, H), (60, 58, 70, 255))
for i in range(0, len(tris) - 2, 3):
    a, b, c = tris[i], tris[i + 1], tris[i + 2]
    argb = a[2]
    col = ((argb >> 16) & 255, (argb >> 8) & 255, argb & 255, (argb >> 24) & 255)
    layer = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).polygon([(a[0] * S, a[1] * S), (b[0] * S, b[1] * S), (c[0] * S, c[1] * S)], fill=col)
    img.alpha_composite(layer)
img.save(sys.argv[2])
print(img.size)
