// 从原版 zh_cn.flatten.json5 里抽出 item.* / block.* 的官方中文名，展平成一张表。
//
// 为什么单独写：patterns 那张表已经由 lang2names.mjs 处理过（只取图案子树），
// 而物品/方块名在另外两棵子树里。这里复用同一套 JSON5 解析思路，
// 把 `"item.hexcasting"` / `"block.hexcasting"` 展平成 `item.hexcasting.staff.oak -> 橡木法杖`。
//
// 用法：node extract_lang_flat.mjs <in.json5> <out.json>
import fs from 'node:fs';

function stripComments(src) {
  let out = '';
  let i = 0;
  const n = src.length;
  while (i < n) {
    const c = src[i];
    if (c === '"' || c === "'") {
      const q = c;
      out += c; i++;
      while (i < n) {
        const d = src[i];
        out += d;
        if (d === '\\') { out += src[i + 1] ?? ''; i += 2; continue; }
        i++;
        if (d === q) break;
      }
      continue;
    }
    if (c === '/' && src[i + 1] === '/') { while (i < n && src[i] !== '\n') i++; continue; }
    if (c === '/' && src[i + 1] === '*') {
      i += 2;
      while (i < n && !(src[i] === '*' && src[i + 1] === '/')) i++;
      i += 2;
      continue;
    }
    out += c; i++;
  }
  return out;
}

function parseJson5(src) {
  let s = stripComments(src);
  s = s.replace(/([{,]\s*)([A-Za-z_$][\w$.\/-]*)\s*:/g, '$1"$2":');
  s = s.replace(/,(\s*[}\]])/g, '$1');
  return JSON.parse(s);
}

function flatten(node, prefix, acc) {
  for (const [k, v] of Object.entries(node)) {
    if (typeof v === 'string') acc[prefix + k] = v;
    else if (v && typeof v === 'object') flatten(v, prefix + k + '.', acc);
  }
  return acc;
}

const [, , inPath, outPath] = process.argv;
const parsed = parseJson5(fs.readFileSync(inPath, 'utf8'));

const out = {};
for (const root of ['item.hexcasting', 'block.hexcasting', 'itemGroup.hexcasting',
                    'key.hexcasting', 'gui.hexcasting', 'effect.hexcasting',
                    'entity.hexcasting', 'death.attack.hexcasting']) {
  if (parsed[root]) flatten(parsed[root], root + '.', out);
}
fs.writeFileSync(outPath, JSON.stringify(out, null, 2), 'utf8');
console.log(`抽出 ${Object.keys(out).length} 条 -> ${outPath}`);
