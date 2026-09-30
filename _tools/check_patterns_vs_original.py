"""逐条对拍：模组注册的 188 个图案 vs 原版源码里的形状（角度串 + 起笔方向）。

为什么要常驻：书里画的、画布识别的都是这张表，形状一错（例如交换和轮换画混了），
图案就会执行成别的操作，而所有离线测试照样全绿（测试也是从同一张表取图案）。

原版：hexsrc/Common/src/main/java 下所有 make("名字", … HexPattern.fromAngles("角度", HexDir.方向))
模组：HexCastingTerraria/Core/Registry/GeneratedPatternData.cs
退出码 0 = 完全一致。
"""
import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(os.path.dirname(ROOT), 'hexsrc', 'Common', 'src', 'main', 'java')
GEN = os.path.join(ROOT, 'HexCastingTerraria', 'Core', 'Registry', 'GeneratedPatternData.cs')
DIRS = {'NORTH_EAST': 'NorthEast', 'EAST': 'East', 'SOUTH_EAST': 'SouthEast',
        'SOUTH_WEST': 'SouthWest', 'WEST': 'West', 'NORTH_WEST': 'NorthWest'}

text = ''.join(open(f, encoding='utf-8').read()
               for f in glob.glob(os.path.join(SRC, '**', '*.java'), recursive=True)
               + glob.glob(os.path.join(SRC, '**', '*.kt'), recursive=True))
orig = {}
for m in re.finditer(r'make\("([^"]+)",[^;]*?fromAngles\(\s*"([a-z]*)",\s*HexDir\.(\w+)\)', text, re.S):
    orig.setdefault('hexcasting:' + m.group(1), (m.group(2), DIRS[m.group(3)]))

gen = open(GEN, encoding='utf-8').read()
port = {m.group(1): (m.group(2), m.group(3))
        for m in re.finditer(r'new PatternData\("([^"]+)",\s*"([a-z]*)",\s*HexDir\.(\w+)', gen)}

problems = []
for k, v in orig.items():
    if k not in port:
        problems.append(f'模组缺少 {k}')
    elif port[k] != v:
        problems.append(f'{k}: 原版 {v[1]} {v[0]}，模组 {port[k][1]} {port[k][0]}')
problems += [f'原版没有 {k}' for k in port if k not in orig]

print(f'原版 {len(orig)} 个 / 模组 {len(port)} 个 / 不一致 {len(problems)}')
for p in problems:
    print('  ' + p)
sys.exit(1 if problems or len(orig) < 180 else 0)
