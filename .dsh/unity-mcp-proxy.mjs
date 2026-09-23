#!/usr/bin/env node
/**
 * unity-mcp-proxy —— 兼容性垫片，一个 stdio MCP 服务器。
 *
 * 为什么需要它
 * ------------
 * `codely serve unity-mcp` 输出的 inputSchema 不符合 harness 的
 * 「受支持 JSON Schema 子集」。DSH 的 @deepseek-ai/dsh-mcp-client 把工具定义
 * 直接交给 ctx.tools.register()，后者用
 * `@deepseek-ai/dsh-tools` 的 assertSupportedJsonSchema / assertObjectJsonSchema
 * 校验，而 mcp-client 又遵循「要么完整世代，要么没有」——
 * **任何一个工具不合规，全部 17 个工具都注册不上**。
 *
 * 子集的权威定义（dsh-tools/lib/types/json-schema.js）：
 *
 *   CONSTRAINT_KEYWORDS = type, oneOf, properties, required,
 *                         additionalProperties, items, enum, const
 *   ANNOTATION_KEYWORDS = description, title, default, examples
 *
 * 其余 key **一律违规**（包括 $schema、$ref、anyOf、$defs、format…）。
 * 另有几条硬规则：
 *   - 工具入口 schema 的根必须是 type:"object"（assertObjectJsonSchema）
 *   - 不认识的 key 会被**递归**检查，不只是根
 *   - required 里的名字必须出现在 properties 里
 *   - additionalProperties 必须是布尔值，不能是 schema
 *   - type 只能是单个字符串；enum/const 只能用于标量类型
 *
 * 实测（对 17 个工具跑真校验器）：**0/17 通过**。具体是
 *   - 全部 17 个都带 $schema
 *   - unity_editor 根是 anyOf（两个 object 变体）
 *   - unity_gameobject 在 properties.propertyValue 里嵌套 anyOf
 *   - 7 个工具的 additionalProperties 是 schema 而不是布尔
 *
 * codely.exe 是闭源二进制改不了，所以在中间放这一层。
 *
 * 它做什么
 * --------
 * 1. 作为 MCP stdio 服务器面对 harness（换行分隔的 JSON-RPC）
 * 2. 作为 MCP stdio 客户端拉起 `codely serve unity-mcp --stdio`
 * 3. 双向透明转发；**只在 tools/list 响应里**把每个 inputSchema 递归消毒：
 *    - 先解析本地 $ref（趁 anyOf 还在，指针路径才有效）
 *    - 根上的 anyOf（或全 object 分支的 anyOf）→ 合并成单个 object schema
 *      · properties 取并集，同名属性按 enum/const 取并集
 *        （关键：unity_editor 的 action 在分支0 是 const:"resume"、
 *          分支1 是 5 值 enum，直接后者覆盖前者会把 resume 弄丢）
 *      · required 取交集，避免把某一分支的必填变成全局必填
 *    - 非根的 anyOf → oneOf（受支持），语义从"任一"收窄成"恰一"，
 *      但服务端仍做真实校验，不影响可用性
 *    - 丢掉所有不在子集里的 key（$schema / $ref / $defs / anyOf …）
 *    - 非布尔的 additionalProperties → true（放宽，别让 harness 拦掉
 *      服务端本来接受的参数）
 *    - 认不出来的形状退化成 {}（"任意 JSON"，这是合法的 annotation-only
 *      schema），**保证永远不因为翻译失败而让工具消失**
 *
 * 语义没有损失：服务端始终自己做真实校验，schema 只是给模型的提示。
 *
 * 用法
 *   常规：由 DSH 按 cordis.patch.yml 自动拉起，不用手敲
 *   本地验证（不 spawn 任何进程，受限环境也能跑）：
 *     node unity-mcp-proxy.mjs --transform <原始 tools/list 响应.json> <输出.json>
 *     node unity-mcp-proxy.mjs --selftest  <原始 tools/list 响应.json>
 */

import { spawn } from 'node:child_process';
import readline from 'node:readline';
import fs from 'node:fs';

// 默认值刻意硬编码：这两个路径都含空格和全角冒号，靠 argv 传容易被
// 中间层（Start-Process、cmd 之类）按空格拆开。DSH 的 MCP 客户端按数组
// 传参不会拆，但少一个出错点总是好的。
const DEFAULT_CODELY = 'D:\\Tuanjie Cowork\\cli\\bin\\win32-x64\\codely.exe';
const DEFAULT_PROJECT = 'D:\\project：cultivation\\cultivation';

const log = (m) => process.stderr.write(`[unity-mcp-proxy] ${m}\n`);

// harness 支持的子集（照抄 dsh-tools/lib/types/json-schema.js）
const SCHEMA_TYPES = ['object', 'array', 'string', 'number', 'integer', 'boolean', 'null'];
const ANNOTATION_KEYWORDS = ['description', 'title', 'default', 'examples'];
/** 与 oneOf 互斥的兄弟关键字 */
const ONE_OF_SIBLINGS = ['properties', 'required', 'additionalProperties', 'items', 'enum', 'const'];

// ---------------------------------------------------------------- schema 消毒

/** 解析 schema 内部的本地 $ref（#/a/b/c），带深度保护。 */
function resolveRefs(node, root, depth = 0) {
    if (depth > 24 || node === null || typeof node !== 'object') return node;
    if (Array.isArray(node)) return node.map((n) => resolveRefs(n, root, depth + 1));

    if (typeof node.$ref === 'string' && node.$ref.startsWith('#/')) {
        const path = node.$ref.slice(2).split('/')
            .map((k) => k.replace(/~1/g, '/').replace(/~0/g, '~'));
        let target = root;
        for (const k of path) {
            if (target === null || typeof target !== 'object') { target = undefined; break; }
            target = target[k];
        }
        if (target !== undefined) {
            const { $ref, ...rest } = node;
            return resolveRefs(Object.assign({}, resolveRefs(target, root, depth + 1), rest), root, depth + 1);
        }
    }

    const out = {};
    for (const [k, v] of Object.entries(node)) out[k] = resolveRefs(v, root, depth + 1);
    return out;
}

/** 值是否匹配某个标量类型（对齐校验器的 scalarMatches）。 */
function scalarMatches(type, value) {
    switch (type) {
        case 'string': return typeof value === 'string';
        case 'number': return typeof value === 'number' && Number.isFinite(value);
        case 'integer': return typeof value === 'number' && Number.isInteger(value);
        case 'boolean': return typeof value === 'boolean';
        case 'null': return value === null;
        default: return false;
    }
}

/** 取属性上的枚举值（enum 或 const），没有则 null。 */
function enumValues(prop) {
    if (!prop || typeof prop !== 'object') return null;
    if (prop.const !== undefined) return [prop.const];
    if (Array.isArray(prop.enum)) return prop.enum.slice();
    return null;
}

/** 合并同名属性：enum/const 取并集，其余后者覆盖前者。 */
function mergeProp(a, b) {
    if (!a) return b;
    if (!b) return a;
    const out = Object.assign({}, a, b);
    const va = enumValues(a);
    const vb = enumValues(b);
    if (va && vb) {
        out.enum = [...new Set([...va, ...vb])];
        delete out.const;
    } else if (va && !vb) {
        out.enum = va;
        delete out.const;
    } else if (!va && vb) {
        out.enum = vb;
        delete out.const;
    }
    return out;
}

function isPlainObject(v) {
    return v !== null && typeof v === 'object' && !Array.isArray(v);
}

/** 把若干 object 分支合并成一个 object schema（保留 enum 并集 / required 交集）。 */
function mergeBranches(branches, depth) {
    if (depth > 32) return null;
    const objs = branches.filter(isPlainObject);
    if (!objs.length) return null;

    const properties = {};
    let required = null;
    let additional;

    for (const b of objs) {
        const rb = resolveRefs(b, b);
        const bp = isPlainObject(rb.properties) ? rb.properties : {};
        for (const [k, v] of Object.entries(bp)) properties[k] = mergeProp(properties[k], v);

        const r = Array.isArray(rb.required) ? rb.required : [];
        required = required === null ? r.slice() : required.filter((x) => r.includes(x));

        if (additional === undefined) additional = rb.additionalProperties;
        else if (additional !== rb.additionalProperties) additional = null;   // 分支有分歧就不带
    }

    const out = { type: 'object', properties };
    if (required && required.length) out.required = required;
    if (typeof additional === 'boolean') out.additionalProperties = additional;
    return out;
}

/**
 * 把一个原始 schema 递归消毒成受支持子集内的 schema。
 * 认不出来时退化（而不是抛错），确保工具不会整体消失。
 * @param raw - 原始 schema
 * @param isRoot - 工具入口 schema 的根必须落成 type:"object"
 */
function sanitizeSchema(raw, isRoot, depth = 0) {
    const fallback = isRoot ? { type: 'object', properties: {} } : {};
    if (depth > 32 || !isPlainObject(raw)) return fallback;

    const node = resolveRefs(raw, raw);

    // 1) annotation 先抄（都是子集允许的）
    const out = {};
    for (const k of ANNOTATION_KEYWORDS) {
        if (Object.hasOwn(node, k)) out[k] = node[k];
    }

    // 2) oneOf（受支持）
    if (Array.isArray(node.oneOf) && node.oneOf.length >= 2) {
        const noSiblings = ONE_OF_SIBLINGS.every((k) => !Object.hasOwn(node, k));
        if (isRoot && !Object.hasOwn(node, 'type')) {
            const merged = mergeBranches(node.oneOf, depth);
            if (merged) return sanitizeSchema(merged, true, depth + 1);
            return fallback;
        }
        if (noSiblings) {
            const list = node.oneOf.map((b) => sanitizeSchema(b, false, depth + 1));
            if (list.length >= 2) { out.oneOf = list; return out; }
        }
    }

    // 3) anyOf（不支持）
    if (Array.isArray(node.anyOf) && node.anyOf.length) {
        const allObjects = node.anyOf.every((b) => isPlainObject(b) && b.type === 'object');
        if (isRoot || allObjects) {
            const merged = mergeBranches(node.anyOf, depth);
            if (merged) return sanitizeSchema(merged, isRoot, depth + 1);
        }
        // 非根：anyOf → oneOf（受支持）
        const noSiblings = ONE_OF_SIBLINGS.every((k) => !Object.hasOwn(node, k));
        if (!isRoot && noSiblings && node.anyOf.length >= 2) {
            const list = node.anyOf.map((b) => sanitizeSchema(b, false, depth + 1));
            if (list.length >= 2) { out.oneOf = list; return out; }
        }
        // 兜底：拿第一个分支
        const first = sanitizeSchema(node.anyOf[0], isRoot, depth + 1);
        return Object.keys(first).length ? Object.assign({}, out, first) : (isRoot ? fallback : out);
    }

    // 4) type
    const t = node.type;
    if (typeof t === 'string' && SCHEMA_TYPES.includes(t)) {
        out.type = t;

        if (t === 'object') {
            const props = {};
            const src = isPlainObject(node.properties) ? node.properties : {};
            for (const [k, v] of Object.entries(src)) props[k] = sanitizeSchema(v, false, depth + 1);
            if (Object.keys(props).length) out.properties = props;

            if (Array.isArray(node.required)) {
                // required 里的名字必须真的在 properties 里，否则校验器会报违规
                const req = node.required.filter((x) => typeof x === 'string' && Object.hasOwn(props, x));
                if (req.length) out.required = req;
            }
            if (Object.hasOwn(node, 'additionalProperties')) {
                // 必须是布尔；是 schema 就放宽成 true（服务端仍会真实校验）
                out.additionalProperties = typeof node.additionalProperties === 'boolean'
                    ? node.additionalProperties : true;
            }
            return out;
        }

        if (t === 'array') {
            if (Object.hasOwn(node, 'items')) out.items = sanitizeSchema(node.items, false, depth + 1);
            return out;
        }

        // 标量：enum/const 只能用在标量上，且取值必须与 type 相符
        if (Array.isArray(node.enum) && node.enum.length && node.enum.every((e) => scalarMatches(t, e))) {
            out.enum = node.enum.slice();
        } else if (Object.hasOwn(node, 'const') && scalarMatches(t, node.const)) {
            out.const = node.const;
        }
        return out;
    }

    // 5) 没有 type：annotation-only 也算合法（表示"任意 JSON"）
    return isRoot ? fallback : out;
}

/** 只改 tools/list 响应，其它消息一律不碰。 */
function rewrite(msg) {
    const tools = msg && msg.result && msg.result.tools;
    if (!Array.isArray(tools)) return msg;
    let patched = 0;
    for (const tool of tools) {
        if (!isPlainObject(tool)) continue;
        const before = tool.inputSchema;
        const after = sanitizeSchema(before, true);
        if (JSON.stringify(before) !== JSON.stringify(after)) patched++;
        tool.inputSchema = after;
    }
    if (patched) log(`sanitized ${patched}/${tools.length} tool inputSchema(s)`);
    return msg;
}

// ---------------------------------------------------------------- 本地验证模式

const argv = process.argv.slice(2);

function loadResponse(file) {
    const raw = JSON.parse(fs.readFileSync(file, 'utf8'));
    const tools = raw && raw.result && Array.isArray(raw.result.tools) ? raw.result.tools : raw;
    if (!Array.isArray(tools)) { log('no tools array found'); process.exit(2); }
    return { raw, tools };
}

if (argv[0] === '--transform' || argv[0] === '--selftest') {
    const { raw, tools } = loadResponse(argv[1]);
    const transformed = tools.map((t) => Object.assign({}, t, { inputSchema: sanitizeSchema(t.inputSchema, true) }));

    if (argv[0] === '--transform') {
        const outDoc = (raw && raw.result) ? Object.assign({}, raw, { result: Object.assign({}, raw.result, { tools: transformed }) })
            : transformed;
        fs.writeFileSync(argv[2], JSON.stringify(outDoc), 'utf8');
        console.log(`wrote ${transformed.length} tools to ${argv[2]}`);
    } else {
        console.log(`tool count: ${transformed.length}`);
        for (const t of transformed) {
            console.log(`  ${isPlainObject(t.inputSchema) && t.inputSchema.type === 'object' ? 'ok  ' : 'BAD '} ${t.name}`);
        }
    }
    process.exit(0);
}

// ---------------------------------------------------------------- 进程接线

const CODELY = argv[0] || DEFAULT_CODELY;
const PROJECT = argv[1] || DEFAULT_PROJECT;

log(`spawning: ${CODELY} serve unity-mcp --stdio --unity-project-path ${PROJECT}`);

const child = spawn(
    CODELY,
    ['serve', 'unity-mcp', '--stdio', '--unity-project-path', PROJECT],
    { stdio: ['pipe', 'pipe', 'inherit'], windowsHide: true },
);

child.on('error', (e) => { log(`failed to spawn codely: ${e.message}`); process.exit(3); });
child.on('exit', (code, sig) => { log(`codely exited (code=${code} signal=${sig})`); process.exit(code === null ? 0 : code); });

// 上游 → harness（唯一改写点）
readline.createInterface({ input: child.stdout }).on('line', (line) => {
    const t = line.trim();
    if (!t) return;
    let msg;
    try { msg = JSON.parse(t); } catch { process.stdout.write(line + '\n'); return; }
    process.stdout.write(JSON.stringify(rewrite(msg)) + '\n');
});

// harness → 上游
const rl = readline.createInterface({ input: process.stdin });
rl.on('line', (line) => {
    const t = line.trim();
    if (!t) return;
    if (!child.stdin.writable) { log('upstream stdin closed, dropping message'); return; }
    child.stdin.write(t + '\n');
});
rl.on('close', () => { try { child.stdin.end(); } catch { /* already gone */ } });

for (const sig of ['SIGINT', 'SIGTERM']) {
    process.on(sig, () => { try { child.kill(); } catch { /* already gone */ } process.exit(0); });
}
