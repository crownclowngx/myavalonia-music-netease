// 开发工具：调用固定提交的原始 crypto.js 生成协议向量；应用和默认单测均不依赖 Node。
// 先按 docs/reference/netease-http-session.md 中的方法将上游放入 artifacts/upstream-reference。
const path = require('node:path');
const fs = require('node:fs');
const crypto = require('node:crypto');
const base = path.resolve(__dirname, '../artifacts/upstream-reference');
const upstream = require(path.join(base, 'crypto.js'));
const source = fs.readFileSync(path.join(base, 'crypto.js'));
const expectedSha256 = process.argv[2];
const sha256 = crypto.createHash('sha256').update(source).digest('hex');
if (!expectedSha256 || sha256 !== expectedSha256) throw new Error('上游文件摘要不匹配或未提供摘要');
const inputs = [
  { type: 3, e_r: false },
  { key: 'fixture-key', type: 3, e_r: false },
  { text: '中文 & + % / 😃', enabled: true, optional: null, id: 9007199254740991 },
  { e_r: false, csrf_token: '' }
];
const previousRandom = Math.random;
// 原实现通过 round(random * 61) 选取字符，令每个位置稳定落在索引 0..15。
let index = 0;
Math.random = () => (index++ % 16) / 61;
try {
  const vectors = inputs.map(input => {
    index = 0;
    return { json: JSON.stringify(input), path: '/api/login/qrcode/unikey',
      secret: 'abcdefghijklmnop', eapi: upstream.eapi('/api/login/qrcode/unikey', input), weapi: upstream.weapi(input) };
  });
  const result = { revision: 'a8c781fd64faab17fedfd46e0615a2609307f163', sourceSha256: sha256, vectors };
  const destination = path.resolve(__dirname, '../tests/MusicNetEasePlugin.Tests/Fixtures/protocol-vectors.json');
  fs.mkdirSync(path.dirname(destination), { recursive: true });
  fs.writeFileSync(destination, JSON.stringify(result, null, 2) + '\n');
  process.stdout.write(JSON.stringify({ count: vectors.length, sourceSha256: sha256 }) + '\n');
} finally { Math.random = previousRandom; }
