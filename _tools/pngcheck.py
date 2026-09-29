import os, sys
from PIL import Image
roots = [r"D:\DeepSeekHarness\tmod\HexCastingTerraria",
         r"C:\Users\MSI-PC\Documents\My Games\Terraria\tModLoader\ModSources\HexCastingTerraria"]
bad = []
n = 0
for root in roots:
    for dp, dn, fn in os.walk(root):
        if "obj" in dp.split(os.sep) or "bin" in dp.split(os.sep):
            continue
        for f in fn:
            if not f.lower().endswith(".png"):
                continue
            p = os.path.join(dp, f)
            n += 1
            try:
                im = Image.open(p)
                im.load()
                if im.mode not in ("RGBA", "RGB"):
                    bad.append((p, "mode=" + im.mode))
            except Exception as e:
                bad.append((p, repr(e)))
print("checked", n, "png;  decode failures:", len(bad))
for p, why in bad:
    print("  BAD", p, why)
