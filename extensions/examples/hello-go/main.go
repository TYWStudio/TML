// Hello Go —— TML 扩展 API v1 的 Go 示例。
//
// 运行方式：宿主（TYW Launcher）把 .tmlext 解开后，直接把包里的
// bin/tml_ext 当作独立进程拉起，双方只用 stdin/stdout 的 NDJSON 通信。
// 这里只演示 SDK 的用法；真正的能力边界由 manifest.json 的 permissions 决定。
package main

import (
	"encoding/json"

	tmlext "github.com/TYWStudio/TML/extensions/sdk/go"
)

type hello struct{}

func (hello) Ready() {
	tmlext.Log("info", "Hello Go 扩展已启动（TML Extension API v1）")

	if info := tmlext.HostInfo(); info != nil {
		if b, err := json.Marshal(info); err == nil {
			tmlext.Log("info", "host.info = "+string(b))
		}
	}

	for _, inst := range tmlext.Instances() {
		if b, err := json.Marshal(inst); err == nil {
			tmlext.Log("info", "instance = "+string(b))
		}
	}

	_ = tmlext.Subscribe("launcher.started", "game.launched", "settings.changed")

	_ = tmlext.StorageSet("lastRun", tmlext.Env("launcher_version"))
	if v, ok := tmlext.StorageGet("lastRun"); ok {
		tmlext.Log("info", "storage.get(lastRun) = "+v)
	}
	_ = tmlext.SettingsSet("greeted", "1")
	if v, ok := tmlext.SettingsGet("greeted"); ok {
		tmlext.Log("info", "settings.get(greeted) = "+v)
	}

	tmlext.Toast("Hello Go 已就绪")
}

func (hello) OnEvent(e tmlext.Event) {
	switch e.Type {
	case "launcher.started":
		tmlext.Log("info", "启动器已就绪")
	case "game.launched":
		name, _ := e.Data["name"].(string)
		tmlext.Log("info", "开始游戏："+name)
	default:
		tmlext.Log("info", "事件 "+e.Type)
	}
}

func main() {
	tmlext.Run(hello{})
}
