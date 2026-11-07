'use strict';
/** Hello Node：打包时 SDK 会被放到包根目录的 tml_ext.js。 */
const { Ext } = require('./tml_ext');

class HelloNode extends Ext {
  constructor() {
    super({ name: 'Hello Node', version: '1.0.0' });
  }

  onStart() {
    return Promise.all([
      this.subscribe(['launcher.started', 'instance.listChanged']),
      this.log('info', 'Hello Node started on ' + this.platform),
      this.settingsSet('lastStart', new Date().toISOString())
    ]);
  }

  onEvent(type, data) {
    if (type === 'instance.listChanged') {
      return this.toast('实例列表已变化，共 ' + (data.count || 0) + ' 个');
    }
    return this.log('info', 'event ' + type);
  }
}

new HelloNode().run();
