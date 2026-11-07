'use strict';
/**
 * TML Extension API v1 / Node.js SDK (CommonJS)
 *
 * 只往 stdout 写协议消息；console.log 会自动改道 stderr，避免污染协议流。
 * SDK 只封装宿主开放的白名单方法，不提供文件 / 网络 / 子进程 API。
 *
 * 用法：
 *   const { Ext } = require('./tml_ext');
 *   class MyExt extends Ext {
 *     onEvent(type, data) { this.log('info', 'got ' + type); }
 *   }
 *   new MyExt({ name: 'Hello', version: '1.0.0' }).run();
 */
const readline = require('readline');

class ExtError extends Error {}
class PermissionDenied extends ExtError {}

// 作者的 console.log 改道 stderr，stdout 只留给协议
const _rawOut = process.stdout.write.bind(process.stdout);
for (const key of ['log', 'info', 'debug', 'warn']) {
  console[key] = (...args) => console.error(...args);
}

class Ext {
  constructor(options) {
    options = options || {};
    this.name = options.name || 'node-ext';
    this.version = options.version || '1.0.0';
    this.runtime = 'node';
    this.extId = '';
    this.platform = '';
    this.launcherVersion = '';
    this.apiVersion = 1;
    this.permissions = [];
    this.privateDir = '';
    this.nonce = '';

    this._nextId = 2;
    this._pending = new Map();
    this._ready = false;
    this._stopped = false;
  }

  _send(obj) {
    const line = JSON.stringify(obj);
    if (Buffer.byteLength(line, 'utf8') > 65536) return;
    try {
      _rawOut(line + '\n');
    } catch (_) {
      this._stopped = true;
    }
  }

  _reply(id, result, error) {
    const body = { id };
    if (error) body.error = error;
    else body.result = result || {};
    this._send(body);
  }

  _onInitialize(params) {
    params = params || {};
    this.apiVersion = params.apiVersion || 1;
    this.extId = params.extId || '';
    this.platform = params.platform || '';
    this.launcherVersion = params.launcherVersion || '';
    this.permissions = Array.isArray(params.permissions) ? params.permissions.slice() : [];
    this.privateDir = params.privateDir || '';
    this.nonce = params.nonce || '';
    this._ready = true;
  }

  _handle(line) {
    if (!line || Buffer.byteLength(line, 'utf8') > 65536) return;
    let msg;
    try { msg = JSON.parse(line); } catch (_) { return; }
    if (!msg || typeof msg !== 'object' || Array.isArray(msg)) return;

    if (typeof msg.method === 'string') {
      if (msg.method === 'initialize') {
        this._onInitialize(msg.params);
        this._reply(msg.id, {
          name: this.name, version: this.version, runtime: this.runtime, ready: true
        });
        Promise.resolve().then(() => this.onStart()).catch((e) => {
          this.log('error', 'onStart failed: ' + e.message).catch(() => {});
        });
      } else if (msg.method === 'event') {
        const p = msg.params || {};
        Promise.resolve().then(() => this.onEvent(String(p.type || ''), p.data || {}))
          .catch((e) => this.log('error', 'onEvent failed: ' + e.message).catch(() => {}));
      } else if (msg.method === 'shutdown') {
        this.stop();
      } else if (msg.id !== undefined) {
        this._reply(msg.id, null, { code: -32601, message: 'method not allowed for extensions' });
      }
      return;
    }

    if (msg.id !== undefined) {
      const slot = this._pending.get(msg.id);
      if (!slot) return;
      this._pending.delete(msg.id);
      clearTimeout(slot.timer);
      if (msg.error) {
        const err = new ExtError(String(msg.error.message || 'host error'));
        if (msg.error.code === -32001 || msg.error.code === -32601) {
          slot.reject(new PermissionDenied(err.message));
        } else {
          slot.reject(err);
        }
      } else {
        slot.resolve(msg.result || {});
      }
    }
  }

  call(method, params, timeoutMs) {
    if (this._stopped) return Promise.reject(new ExtError('extension stopped'));
    const id = this._nextId++;
    const timeout = timeoutMs || 5000;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this._pending.delete(id);
        reject(new ExtError('host timeout: ' + method));
      }, timeout);
      this._pending.set(id, { resolve, reject, timer });
      this._send({ id, method, params: params || {} });
    });
  }

  // ---------- 白名单 API ----------
  log(level, message) { return this.call('log', { level: String(level), message: String(message) }); }
  toast(message) { return this.call('toast', { message: String(message) }); }
  hostInfo() { return this.call('host.info'); }
  instances() { return this.call('instances.list'); }
  subscribe(types) { return this.call('event.subscribe', { types: types || [] }); }
  storageGet(key, fallback) {
    return this.call('storage.get', { key: String(key) }).then((r) =>
      Object.prototype.hasOwnProperty.call(r, 'value') ? r.value : fallback);
  }
  storageSet(key, value) { return this.call('storage.set', { key: String(key), value }); }
  settingsGet(key, fallback) {
    return this.call('settings.get', { key: String(key) }).then((r) =>
      Object.prototype.hasOwnProperty.call(r, 'value') ? r.value : fallback);
  }
  settingsSet(key, value) { return this.call('settings.set', { key: String(key), value }); }

  // ---------- 生命周期 ----------
  onStart() { return this.log('info', this.name + ' ' + this.version + ' started on ' + this.platform); }
  onEvent(type, data) { /* 覆写 */ }
  onStop() {}

  stop() {
    if (this._stopped) return;
    this._stopped = true;
    try { this.onStop(); } catch (_) {}
    for (const slot of this._pending.values()) clearTimeout(slot.timer);
    this._pending.clear();
    setImmediate(() => process.exit(0));
  }

  run() {
    process.stdin.setEncoding('utf8');
    const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
    rl.on('line', (line) => this._handle(line));
    rl.on('close', () => this.stop());
    process.on('SIGTERM', () => this.stop());
    return this;
  }
}

module.exports = { Ext, ExtError, PermissionDenied };
