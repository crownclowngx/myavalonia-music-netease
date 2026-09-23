// 开发工具：执行固定上游原文件，只替换熵源；不会调用本项目 C# 实现生成“预期值”。
// 用法：node tools/generate-xeapi-vectors.cjs <crypto.js> <sha256>
const fs = require('node:fs');
const crypto = require('node:crypto');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(process.argv[2]);
const hash = crypto.createHash('sha256').update(source).digest('hex');
if (hash !== process.argv[3]) throw new Error('固定上游文件摘要不匹配');
const fixed = n => Buffer.from(Array.from({length:n}, (_,i)=>i+1));
const privateKey = crypto.createPrivateKey({ key: Buffer.concat([Buffer.from('302e020100300506032b656e04220420','hex'), fixed(32)]), format:'der', type:'pkcs8' });
const publicKey = crypto.createPublicKey(privateKey);
let entropy;
const deterministicCrypto = { ...crypto,
  randomBytes(n) { const value=entropy.shift(); if(value.length!==n) throw new Error('上游随机调用顺序变化'); return value; },
  generateKeyPairSync(type) { if(type!=='x25519') throw new Error(type); return {privateKey,publicKey}; }
};
const moduleObject = {exports:{}};
const unsupported = new Proxy({}, { get() { throw new Error('xeapi 不应使用旧协议的外部依赖'); } });
vm.runInNewContext(source.toString(), {module:moduleObject, Buffer, URL, URLSearchParams,
  require(name) { if(name==='crypto') return deterministicCrypto; if(name==='zlib') return require('node:zlib');
    if(name==='crypto-js'||name==='node-forge') return unsupported; throw new Error(name); }
});
const upstream=moduleObject.exports;
const peer={publicKey:publicKey.export({format:'der',type:'spki'}).subarray(-32).toString('base64'),version:'fixture-v1',sk:'fixture-sk'};
const inputs=[{ids:'[9007199254740991]',level:'standard',encodeType:'flac'}, {text:'中文 & + % / 😃 ~ *',e_r:'false'}];
const vectors=[];
for(const data of inputs) for(const resumed of [false,true]) {
  entropy=resumed?[fixed(16),fixed(12)]:[fixed(16),fixed(16),fixed(12)];
  const sessionKey=resumed?'session-key-1234':null, sessionId=resumed?'session-id':null;
  const expected=upstream.xeapi('/api/song/enhance/player/url/v1', data, {publicKeyState:peer,sessionKey,sessionId});
  if(entropy.length) throw new Error('未消费预期熵');
  vectors.push({data,sessionKey,sessionId,expected});
}
const cipher=crypto.createCipheriv('aes-256-ecb',Buffer.from('ab1d5a430f6bb04a3f01e81ddd72bd916d5ce591248ac128714806d7f8fb1b84','hex'),null);
const encryptedKey=Buffer.concat([cipher.update(JSON.stringify(peer)),cipher.final()]).toString('base64');
const result={revision:'a8c781fd64faab17fedfd46e0615a2609307f163',sourceSha256:hash,peer,encryptedKey,
  signature:upstream.xeapiSign('1700000000000','1234567890123456'),vectors};
fs.writeFileSync(path.join(__dirname,'../tests/MusicNetEasePlugin.Tests/Fixtures/xeapi-vectors.json'),JSON.stringify(result,null,2)+'\n');
process.stdout.write(JSON.stringify({count:vectors.length,sourceSha256:hash})+'\n');
