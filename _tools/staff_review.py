import os
from PIL import Image, ImageDraw, ImageFont
MOD = r"D:\DeepSeekHarness\tmod\HexCastingTerraria"
F = "C:/Windows/Fonts/msyh.ttc"
def fnt(s): return ImageFont.truetype(F, s)
groups = {
  "Content/Items  *Staff.png": [],
  "Assets/Staffs (无代码引用)": [],
}
for dp, dn, fn in os.walk(MOD):
    if "obj" in dp or "bin" in dp: continue
    rel = os.path.relpath(dp, MOD).replace("\\","/")
    for f in sorted(fn):
        if not f.endswith(".png"): continue
        if "Staff.png" in f and rel.endswith("Content/Items"):
            groups["Content/Items  *Staff.png"].append(os.path.join(dp,f))
        elif rel.endswith("Assets/Staffs"):
            groups["Assets/Staffs (无代码引用)"].append(os.path.join(dp,f))
Z = 5; CELL = 32*Z + 30
cols = 6
rows_total = sum((len(v)+cols-1)//cols for v in groups.values())
W = 20 + cols*(CELL+8)
H = 30 + rows_total*(CELL+30)
img = Image.new("RGB",(W,H),(250,248,244)); d = ImageDraw.Draw(img)
y = 8
for title, paths in groups.items():
    d.text((16,y), "%s   —— %d 张" % (title, len(paths)), fill=(20,15,30), font=fnt(20)); y += 26
    for i,p in enumerate(paths):
        r,c = divmod(i,cols)
        x = 20 + c*(CELL+8); yy = y + r*(CELL+30)
        d.rectangle([x,yy,x+CELL,yy+CELL], fill=(255,255,255), outline=(220,216,226))
        sp = Image.open(p).convert("RGBA")
        big = sp.resize((sp.width*Z, sp.height*Z), Image.NEAREST)
        img.paste(big,(x+(CELL-big.width)//2, yy+(CELL-big.height)//2), big)
        d.text((x+4, yy+CELL+2), os.path.basename(p), fill=(40,34,55), font=fnt(13))
    y += ((len(paths)+cols-1)//cols)*(CELL+30) + 14
img.save("staff_review.png"); print("staff_review.png", img.size)
