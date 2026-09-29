# 把 drawtest 记录的书本绘制命令（_shots/book/*.json）画成 PNG，用来**看图**检查书的排版。
#
# 用法：
#   cd tests/drawtest; DRAWTEST_RENDER=1 dotnet run -c Release   # 产出 JSON
#   python _tools/render_book_preview.py                          # JSON → PNG（同目录）
#
# 文字用微软雅黑近似泰拉的中文字体；原版物品没有贴图可用，画成带名字的灰格。
# 这是**预览**：几何、贴图、配色是真的；字形与字宽是近似的。
import glob, json, os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
SHOTS = os.path.join(ROOT, '_shots', 'book')
ATLAS = os.path.join(ROOT, 'HexCastingTerraria', 'Client', 'UI', 'BookAtlas')
CONTENT = os.path.join(ROOT, 'HexCastingTerraria', 'Content')
TEX = {'book': 'patchi_book.png', 'crafting': 'crafting.png', 'filler': 'patchi_filler.png'}
FONT = 'C:/Windows/Fonts/msyh.ttc'

_tex_cache, _font_cache, _item_cache = {}, {}, {}


def tex(name):
    if name not in _tex_cache:
        _tex_cache[name] = Image.open(os.path.join(ATLAS, TEX[name])).convert('RGBA')
    return _tex_cache[name]


def font(px):
    px = max(6, int(round(px)))
    if px not in _font_cache:
        _font_cache[px] = ImageFont.truetype(FONT, px)
    return _font_cache[px]


def item_img(key):
    if key in _item_cache:
        return _item_cache[key]
    img = None
    if key.startswith('Mod:'):
        name = key[4:]
        for p in glob.glob(os.path.join(CONTENT, '**', name + '.png'), recursive=True):
            img = Image.open(p).convert('RGBA')
            break
        if img is None and name.endswith('Item'):
            for p in glob.glob(os.path.join(CONTENT, '**', name[:-4] + '.png'), recursive=True):
                im = Image.open(p).convert('RGBA')
                img = im.crop((0, 0, min(im.width, 18), min(im.height, 18)))
                break
    _item_cache[key] = img
    return img


def paste(canvas, img, box, alpha=1.0):
    x, y, w, h = [int(round(v)) for v in box]
    if w <= 0 or h <= 0:
        return
    im = img.resize((w, h), Image.NEAREST)
    if alpha < 1.0:
        a = im.getchannel('A').point(lambda v: int(v * alpha))
        im.putalpha(a)
    canvas.alpha_composite(im, (x, y))


def render(path):
    d = json.load(open(path, encoding='utf-8'))
    canvas = Image.new('RGBA', (d['w'], d['h']), (30, 27, 36, 255))
    over = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    bx0 = by0 = 10 ** 9
    bx1 = by1 = 0
    for op in d['ops']:
        k = op['op']
        if k == 'img':
            sx, sy, sw, sh = op['src']
            src = tex(op['tex']).crop((int(sx), int(sy), int(sx + sw), int(sy + sh)))
            paste(canvas, src, op['dst'], op['c'][3] / 255)
            if op['tex'] == 'book' and op['src'][2] == 272:
                x, y, w, h = op['dst']
                bx0, by0, bx1, by1 = x, y, x + w, y + h
        elif k == 'item':
            img = item_img(op['key'])
            x, y, w, h = op['dst']
            if img is not None:
                s = min(w / img.width, h / img.height)
                iw, ih = img.width * s, img.height * s
                paste(canvas, img, (x + (w - iw) / 2, y + (h - ih) / 2, iw, ih))
            else:
                dr = ImageDraw.Draw(canvas)
                dr.rectangle([x + 1, y + 1, x + w - 2, y + h - 2], fill=(150, 140, 130, 255), outline=(90, 80, 70, 255))
                label = op['key'].split(':')[-1][:3]
                dr.text((x + 2, y + h / 3), label, font=font(h / 3), fill=(255, 255, 255, 255))
        elif k == 'rect':
            x, y, w, h = op['dst']
            layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
            ImageDraw.Draw(layer).rectangle([x, y, x + w - 1, y + h - 1], fill=tuple(op['c']))
            canvas.alpha_composite(layer)
        elif k == 'line':
            x1, y1, x2, y2 = op['p']
            layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
            ImageDraw.Draw(layer).line([x1, y1, x2, y2], fill=tuple(op['c']), width=max(1, int(round(op['w']))))
            canvas.alpha_composite(layer)
        elif k == 'circle':
            cx, cy = op['p']
            r = op['r']
            layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
            ImageDraw.Draw(layer).ellipse([cx - r, cy - r, cx + r, cy + r], fill=tuple(op['c']))
            canvas.alpha_composite(layer)
        elif k == 'text':
            x, y = op['p']
            s = op['s']
            f = font(s * 0.62)
            dr = ImageDraw.Draw(canvas)
            dr.text((x, y + s * 0.2), op['t'], font=f, fill=tuple(op['c']))
            if op['b']:
                dr.text((x + 1, y + s * 0.2), op['t'], font=f, fill=tuple(op['c']))
    canvas.alpha_composite(over)
    if bx1 > bx0:
        pad = 24
        canvas = canvas.crop((int(bx0 - pad), int(by0 - pad), int(bx1 + pad), int(by1 + pad)))
    out = os.path.splitext(path)[0] + '.png'
    canvas.convert('RGB').save(out)
    return out


if __name__ == '__main__':
    for p in sorted(glob.glob(os.path.join(SHOTS, '*.json'))):
        print(render(p))
