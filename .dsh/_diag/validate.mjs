#!/usr/bin/env node
/**
 * validate.mjs —— 用 harness **自己**的校验器检查 MCP 工具列表是否合法。
 *
 * 为什么需要这个：dsh-mcp-client 遵循「要么完整世代，要么没有」，
 * 只要有一个工具的 inputSchema 落在「受支持 JSON Schema 子集」之外，
 * **整批工具都注册不上**（实测 codely 原始输出 0/17 通过）。
 * 这个脚本让 `unity-mcp-proxy.mjs` 的消毒逻辑可以在本地复验，
 * 不需要 spawn 进程、不需要重启 DSH。
 *
 * 校验器**不随本仓库分发**：每次运行时从已安装的 app.asar 里现取
 * `@deepseek-ai/dsh-tools/lib/types/json-schema.js`，所以 DSH 升级后
 * 这里会自动用上新的约束（子集变了会立刻暴露）。
 *
 * 用法:
 *   node validate.mjs <toolslist.json> [app.asar 路径]
 *
 * toolslist.json 可以是完整 JSON-RPC 响应，也可以只是 tools 数组。
 * 通常是这么来的：
 *   codely serve unity-mcp --stdio  ──(喂 initialize + tools/list)──> 原始响应
 *   node unity-mcp-proxy.mjs --transform <原始响应> <消毒后响应>
 *   node validate.mjs <消毒后响应>
 */

import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const DEFAULT_ASAR = 'C:\\Users\\Rihyo\\AppData\\Local\\Programs\\DSH Desktop\\resources\\app.asar';
const TARGET = '/node_modules/@deepseek-ai/dsh-tools/lib/types/json-schema.js';

const toolsFile = process.argv[2];
const asarPath = process.argv[3] || DEFAULT_ASAR;

if (!toolsFile) {
    console.error('用法: node validate.mjs <toolslist.json> [app.asar 路径]');
    process.exit(2);
}

// ---------------------------------------------------------- 从 asar 现取校验器

function readFromAsar(asar, innerPath) {
    const fd = fs.openSync(asar, 'r');
    try {
        const head = Buffer.alloc(16);
        fs.readSync(fd, head, 0, 16, 0);
        const headerSize = head.readUInt32LE(12);       // 真 header 长度在字节 12
        const headerBuf = Buffer.alloc(headerSize);
        fs.readSync(fd, headerBuf, 0, headerSize, 16);
        const header = JSON.parse(headerBuf.toString('utf8').replace(/\0+$/, ''));
        const base = 16 + headerSize;

        let node = header.files;
        for (const seg of innerPath.split('/').filter(Boolean)) {
            if (!node || !node[seg]) return null;
            node = node[seg].files ? node[seg].files : node[seg];
        }
        if (node.offset === undefined) return null;

        const buf = Buffer.alloc(node.size);
        fs.readSync(fd, buf, 0, node.size, base + parseInt(node.offset, 10));
        return buf.toString('utf8');
    } finally {
        fs.closeSync(fd);
    }
}

const SHIM = `
export class HarnessError extends Error {
    constructor(message, code) { super(message); this.name = 'HarnessError'; this.code = code; }
}
export function assertNever(value, label) { throw new Error(\`assertNever(\${label}): \${String(value)}\`); }
export function isJsonValue(value, seen = new Set()) {
    if (value === null) return true;
    const t = typeof value;
    if (t === 'string' || t === 'boolean') return true;
    if (t === 'number') return Number.isFinite(value);
    if (t !== 'object') return false;
    if (seen.has(value)) return false;
    seen.add(value);
    try {
        if (Array.isArray(value)) return value.every((v) => isJsonValue(v, seen));
        const proto = Object.getPrototypeOf(value);
        if (proto !== null && proto !== Object.prototype) return false;
        return Object.values(value).every((v) => isJsonValue(v, seen));
    } finally { seen.delete(value); }
}
`;

async function loadValidator(asar) {
    const source = readFromAsar(asar, TARGET);
    if (!source) throw new Error(`asar 里找不到 ${TARGET}（DSH 版本变了？）`);

    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'dsh-jsonschema-'));
    const patched = source
        .replace(/from '@deepseek-ai\/dsh-llm'/g, "from './shim.mjs'")
        .replace(/from '@deepseek-ai\/dsh-util-values'/g, "from './shim.mjs'");
    if (/from '@deepseek-ai\//.test(patched)) throw new Error('还有未替换的 bare import，垫片需要更新');

    fs.writeFileSync(path.join(dir, 'shim.mjs'), SHIM, 'utf8');
    fs.writeFileSync(path.join(dir, 'json-schema.mjs'), patched, 'utf8');
    return import(pathToFileURL(path.join(dir, 'json-schema.mjs')).href);
}

// ------------------------------------------------------------------ 跑校验

let mod;
try {
    mod = await loadValidator(asarPath);
} catch (e) {
    console.error(`无法加载校验器: ${e.message}`);
    process.exit(3);
}

const raw = JSON.parse(fs.readFileSync(toolsFile, 'utf8'));
const tools = raw && raw.result && Array.isArray(raw.result.tools) ? raw.result.tools
    : Array.isArray(raw) ? raw : null;
if (!tools) { console.error('tools 数组没找到'); process.exit(2); }

let failed = 0;
for (const t of tools) {
    const problems = [];
    // 1) 整棵树必须在受支持子集内
    try { mod.assertSupportedJsonSchema(t.inputSchema); }
    catch (e) { problems.push('subset: ' + (e.violations ?? [e.message]).join(' | ')); }
    // 2) 工具入口 schema 的根必须是 type:"object"
    try { mod.assertObjectJsonSchema(t.inputSchema); }
    catch (e) { problems.push('object-root: ' + (e.violations ?? [e.message]).join(' | ')); }

    if (problems.length) {
        failed++;
        console.log(`FAIL  ${t.name}`);
        for (const p of problems) console.log(`        ${p}`);
    } else {
        console.log(`ok    ${t.name}`);
    }
}

console.log(`\n${tools.length - failed}/${tools.length} 通过`);
process.exit(failed ? 1 : 0);
