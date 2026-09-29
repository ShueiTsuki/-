import os
from PIL import Image, ImageDraw, ImageFont
MOD = r"D:\DeepSeekHarness\tmod\HexCastingTerraria"
F = "C:/Windows/Fonts/msyh.ttc"
names = ["Content/Tiles/ConjuredLight.png", "Content/Tiles/ConjuredBlock.png",
         "Content/Tiles/AkashicRecord.png", "Content/Items/AkashicRecordItem.png",
         "Content/Tiles/AkashicBookshelf.png", "Content/Tiles/QuenchedAllay.png",
         "Content/Tiles/HexSlate.png", "Content/Tiles/SlateBlock.png"]
Z = 9
CELL = 16 * Z + 20
img = Image.new("RGB", (len(names) * (CELL + 10) + 20, CELL + 70), (250, 248, 244))
d = ImageDraw.Draw(img)


def f(s):
    return ImageFont.truetype(F, s)


for i, n in enumerate(names):
    p = os.path.join(MOD, n.replace("/", os.sep))
    x = 20 + i * (CELL + 10)
    y = 20
    d.rectangle([x - 4, y - 4, x + CELL + 4, y + CELL + 4], fill=(255, 255, 255), outline=(210, 206, 216))
    sp = Image.open(p).convert("RGBA")
    big = sp.resize((sp.width * Z, sp.height * Z), Image.NEAREST)
    img.paste(big, (x, y), big)
    d.text((x, y + CELL + 8), os.path.basename(n), fill=(30, 24, 40), font=f(14))
    d.text((x, y + CELL + 26), "%dx%d" % sp.size, fill=(140, 134, 155), font=f(12))
img.save("tile_zoom.png")
print(img.size)
