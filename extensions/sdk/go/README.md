# TML Extension SDK · Go

`package tmlext`，只依赖 Go 标准库，编译出来就是一个静态可执行文件。

## 引用

```bash
go get github.com/TYWStudio/TML/extensions/sdk/go@v1.0.0
```

```go
import tmlext "github.com/TYWStudio/TML/extensions/sdk/go"
```

版本用 Git tag 发：`extensions/sdk/go/v1.0.0`。SDK 有改动时**在本仓库改完再打新 tag**，
外部工程（含 `template-tml-extension-go`）只升级依赖版本，不要各自复制一份实现。

## 最小扩展

```go
package main

import tmlext "github.com/TYWStudio/TML/extensions/sdk/go"

type hello struct{}

func (hello) Ready() {
	tmlext.Subscribe("game.launched")
	tmlext.Log("info", "已就绪")
}

func (hello) OnEvent(e tmlext.Event) {
	tmlext.Log("info", e.Type)
}

func main() { tmlext.Run(hello{}) }
```

`manifest.json` 里 `runtime` 写 `go`，`entry` 指向编出来的二进制（例如 `bin/tml_ext`），
宿主不会再套解释器。能调什么由 `permissions` 决定，没申请的调用会被宿主拒绝（`-32001`）。

## 构建与打包

```bash
# 在扩展工程目录里
go build -trimpath -ldflags "-s -w" -o bin/tml_ext .

# 打成 .tmlext（需要本仓库的 pack.py）
python extensions/pack.py <扩展目录>
```

完整协议见 `extensions/README.md`，示例见 `extensions/examples/hello-go`。
