# -*- coding: utf-8 -*-
"""Hello Python：只用宿主开放的白名单 API。打包时 SDK 会被放到包根目录的 tml_ext.py。"""
from tml_ext import Ext


class HelloPython(Ext):
    def __init__(self):
        super(HelloPython, self).__init__(name="Hello Python", version="1.0.0")

    def on_start(self):
        super(HelloPython, self).on_start()
        self.subscribe(["launcher.started", "game.launched"])
        runs = self.storage_get("runs", 0)
        self.storage_set("runs", runs + 1)
        info = self.host_info()
        self.log("info", "宿主平台：%s，第 %s 次启动" % (info.get("platform", "?"), runs + 1))

    def on_event(self, event_type, data):
        self.log("info", "收到事件：" + event_type)
        if event_type == "game.launched":
            self.toast("游戏已启动：" + str(data.get("name", "")))


if __name__ == "__main__":
    HelloPython().run()
