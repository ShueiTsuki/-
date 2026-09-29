import os
from PIL import Image
MOD = r"D:\DeepSeekHarness\tmod\HexCastingTerraria"
rows = []
for dp, dn, fn in os.walk(MOD):
    if "obj" in dp or "bin" in dp: continue
    for f in sorted(fn):
        if not f.endswith(".png"): continue
        p = os.path.join(dp, f)
        im = Image.open(p).convert("RGBA")
        px = list(im.getdata())
        opaque = [q for q in px if q[3] > 8]
        if not opaque: 
            rows.append((os.path.relpath(p, MOD), im.size, 0, 0.0, "全透明"))
            continue
        cols = set(opaque)
        # 亮度标准差：越小越"平"
        lum = [0.299*q[0]+0.587*q[1]+0.114*q[2] for q in opaque]
        mean = sum(lum)/len(lum)
        var = sum((x-mean)**2 for x in lum)/len(lum)
        sd = var ** 0.5
        rows.append((os.path.relpath(p, MOD), im.size, len(cols), sd, ""))
rows.sort(key=lambda r: (r[2], r[3]))
print("%-52s %-9s %5s %6s  %s" % ("文件","尺寸","色数","亮度σ","备注"))
for r in rows[:26]:
    print("%-52s %-9s %5d %6.1f  %s" % (r[0], "%dx%d"%r[1], r[2], r[3], r[4]))
print("...")
print("总计 %d 张, 色数<8 的有 %d 张" % (len(rows), sum(1 for r in rows if r[2] < 8)))
