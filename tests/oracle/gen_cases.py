"""生成和原版对拍的用例 → tests/oracle/cases/basic.json。

图案的笔顺一律取自原版运行时导出的注册表（golden/registry.json，python original.py dump），不用移植版自己的表。
三类用例：
  1. 逐个图案 × 一组初始栈：每个非法术、非本世界的图案，在几十种栈上各执行一次（类型对 / 类型错 / 参数不够 / 边界值）；
  2. 数字和簿记员：按原版特殊处理器的规则拼出各种笔顺；
  3. 多步程序：内省 / 反思 / 考虑、赫尔墨斯 / 伊里斯 / 托特、渡鸦之思、括号嵌套等。
法术（会改世界）和本世界图案（卓越法术）先不放：它们要和移植版的假世界对齐以后再做。
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
REG = {a['id']: a for a in json.load(open(os.path.join(HERE, 'golden', 'registry.json'), encoding='utf-8'))['actions']}

DIRS = ['NORTH_EAST', 'EAST', 'SOUTH_EAST', 'SOUTH_WEST', 'WEST', 'NORTH_WEST']
TURN = 'wedsaq'  # 相对上一笔向右转 0~5 格


def N(v):
    return {'t': 'num', 'v': v}


def V(x, y, z):
    return {'t': 'vec', 'v': [x, y, z]}


def B(b):
    return {'t': 'bool', 'v': b}


def L(*xs):
    return {'t': 'list', 'v': list(xs)}


NUL = {'t': 'null'}
GAR = {'t': 'garbage'}


def P(action_id):
    a = REG['hexcasting:' + action_id]
    return {'t': 'pat', 'dir': a['dir'], 'angles': a['angles']}


def raw(direction, angles):
    return {'t': 'pat', 'dir': direction, 'angles': angles}


def from_dirs(dirs):
    """方向序列 → (起始方向, 笔顺)。"""
    angles = ''.join(TURN[(DIRS.index(b) - DIRS.index(a)) % 6] for a, b in zip(dirs, dirs[1:]))
    return dirs[0], angles


def mask(spec):
    """簿记员：'-' 保留（顺着东走一笔），'v' 丢弃（先右下再右上两笔），照原版 SpecialHandlerMask。"""
    dirs = []
    for ch in spec:
        dirs += ['EAST'] if ch == '-' else ['SOUTH_EAST', 'NORTH_EAST']
    d, a = from_dirs(dirs)
    return raw(d, a)


ADD, DUP = P('add'), P('duplicate')

STACKS = [
    [], [N(0)], [N(1)], [N(-2.5)], [N(0.5)], [N(16)], [N(65535)], [N(-1)],
    [N(3), N(2)], [N(2), N(0)], [N(0), N(0)], [N(-1), N(0.5)], [N(7), N(-3)], [N(2), N(2)], [N(1.00001), N(1)],
    [N(16), N(4)], [N(2), N(10)], [N(12), N(10)], [N(1e300), N(1e300)], [N(-8), N(1 / 3)],
    [N(1), N(2), N(3)], [N(3), N(2), N(1)], [N(1), N(2), N(3), N(4)], [N(1), N(2), N(3), N(4), N(5), N(6)],
    [V(1, 2, 3)], [V(0, 0, 0)], [V(3, 4, 0)], [V(1, 2, 3), V(4, -5, 6)], [V(1, 2, 3), V(1, 2, 3)], [V(0, 0, 0), V(0, 0, 0)],
    [V(1, 2, 3), N(2)], [N(2), V(1, 2, 3)], [V(1, 2, 3), N(0)], [V(1, 2, 3), B(True)],
    [B(True)], [B(False)], [B(True), B(False)], [B(True), B(True)], [B(False), B(False)], [N(1), B(True)], [B(True), N(1)],
    [NUL], [GAR], [NUL, NUL], [NUL, N(1)],
    [L()], [L(N(1), N(2), N(3))], [L(N(1), N(2), N(3)), N(1)], [L(N(1), N(2), N(3)), N(5)], [L(N(1), N(2), N(3)), N(-1)],
    [L(N(1), N(2), N(3)), N(1.5)], [L(N(1), N(2), N(3)), N(1), N(9)], [L(N(1), N(2), N(3)), N(0), N(2)],
    [L(N(1), N(2)), L(N(3))], [L(N(1), N(2)), L(N(2), N(3))], [N(3), L(N(1), N(2))], [L(N(1), L(N(2), N(3)))],
    [L(B(True), B(False))], [L(ADD)], [N(1), N(2), L(ADD)], [L(DUP), L(N(1), N(2))], [ADD], [ADD, ADD], [L(ADD, DUP)],
    [N(1), N(2000)], [N(1), N(1e10)],
]


def matrix_cases():
    cases = []
    for aid, a in sorted(REG.items()):
        if a['spell'] or a['perWorld']:
            continue
        name = aid.split(':', 1)[1]
        for k, stack in enumerate(STACKS):
            cases.append({'id': f'{name}#{k}', 'action': aid, 'stack': stack, 'program': [P(name)]})
    return cases


def special_cases():
    cases = []
    for sig in ['aqaa', 'aqaaw', 'aqaaq', 'aqaae', 'aqaawa', 'aqaawd', 'aqaaeqw', 'aqaawaawaa', 'aqaaqqqq', 'aqaawdd',
                'dedd', 'deddw', 'deddwd', 'deddeqw', 'deddq']:
        cases.append({'id': f'number:{sig}', 'action': 'number', 'stack': [], 'program': [raw('EAST', sig)]})
    stacks = [[N(1), N(2), N(3), N(4)], [N(1)], [], [V(1, 2, 3), B(True), N(5)]]
    for spec in ['-', 'v', '--', '-v', 'v-', 'vv', '-v-', '--v', 'v--', 'v-v', 'vvv-']:
        for k, stack in enumerate(stacks):
            cases.append({'id': f'mask:{spec}#{k}', 'action': 'mask', 'stack': stack, 'program': [mask(spec)]})
    return cases


def program_cases():
    intro, retro, consider = P('open_paren'), P('close_paren'), P('escape')
    evl, evlcc, thoth, halt = P('eval'), P('eval/cc'), P('for_each'), P('halt')
    rd, wr = P('read/local'), P('write/local')
    progs = {
        'intro-add-retro': ([], [intro, ADD, retro]),
        'intro-nested': ([], [intro, intro, ADD, retro, retro]),
        'intro-unclosed': ([N(1)], [intro, ADD]),
        'retro-alone': ([N(1)], [retro]),
        'consider-add': ([], [consider, ADD]),
        'consider-consider': ([], [consider, consider]),
        'consider-in-parens': ([], [intro, consider, retro, retro]),
        'intro-escape-number': ([], [intro, raw('EAST', 'aqaaw'), retro]),
        'hermes-add': ([N(1), N(2), L(ADD)], [evl]),
        'hermes-number': ([N(4)], [intro, raw('EAST', 'aqaaw'), ADD, retro, evl]),
        'hermes-empty': ([L()], [evl]),
        'hermes-not-list': ([N(1)], [evl]),
        'hermes-halt': ([N(1), L(ADD, halt, ADD), N(2)], [evl]),
        'hermes-nested': ([N(1), N(2), N(3), L(ADD, L(ADD), evl)], [evl]),
        'iris': ([N(1), N(2), L(evlcc, ADD)], [evl]),
        'iris-top': ([N(1), L(ADD)], [evlcc]),
        'thoth-dup': ([L(DUP), L(N(1), N(2), N(3))], [thoth]),
        'thoth-add-empty': ([L(ADD), L()], [thoth]),
        'thoth-halt': ([L(halt), L(N(1), N(2))], [thoth]),
        'halt-top': ([N(1), N(2)], [halt]),
        'raven-write-read': ([N(42)], [wr, rd]),
        'raven-read-empty': ([], [rd]),
        'raven-keeps-stack': ([N(1), N(2)], [wr, ADD]),
        'number-then-add': ([], [raw('EAST', 'aqaaw'), raw('EAST', 'aqaaq'), ADD]),
        'invalid-pattern': ([], [raw('EAST', 'qqqqq')]),
        'plain-iota-unescaped': ([], [N(1)]),
    }
    return [{'id': 'prog:' + k, 'action': None, 'stack': s, 'program': p} for k, (s, p) in progs.items()]


def main():
    cases = matrix_cases() + special_cases() + program_cases()
    out = os.path.join(HERE, 'cases', 'basic.json')   # 不进仓库：随时由本脚本重新生成
    os.makedirs(os.path.dirname(out), exist_ok=True)
    json.dump({'version': 1, 'cases': cases}, open(out, 'w', encoding='utf-8'), ensure_ascii=False, separators=(',', ':'))
    print(len(cases), '个用例 ->', out)


if __name__ == '__main__':
    main()
