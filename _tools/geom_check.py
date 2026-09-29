# -*- coding: utf-8 -*-
"""判定 draw.py 的 pattern 几何是否少画一段：
把「官方图案图」与「draw.py 现行算法」「补上末段的算法」并排画出来对比。

原版 HexPattern.kt positions():
    out = [start]
    for a in angles:  cursor += compass; out.append(cursor); compass *= a
    out.append(cursor + compass)      # ← 关键：循环外还有**最后一步**
所以点数 = 角度数 + 2，段数 = 角度数 + 1。

draw.py 的 path_pts 没有最后那一步，于是段数 = 角度数，整体少一段。
"""
import os
import sys
import math

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, r'D:\DeepSeekHarness\图案工具')
import draw as hexdraw  # noqa: E402

F = "C:/Windows/Fonts/msyh.ttc"


def fnt(s):
    return ImageFont.truetype(F, s)


def path_pts_fixed(sig, start):
    """原版 positions() 的忠实实现：比 draw.py 多走最后一步。"""
    pts = [(0, 0)]
    q = r = 0
    comp = hexdraw.ORD.index(start)
    for ch in sig:
        dq, dr = hexdraw.DELTA[hexdraw.ORD[comp]]
        q += dq
        r += dr
        pts.append((q, r))
        comp = (comp + hexdraw.ANG[ch]) % 6
    # 最后一步：转完弯之后再走一格（draw.py 缺的就是这两行）
    dq, dr = hexdraw.DELTA[hexdraw.ORD[comp]]
    pts.append((q + dq, r + dr))
    return pts


def render(pts, size=300, color=(120, 90, 170)):
    s3 = math.sqrt(3)
    pix = [(s3 * q + s3 / 2 * r, 1.5 * r) for q, r in pts]
    xs = [p[0] for p in pix]
    ys = [p[1] for p in pix]
    cx = (min(xs) + max(xs)) / 2
    cy = (min(ys) + max(ys)) / 2
    span = max(max(xs) - min(xs), max(ys) - min(ys), 1)
    sc = size * 0.62 / span
    img = Image.new('RGB', (size, size), (252, 250, 245))
    d = ImageDraw.Draw(img)
    xy = [(size / 2 + (x - cx) * sc, size / 2 + (y - cy) * sc) for x, y in pix]
    d.line(xy, fill=color, width=10, joint='curve')
    for k, (x, y) in enumerate(xy):
        if k == 0:
            d.ellipse([x - 11, y - 11, x + 11, y + 11], fill=(232, 78, 78))
        else:
            d.ellipse([x - 5, y - 5, x + 5, y + 5], fill=(255, 255, 255), outline=color, width=3)
    return img


def build(cases, out):
    cell = 300
    pad = 12
    head = 58
    W = pad + 3 * (cell + pad)
    H = head + len(cases) * (cell + 60 + pad)
    img = Image.new('RGB', (W, H), (255, 255, 255))
    d = ImageDraw.Draw(img)
    d.text((pad, 12), '左：官方原图   中：draw.py 现行（少一段）   右：补上末段（应为正确）',
           fill=(30, 24, 40), font=fnt(20))

    y = head
    for sig, start, official in cases:
        d.text((pad, y - 22), 'signature="%s"  start=%s' % (sig, start),
               fill=(60, 50, 80), font=fnt(17))
        x = pad
        img.paste(Image.open(official).convert('RGB').resize((cell, cell)), (x, y))
        x += cell + pad
        img.paste(render(hexdraw.path_pts(sig, start), cell), (x, y))
        x += cell + pad
        img.paste(render(path_pts_fixed(sig, start), cell), (x, y))
        y += cell + 60 + pad

    img.save(out)
    print(out, img.size)


if __name__ == '__main__':
    build([
        ('qdwdq', 'NE', r'D:\DeepSeekHarness\咒法学图案\594875.png'),
        ('de', 'NE', r'D:\DeepSeekHarness\咒法学图案\594866.png'),
        ('qaq', 'NE', r'D:\DeepSeekHarness\咒法学图案\594861.png'),
    ], 'geom_check.png')
