// Package tmlext 是 TML 扩展 API v1 的 Go 语言 SDK。
//
// TYW Launcher 的扩展永远运行在启动器进程之外，只通过 stdin/stdout 的
// NDJSON（每行一个 JSON 对象）通信，扩展拿不到宿主内存、凭据或任意系统权限。
// 本包只依赖 Go 标准库，编译产物就是一个静态可执行文件，manifest 里把
// runtime 写成 go、entry 指向该二进制即可（宿主不会再套任何解释器）。
//
//	package main
//
//	func main() {
//	    tmlext.Run(tmlext.HandlerFunc(func(e tmlext.Event) {
//	        tmlext.Log("info", "收到事件 "+e.Type)
//	    }))
//	}
//
// 可调用的能力由 manifest.json 的 permissions 决定：没申请的调用会被宿主
// 拒绝（-32001），不会返回任何数据。协议细节见 extensions/README.md。
package tmlext

import (
	"bufio"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"strings"
)

// maxLine 与宿主保持一致：单行消息上限 64 KiB。
const maxLine = 64 * 1024

// Event 是宿主广播过来的一条白名单事件（数据已脱敏）。
type Event struct {
	Type string
	Data map[string]any
}

// Handler 是扩展的业务入口。两个方法都可以留空实现。
type Handler interface {
	// Ready 在 initialize 握手完成、可以调用宿主能力之后触发。
	Ready()
	// OnEvent 收到宿主广播时触发；可在其中调用 Log / StorageSet 等。
	OnEvent(Event)
}

// HandlerFunc 让函数式写法少写一个结构体。
type HandlerFunc func(Event)

// Ready 实现 Handler（空实现）。
func (HandlerFunc) Ready() {}

// OnEvent 实现 Handler。
func (f HandlerFunc) OnEvent(e Event) { f(e) }

// Session 是一条与宿主的会话，Run 内部创建，业务代码不需要自己 new。
type Session struct {
	in     *bufio.Scanner
	out    io.Writer
	nextID int64
	stop   bool
}

var active *Session

// Run 完成握手并进入事件循环，直到宿主发来 shutdown 或 stdin 关闭。
// handler 可以为 nil（只做握手，不处理事件）。
func Run(handler Handler) {
	s := &Session{
		in:     bufio.NewScanner(os.Stdin),
		out:    os.Stdout,
		nextID: 2, // 1 留给宿主的 initialize
	}
	s.in.Buffer(make([]byte, 0, 16*1024), maxLine*8)
	active = s

	if !s.handshake(handler) {
		return
	}
	if handler != nil {
		handler.Ready()
	}
	s.loop(handler)
}

// Stop 请求退出事件循环（当前调用返回后生效）。
func Stop() {
	if active != nil {
		active.stop = true
	}
}

func (s *Session) handshake(handler Handler) bool {
	for {
		raw, ok := s.readLine()
		if !ok {
			return false
		}
		m, ok := decode(raw)
		if !ok {
			continue
		}
		isInit := methodOf(m) == "initialize"
		s.dispatch(m, handler)
		if isInit {
			return true
		}
		if s.stop {
			return false
		}
	}
}

func (s *Session) loop(handler Handler) {
	for {
		raw, ok := s.readLine()
		if !ok {
			return
		}
		m, ok := decode(raw)
		if !ok {
			continue
		}
		s.dispatch(m, handler)
		if s.stop {
			return
		}
	}
}

func (s *Session) readLine() (string, bool) {
	for s.in.Scan() {
		line := strings.TrimSpace(s.in.Text())
		if line == "" {
			continue
		}
		return line, true
	}
	return "", false
}

// decode 把一行 JSON 解成「字段 -> 原始 JSON」，避免为大对象建结构体。
func decode(line string) (map[string]json.RawMessage, bool) {
	var m map[string]json.RawMessage
	if err := json.Unmarshal([]byte(line), &m); err != nil {
		return nil, false
	}
	return m, true
}

func methodOf(m map[string]json.RawMessage) string {
	var s string
	if raw, ok := m["method"]; ok {
		_ = json.Unmarshal(raw, &s)
	}
	return s
}

func (s *Session) dispatch(m map[string]json.RawMessage, handler Handler) {
	_, isResult := m["result"]
	_, isError := m["error"]
	if isResult || isError {
		return // 不是发给我们的响应，忽略
	}
	method := methodOf(m)
	var id json.RawMessage
	if raw, ok := m["id"]; ok {
		id = raw
	}
	reply := func(result any) {
		if id == nil {
			return
		}
		var rid any
		if err := json.Unmarshal(id, &rid); err != nil {
			return
		}
		_ = s.send(map[string]any{"id": rid, "result": result})
	}
	switch method {
	case "initialize":
		reply(map[string]any{"ready": true})
	case "shutdown":
		s.stop = true
	case "event":
		if handler == nil {
			return
		}
		var p struct {
			Type string         `json:"type"`
			Data map[string]any `json:"data"`
		}
		if raw, ok := m["params"]; ok {
			_ = json.Unmarshal(raw, &p)
		}
		handler.OnEvent(Event{Type: p.Type, Data: p.Data})
	default:
		// 宿主下发的自定义命令：SDK 一律确认，避免宿主等待超时
		reply(map[string]any{})
	}
}

func (s *Session) send(v any) error {
	b, err := json.Marshal(v)
	if err != nil {
		return err
	}
	if len(b) > maxLine {
		return errors.New("tmlext: 单行消息超过 64 KiB")
	}
	if _, err := fmt.Fprintf(s.out, "%s\n", b); err != nil {
		return err
	}
	return nil
}

// call 同步调用宿主能力，阻塞到拿到同 id 的响应。
func (s *Session) call(method string, params any) (json.RawMessage, error) {
	if s == nil {
		return nil, errors.New("tmlext: 扩展还没握手")
	}
	id := s.nextID
	s.nextID++
	msg := map[string]any{"id": id, "method": method}
	if params != nil {
		msg["params"] = params
	}
	if err := s.send(msg); err != nil {
		return nil, err
	}
	for {
		raw, ok := s.readLine()
		if !ok {
			return nil, io.EOF
		}
		m, ok := decode(raw)
		if !ok {
			continue
		}
		if rid, has := m["id"]; has && idEquals(rid, id) {
			if e, isErr := m["error"]; isErr {
				return nil, fmt.Errorf("tmlext: 宿主拒绝调用 %s：%s", method, string(e))
			}
			if r, hasR := m["result"]; hasR {
				return r, nil
			}
			return json.RawMessage("{}"), nil
		}
		s.dispatch(m, nil)
		if s.stop {
			return nil, io.EOF
		}
	}
}

func idEquals(raw json.RawMessage, id int64) bool {
	var n int64
	if err := json.Unmarshal(raw, &n); err == nil {
		return n == id
	}
	var f float64
	if err := json.Unmarshal(raw, &f); err == nil {
		return int64(f) == id
	}
	return false
}

// ---------------------------------------------------------------- 白名单能力

// Log 写启动器日志；level 只能是 info / warn / error。
func Log(level, message string) {
	if level != "info" && level != "warn" && level != "error" {
		level = "info"
	}
	_, _ = active.call("log", map[string]any{"level": level, "message": truncate(message, 4096)})
}

// Toast 在启动器里显示一条提示（纯文本，最长 300 字）。
func Toast(message string) {
	_, _ = active.call("toast", map[string]any{"message": truncate(message, 300)})
}

// HostInfo 只读：平台、架构、启动器版本、通道、语言、API 版本。
func HostInfo() map[string]any {
	return decodeObject(active, "host.info", nil)
}

// Instances 只读实例列表（仅 id / 名称 / 游戏版本 / 加载器类型）。
func Instances() []any {
	r, err := active.call("instances.list", nil)
	if err != nil {
		return nil
	}
	var box struct {
		Instances []any `json:"instances"`
	}
	_ = json.Unmarshal(r, &box)
	return box.Instances
}

// Subscribe 订阅白名单事件（清单里也要写在 subscribe 里）。
func Subscribe(types ...string) error {
	if len(types) == 0 {
		return nil
	}
	_, err := active.call("event.subscribe", map[string]any{"types": types})
	return err
}

// StorageGet 读扩展私有目录内的键值；不存在时 ok 为 false。
func StorageGet(key string) (string, bool) {
	return readString(active, "storage.get", key)
}

// StorageSet 写扩展私有目录内的键值（value ≤ 64 KiB）。
func StorageSet(key, value string) error {
	_, err := active.call("storage.set", map[string]any{"key": key, "value": truncate(value, 64*1024)})
	return err
}

// SettingsGet 读 ext.<extId>. 命名空间下的设置项。
func SettingsGet(key string) (string, bool) {
	return readString(active, "settings.get", key)
}

// SettingsSet 写 ext.<extId>. 命名空间下的设置项（value ≤ 8 KiB）。
func SettingsSet(key, value string) error {
	_, err := active.call("settings.set", map[string]any{"key": key, "value": truncate(value, 8*1024)})
	return err
}

// Env 读取宿主下发的协议配置（TML_EXT_ID / API / PLATFORM / LAUNCHER_VERSION …）。
func Env(name string) string {
	return os.Getenv("TML_EXT_" + strings.ToUpper(name))
}

func decodeObject(s *Session, method string, params any) map[string]any {
	r, err := s.call(method, params)
	if err != nil {
		return nil
	}
	var m map[string]any
	if json.Unmarshal(r, &m) != nil {
		return nil
	}
	return m
}

func readString(s *Session, method, key string) (string, bool) {
	r, err := s.call(method, map[string]any{"key": key})
	if err != nil {
		return "", false
	}
	var box struct {
		Value *string `json:"value"`
	}
	if json.Unmarshal(r, &box) != nil || box.Value == nil {
		return "", false
	}
	return *box.Value, true
}

func truncate(s string, max int) string {
	if len(s) <= max {
		return s
	}
	return s[:max]
}
