#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""贴图总览表：把模组里所有 PNG 拼成几页带文件名的图，用来做**风格一致性审查**。

为什么需要：一百多张图散在几十个目录里，肉眼一张张开根本看不出「哪几张跑偏了」。
拼成网格 + 放大到同一视觉尺寸之后，风格不统一会立刻跳出来（描边、打光、饱和度）。

用法：python art_sheet.py [每页列数] [每页行数]
输出：art_sheet_pN.png  到本目录
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
MOD = os.path.join(os.path.dirname(HERE), 'HexCastingTerraria')
FONT_PATH = 'C:/Windows/Fonts/msyh.ttc'

COLS = int(sys.argv[1]) if len(sys.argv) > 1 else 8
ROWS = int(sys.argv[2]) if len(sys.argv) > 2 else 6
CELL = 132
PAD = 8
HEADER = 34


def font(size):
    return ImageFont.truetype(FONT_PATH, size)


def collect():
    out = []
    for dp, dn, fn in os.walk(MOD):
        parts = set(dp.split(os.sep))
        if 'obj' in parts or 'bin' in parts:
            continue
        for f in sorted(fn):
            if f.lower().endswith('.png'):
                out.append(os.path.join(dp, f))
    out.sort()
    return out


def render(paths, page, pages, per):
    w = PAD + COLS * (CELL + PAD)
    h = HEADER + PAD + ROWS * (CELL + PAD)
    img = Image.new('RGB', (w, h), (246, 244, 240))
    d = ImageDraw.Draw(img)
    d.text((PAD, 8), 'HexCastingTerraria 贴图总览  %d/%d   （共 %d 张，按放大后的视觉尺寸对齐）'
           % (page + 1, pages, len(paths)), fill=(30, 24, 40), font=font(19))

    for i, p in enumerate(paths):
        r, c = divmod(i, COLS)
        x = PAD + c * (CELL + PAD)
        y = HEADER + PAD + r * (CELL + PAD)

        d.rectangle([x, y, x + CELL, y + CELL], fill=(255, 255, 255), outline=(222, 218, 226))
        # 棋盘格底，方便看透明度
        for by in range(0, CELL, 12):
            for bx in range(0, CELL, 12):
                if (bx // 12 + by // 12) % 2 == 0:
                    d.rectangle([x + bx, y + by, x + min(bx + 12, CELL), y + min(by + 12, CELL)],
                                fill=(238, 236, 240))

        try:
            sp = Image.open(p).convert('RGBA')
        except Exception as exc:  # 坏图要看得见，不要静默跳过
            d.text((x + 6, y + 6), 'BAD', fill=(200, 30, 30), font=font(16))
            d.text((x + 6, y + 60), repr(exc)[:24], fill=(200, 30, 30), font=font(11))
            continue

        scale = max(1, int(96 / max(sp.width, sp.height)))
        big = sp.resize((sp.width * scale, sp.height * scale), Image.NEAREST)
        px = x + (CELL - big.width) // 2
        py = y + (CELL - big.height) // 2
        img.paste(big, (px, py), big)

        name = os.path.relpath(p, MOD).replace('\\', '/')
        rel = name.rsplit('/', 1)
        d.text((x + 4, y + CELL - 26), rel[-1][:20], fill=(40, 34, 55), font=font(11))
        if len(rel) > 1:
            d.text((x + 4, y + CELL - 14), rel[0][-22:], fill=(140, 134, 155), font=font(10))
    return img


def main():
    paths = collect()
    per = COLS * ROWS
    pages = (len(paths) + per - 1) // per
    for p in range(pages):
        img = render(paths[p * per:(p + 1) * per], p, pages, per)
        out = os.path.join(HERE, 'art_sheet_p%d.png' % (p + 1))
        img.save(out)
        print('%s  %dx%d  (%d 张)' % (out, img.width, img.height, min(per, len(paths) - p * per)))
    print('总计 %d 张' % len(paths))


if __name__ == '__main__':
    main()
