# 读取咒法学的 *.flatten.json5 语言文件，展平成 {"a.b.c": "文本"}。
#
# 格式（HexMod 自己的约定）：嵌套对象的键用 "." 连接；键 "" 表示父键本身。
# json5 的子集：未加引号的键、尾逗号、// 与 /* */ 注释、双/单引号字符串。
# 不引入第三方库 —— 只需要这么多。
import sys


class _P:
    def __init__(self, s):
        self.s, self.i = s, 0

    def ws(self):
        s = self.s
        while self.i < len(s):
            c = s[self.i]
            if c in ' \t\r\n﻿':
                self.i += 1
            elif s.startswith('//', self.i):
                j = s.find('\n', self.i)
                self.i = len(s) if j < 0 else j
            elif s.startswith('/*', self.i):
                self.i = s.index('*/', self.i) + 2
            else:
                break

    def string(self):
        q = self.s[self.i]
        self.i += 1
        out = []
        while True:
            c = self.s[self.i]
            if c == q:
                self.i += 1
                return ''.join(out)
            if c == '\\':
                n = self.s[self.i + 1]
                self.i += 2
                if n == 'n':
                    out.append('\n')
                elif n == 't':
                    out.append('\t')
                elif n == 'u':
                    out.append(chr(int(self.s[self.i:self.i + 4], 16)))
                    self.i += 4
                elif n == '\n':
                    pass
                else:
                    out.append(n)
            else:
                out.append(c)
                self.i += 1

    def key(self):
        if self.s[self.i] in '"\'':
            return self.string()
        j = self.i
        while self.s[j] not in ':':
            j += 1
        k = self.s[self.i:j].strip()
        self.i = j
        return k

    def value(self):
        self.ws()
        c = self.s[self.i]
        if c == '{':
            self.i += 1
            obj = {}
            while True:
                self.ws()
                if self.s[self.i] == '}':
                    self.i += 1
                    return obj
                k = self.key()
                self.ws()
                assert self.s[self.i] == ':', self.s[self.i - 20:self.i + 20]
                self.i += 1
                obj[k] = self.value()
                self.ws()
                if self.s[self.i] == ',':
                    self.i += 1
        if c in '"\'':
            return self.string()
        j = self.i
        while self.s[j] not in ',}\n':
            j += 1
        tok = self.s[self.i:j].strip()
        self.i = j
        return tok


def _join(prefix, k):
    # HexMod 的约定：以 ':' 或 '/' 结尾的键段直接拼接，不加 '.'（如 hexcasting: + add）
    if not prefix:
        return k
    return prefix + k if prefix.endswith((':', '/')) else f'{prefix}.{k}'


def load(path):
    with open(path, encoding='utf-8') as f:
        root = _P(f.read()).value()
    flat = {}

    def walk(prefix, v):
        if isinstance(v, dict):
            for k, sub in v.items():
                walk(prefix if k == '' else _join(prefix, k), sub)
        else:
            flat[prefix] = v

    walk('', root)
    return flat


if __name__ == '__main__':
    d = load(sys.argv[1])
    print(len(d))
    for k in sys.argv[2:]:
        print(k, '=>', d.get(k))
