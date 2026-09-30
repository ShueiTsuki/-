# -*- coding: utf-8 -*-
"""像素级复现 HexCanvas.DrawMove：模拟鼠标沿图案拖拽，看最终识别的角度串。

为什么要这样查：菱形（qaq / 意识之精思）是**闭合图形** —— 最后一段要回到起点，
而它相对锚点的距离**恰好等于一个格距**，正好压在吸附阈值上。
只要鼠标差一点点没回到起点，这一段就记不上，签名变成 "qa"。
"""
import math
import sys

sys.path.insert(0, r'D:\DeepSeekHarness\图案工具')
import draw as hexdraw  # noqa: E402

ORD = hexdraw.ORD            # NE E SE SW W NW
DELTA = hexdraw.DELTA
FULL = {'NorthEast': 'NE', 'East': 'E', 'SouthEast': 'SE',
        'SouthWest': 'SW', 'West': 'W', 'NorthWest': 'NW'}
CHAR = {0: 'w', 1: 'e', 2: 'd', 3: 's', 4: 'a', 5: 'q'}


def coord_to_px(c, size):
    s3 = math.sqrt(3)
    return (s3 * c[0] + s3 / 2 * c[1]) * size, 1.5 * c[1] * size


def snap_dir(dx, dy, snap_sq):
    """忠实照抄 HexCanvas.DrawMove 的吸附部分。"""
    if dx * dx + dy * dy < snap_sq:
        return None
    angle = math.atan2(dy, dx)
    turns = angle / (math.tau) * 6.0
    return (int(round(turns)) + 1) % 6


def simulate(sig, start, size, threshold, steps_per_seg=24, shortfall_px=0.0):
    """沿图案折线拖动鼠标，返回 DrawMove 状态机得出的签名。"""
    pts = hexdraw.path_pts(sig, FULL[start])   # 格点坐标序列
    snap_sq = size * size * 2.0 * max(0.5, min(1.0, threshold))

    # 鼠标轨迹：每段线性插值
    path = []
    for i in range(len(pts) - 1):
        ax, ay = coord_to_px(pts[i], size)
        bx, by = coord_to_px(pts[i + 1], size)
        for k in range(steps_per_seg):
            t = k / steps_per_seg
            path.append((ax + (bx - ax) * t, ay + (by - ay) * t))
    # 最后一段的终点（可人为「差一点没到」，模拟松手偏早）
    lx, ly = coord_to_px(pts[-1], size)
    px, py = coord_to_px(pts[-2], size)
    d = math.hypot(lx - px, ly - py)
    if d > 0:
        lx -= (lx - px) / d * shortfall_px
        ly -= (ly - py) / d * shortfall_px
    path.append((lx, ly))

    # ---- DrawMove 状态机 ----
    start_coord = (0, 0)
    anchor = start_coord
    angles = []
    compass = None
    state = 'Between'
    for (mx, my) in path:
        if state == 'Between':
            state = 'JustStarted'
            anchor = start_coord
            continue
        ax, ay = coord_to_px(anchor, size)
        nd = snap_dir(mx - ax, my - ay, snap_sq)
        if nd is None:
            continue
        if state == 'JustStarted':
            compass = nd
            anchor = (anchor[0] + DELTA[ORD[nd]][0], anchor[1] + DELTA[ORD[nd]][1])
            state = 'Drawing'
            continue
        # 回溯判定
        if nd == (compass + 3) % 6:
            if not angles:
                state = 'JustStarted'
                anchor = (anchor[0] + DELTA[ORD[nd]][0], anchor[1] + DELTA[ORD[nd]][1])
                compass = nd
            else:
                anchor = (anchor[0] + DELTA[ORD[nd]][0], anchor[1] + DELTA[ORD[nd]][1])
                angles.pop()
                compass = nd
            continue
        next_angle = (nd - compass) % 6
        angles.append(CHAR[next_angle])
        compass = nd
        anchor = (anchor[0] + DELTA[ORD[nd]][0], anchor[1] + DELTA[ORD[nd]][1])

    return ''.join(angles)


def main():
    size = math.sqrt(1920 * 1080 / 512.0)     # 1920x1080 下的实际格距 ≈ 63.6 px
    print('格距 size = %.1f px\n' % size)
    cases = [('qaq', 'NorthEast', '意识之精思（菱形·闭合）'),
             ('de', 'NorthEast', '揭示'),
             ('qdwdq', 'NorthEast', '弧之精思'),
             ('aa', 'East', '指南针之纯化')]
    for threshold in (0.5, 1.0):
        print('===== 阈值 %.1f =====' % threshold)
        for sig, start, name in cases:
            got = simulate(sig, start, size, threshold)
            ok = 'OK ' if got == sig else 'BAD'
            print('  %s %-24s 期望 %-8s 实得 %s' % (ok, name, sig, got or '(空)'))
        print()

    print('===== 阈值 0.5，鼠标差一点没回到起点 =====')
    for short in (0, 1, 2, 4, 8, 16):
        got = simulate('qaq', 'NorthEast', size, 0.5, shortfall_px=short)
        print('  差 %2d px → 实得 %s' % (short, got or '(空)'))


if __name__ == '__main__':
    main()
