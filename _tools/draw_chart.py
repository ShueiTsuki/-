#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""中文图案图卡生成器。

为什么要另写一个而不是改「图案工具/draw.py」：那是用户自己的工具，别动。
这里 **复用** 它的几何算法（path_pts / stroke_dirs），只把 PIL 的默认位图字体
换成中文字体 —— 默认字体没有 CJK 字形，中文标题会全变方块。

产出：
    spells_zh.png          几个简单法术的画法（每个法术一行图案 + 中文说明）
    patterns_zh_pN.png     全部图案的中文对照表（每页 32 个）

用法：python draw_chart.py
"""
import json
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = r'D:\DeepSeekHarness\图案工具'
sys.path.insert(0, TOOL)
import draw as hexdraw  # noqa: E402  复用 path_pts / stroke_dirs

FONT_PATH = 'C:/Windows/Fonts/msyh.ttc'  # 微软雅黑，带完整 CJK
INK = (120, 90, 170)
BG = (252, 250, 245)

_font_cache = {}


def font(size):
    if size not in _font_cache:
        _font_cache[size] = ImageFont.truetype(FONT_PATH, size)
    return _font_cache[size]


def dir_short(name):
    """NorthEast -> NE"""
    out = []
    for ch in name:
        if ch.isupper() and out:
            out.append('_')
        out.append(ch.upper())
    return ''.join(out)


def render_cell(size, sig, start, title, sub='', color=INK):
    """一个图案：上半是走线图，下半是名字与签名。"""
    img = Image.new('RGB', (size, size + 62), BG)
    d = ImageDraw.Draw(img)

    pts = hexdraw.path_pts(sig, start)
    s3 = 3 ** 0.5
    pix = [(s3 * q + s3 / 2 * r, 1.5 * r) for q, r in pts]
    xs = [p[0] for p in pix]
    ys = [p[1] for p in pix]
    cx = (min(xs) + max(xs)) / 2
    cy = (min(ys) + max(ys)) / 2
    span = max(max(xs) - min(xs), max(ys) - min(ys), 1)
    sc = size * 0.50 / span
    xy = [(size / 2 + (x - cx) * sc, size / 2 + (y - cy) * sc) for x, y in pix]

    d.line(xy, fill=color, width=11, joint='curve')
    for k in range(len(xy) - 1):
        x1, y1 = xy[k]
        x2, y2 = xy[k + 1]
        mx, my = (x1 + x2) / 2, (y1 + y2) / 2
        a = math.atan2(y2 - y1, x2 - x1)
        L = 9
        d.polygon([
            (mx + L * math.cos(a), my + L * math.sin(a)),
            (mx + L * math.cos(a + 2.5), my + L * math.sin(a + 2.5)),
            (mx + L * math.cos(a - 2.5), my + L * math.sin(a - 2.5)),
        ], fill=(255, 255, 255))
    for k, (x, y) in enumerate(xy):
        if k == 0:
            d.ellipse([x - 12, y - 12, x + 12, y + 12], fill=(232, 78, 78))
        else:
            d.ellipse([x - 5, y - 5, x + 5, y + 5], fill=(255, 255, 255), outline=color, width=3)
            d.text((x + 7, y - 5), str(k), fill=(70, 70, 70), font=font(13))

    # 起点方向箭头：只画一个红点说明不了「往哪边走」，而菱形这类对称图形
    # 从不同角落笔会得到**循环移位**的签名（下角起笔 qaq 能命中，右上角起笔 aqa 命中不了），
    # 所以起始方向必须画清楚。游戏内的书页预览也画同一根箭头（见 PatternRenderer）。
    if len(xy) >= 2:
        (x0, y0), (x1, y1) = xy[0], xy[1]
        vx, vy = x1 - x0, y1 - y0
        n = math.hypot(vx, vy)
        if n > 0.01:
            vx, vy = vx / n, vy / n
            L = 30
            tx, ty = x0 + vx * L, y0 + vy * L
            d.line([(x0, y0), (tx, ty)], fill=(255, 170, 40), width=4)
            for sgn in (1, -1):
                wx, wy = -vy * sgn, vx * sgn
                d.line([(tx, ty), (tx - vx * 11 + wx * 8, ty - vy * 11 + wy * 8)],
                       fill=(255, 170, 40), width=4)

    d.text((8, size + 2), title, fill=(20, 16, 30), font=font(18))
    d.text((8, size + 26), '"%s" %s' % (sig, start), fill=(120, 112, 130), font=font(14))
    if sub:
        d.text((8, size + 44), sub, fill=(150, 145, 158), font=font(13))
    return img


def load_patterns():
    with open(os.path.join(HERE, 'patterns_all.json'), encoding='utf-8') as fh:
        rows = json.load(fh)
    return {r['id']: r for r in rows}


def synth(pid, name, sig, start, note=''):
    return {'id': pid, 'name': name, 'sig': sig, 'dir': start, 'kind': 'pattern', 'note': note}


# 特殊图案：不在图案注册表里，是「字面量」由特殊处理器解析的。
NUMBER3 = synth('__number3__', '数字 3（特殊图案）', 'aqaawww', 'East',
                'aqaa=正数前缀，www=+1+1+1')

SPELLS = [
    ('① 读出圆周率 π', '把 π 压进栈，再打印到聊天框（最简单的一个法术）',
     ['hexcasting:const/double/pi', 'hexcasting:print']),
    ('② 看自己的坐标', '取施法者 → 换成它的眼位 → 打印。泰拉坐标 Y 向下，所以 py 是「下」',
     ['hexcasting:get_caster', 'hexcasting:entity_pos/eye', 'hexcasting:print']),
    ('③ 向上跳高', '注意：驱动要的是【实体本身】，别先换成坐标 —— 换成坐标后实体就没了',
     ['hexcasting:get_caster', 'hexcasting:const/vec/ny', 'hexcasting:add_motion']),
    ('④ 朝准星方向闪现 3 格', '数字 3 要现场画一个数字字面量，再喂给闪现',
     ['hexcasting:get_caster', NUMBER3['id'], 'hexcasting:blink']),
    ('⑤ 隔空挖掉准星指的方块',
     '注意：必须取【两次】施法者：一次换眼位、一次换视线。只取一次的话，'
     '视线那步拿不到实体，后面会一路错下去，最后挖到你自己脚下那一格',
     ['hexcasting:get_caster', 'hexcasting:entity_pos/eye',
      'hexcasting:get_caster', 'hexcasting:get_entity_look',
      'hexcasting:raycast', 'hexcasting:break_block']),
]


def build_spells(by_id, out_path):
    cell = 210
    pad = 16
    header = 34
    line_h = cell + 62 + pad
    # 列数按最长的那个法术来 —— 写死 5 列的话，⑥ 个图案的法术会被裁掉
    cols = max(len(ids) for _, _, ids in SPELLS)
    width = pad + cols * (cell + pad)
    height = pad
    for _ in SPELLS:
        height += header + line_h
    img = Image.new('RGB', (width, height), (255, 255, 255))
    d = ImageDraw.Draw(img)

    y = pad
    for title, desc, ids in SPELLS:
        d.rectangle([0, y, width, y + header - 6], fill=(238, 232, 250))
        d.text((pad, y + 6), title, fill=(40, 24, 70), font=font(21))
        d.text((pad + 380, y + 11), desc, fill=(90, 80, 110), font=font(15))
        y += header

        x = pad
        for pid in ids:
            rec = by_id[pid]
            start = hexdraw.norm(dir_short(rec['dir']))
            sub = rec.get('note', '')
            img.paste(render_cell(cell, rec['sig'], start, rec['name'], sub),
                      (int(x), int(y)))
            if pid != ids[-1]:
                ax = x + cell + 2
                d.text((ax, y + cell / 2 - 12), '→', fill=(160, 150, 180), font=font(22))
            x += cell + pad
        y += line_h

    img.save(out_path)
    print('%s  %dx%d' % (out_path, width, height))


def build_gallery(by_id, out_dir, cols=4, rows=8):
    order = sorted(by_id.values(), key=lambda r: r['id'])
    per = cols * rows
    pages = (len(order) + per - 1) // per
    cell, pad, header = 240, 12, 40

    for p in range(pages):
        chunk = order[p * per:(p + 1) * per]
        w = pad + cols * (cell + pad)
        h = header + pad + rows * (cell + 62 + pad)
        img = Image.new('RGB', (w, h), (255, 255, 255))
        d = ImageDraw.Draw(img)
        d.text((pad, 8), '咒法学图案对照表（中文名 / id / 签名）  %d / %d' % (p + 1, pages),
               fill=(40, 24, 70), font=font(22))
        for i, rec in enumerate(chunk):
            r, c = divmod(i, cols)
            start = hexdraw.norm(dir_short(rec['dir']))
            sub = (rec['id'].replace('hexcasting:', '') + '  ' + start)
            img.paste(render_cell(cell, rec['sig'], start, rec['name'], sub),
                      (pad + c * (cell + pad), header + pad + r * (cell + 62 + pad)))
        path = os.path.join(out_dir, 'patterns_zh_p%d.png' % (p + 1))
        img.save(path)
        print('%s  %dx%d' % (path, w, h))


def main():
    by_id = load_patterns()
    by_id[NUMBER3['id']] = NUMBER3
    build_spells(by_id, os.path.join(HERE, 'spells_zh.png'))
    build_gallery({k: v for k, v in by_id.items() if not k.startswith('__')}, HERE)


if __name__ == '__main__':
    main()
