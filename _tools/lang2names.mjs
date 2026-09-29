// 把 HexMod 的 lang/*.flatten.json5 抽成扁平表：{ "hexcasting:get_caster": "意识之精思", ... }
//
// 为什么要自己解析 JSON5：原版语言文件是 JSON5（裸键、注释、尾逗号），
// PowerShell 的 ConvertFrom-Json 直接报 "Invalid JSON primitive"。
//
// 用法：node lang2names.mjs <in.json5> <out.json>
import fs from 'node:fs';

/** 去掉 // 与 /* *\/ 注释，但不动字符串里的内容。 */
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
  // 裸键 -> 带引号。只在 { 或 , 之后匹配，避免误伤字符串内容。
  s = s.replace(/([{,]\s*)([A-Za-z_$][\w$.\/-]*)\s*:/g, '$1"$2":');
  // 尾逗号
  s = s.replace(/,(\s*[}\]])/g, '$1');
  return JSON.parse(s);
}

/** 找到含有 get_caster 的那棵子树（就是图案名表）。 */
function findActionNode(obj, depth = 0) {
  if (!obj || typeof obj !== 'object' || depth > 8) return null;
  if (Object.prototype.hasOwnProperty.call(obj, 'get_caster')) return obj;
  for (const v of Object.values(obj)) {
    const hit = findActionNode(v, depth + 1);
    if (hit) return hit;
  }
  return null;
}

/** 键以 / 结尾的是命名空间段，其余是叶子。 */
function flatten(node, prefix, acc) {
  for (const [k, v] of Object.entries(node)) {
    if (typeof v === 'string') {
      acc[prefix + k] = v;
    } else if (v && typeof v === 'object') {
      flatten(v, prefix + k, acc);
    }
  }
  return acc;
}

const [, , inPath, outPath] = process.argv;
if (!inPath || !outPath) {
  console.error('usage: node lang2names.mjs <in.json5> <out.json>');
  process.exit(2);
}

const parsed = parseJson5(fs.readFileSync(inPath, 'utf8'));
const node = findActionNode(parsed);
if (!node) {
  console.error('找不到图案名表（没有 get_caster 键）');
  process.exit(1);
}

const flat = flatten(node, '', {});
const out = {};
for (const [k, v] of Object.entries(flat)) out['hexcasting:' + k] = v;
out._node_keys = undefined;
delete out._node_keys;

fs.writeFileSync(outPath, JSON.stringify(out, null, 2), 'utf8');
console.log(`图案名 ${Object.keys(out).length} 条 -> ${outPath}`);
