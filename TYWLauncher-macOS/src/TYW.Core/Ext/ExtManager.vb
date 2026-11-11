Imports System
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.IO.Compression
Imports System.Diagnostics
Imports System.Security.Cryptography
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

' TML Extension API v1 —— macOS 端宿主层。
' 扩展永远运行在启动器进程之外，宿主与扩展之间只通过标准输入输出的 NDJSON 通信。
' 设计前提：默认全部拒绝。协议里根本不存在敏感能力，核心库不引用任何 UI 框架，
' 所有宿主能力都通过 Configure 注入的委托提供。
' RootNamespace 在 vbproj 里是 TYW，VB 会把显式命名空间再拼一层（变成 TYW.TYW.Core.Ext）。
' 用 Global. 声明绝对命名空间，确保实际命名空间恰为 TYW.Core.Ext（与目录 TYW.Core/Ext 一致）。
Namespace Global.TYW.Core.Ext

    ''' <summary>扩展状态。Error 是 VB 关键字，故用方括号转义。</summary>
    Public Enum ExtState
        Disabled
        Enabled
        [Error]
    End Enum

    ''' <summary>host.info 的只读宿主信息（平台/架构/版本/通道/语言）。</summary>
    Public Class HostInfo
        Public Property Platform As String = ""
        Public Property Arch As String = ""
        Public Property Version As String = ""
        Public Property Channel As String = ""
        Public Property Language As String = ""
    End Class

    ''' <summary>实例列表项：仅 id / 名称 / 游戏版本 / 加载器，绝不含路径、账号、token。</summary>
    Public Class InstanceInfo
        Public Property Id As String = ""
        Public Property Name As String = ""
        Public Property GameVersion As String = ""
        Public Property Loader As String = ""
    End Class

    ''' <summary>扩展清单与运行时状态，Scan/GetAll 返回。</summary>
    Public Class ExtInfo
        Public Property Id As String = ""
        Public Property Name As String = ""
        Public Property Version As String = ""
        Public Property Author As String = ""
        Public Property Description As String = ""
        Public Property Runtime As String = ""
        Public Property Format As String = ""
        Public Property FilePath As String = ""
        Public Property Sha256 As String = ""
        Public Property Platforms As List(Of String) = New List(Of String)()
        Public Property Permissions As List(Of String) = New List(Of String)()
        Public Property Subscribe As List(Of String) = New List(Of String)()
        Public Property State As ExtState = ExtState.Disabled
        Public Property ErrorMessage As String = ""
        ''' <summary>为 True 表示所需运行时缺失，需用户自行安装，宿主绝不下载或安装。</summary>
        Public Property NeedsRuntime As Boolean = False
        ' 宿主内部使用：包内入口（脚本相对路径或 Java 主类），不写进协议、不对外暴露。
        Friend Property Entry As String = ""
    End Class

    ''' <summary>
    ''' 扩展管理器（单例）。
    ''' README 第 9 节写的 Shared 单例：VB 里 Shared 是关键字、不能作属性名，
    ''' 因此公开名改为 Instance（Public Shared ReadOnly Property Instance），README 以 Instance 为准。
    ''' </summary>
    Public Class ExtensionManager

        ' ===== 平台与协议常量 =====
        Friend Const PLATFORM_NAME As String = "macos"   ' Linux 端为 "linux"
        Friend Const ApiVersion As Integer = 1
        Friend Const HandshakeId As Integer = 1
        Friend Const MaxLineBytes As Integer = 64 * 1024
        Friend Const HandshakeTimeoutMs As Integer = 10000
        Friend Const RequestTimeoutMs As Integer = 5000
        Friend Const MaxLogBytes As Integer = 4 * 1024
        Friend Const MaxToastChars As Integer = 300
        Friend Const MaxStorageValueBytes As Integer = 64 * 1024
        Friend Const MaxSettingsValueBytes As Integer = 8 * 1024
        Friend Const MaxStorageTotalBytes As Long = 1024L * 1024L
        Friend Const MaxKeyLength As Integer = 128
        Friend Const MaxProcesses As Integer = 8

        ' ===== 能力 / 事件 / 平台 白名单 =====
        Friend Shared ReadOnly CapabilityWhitelist As New HashSet(Of String)(StringComparer.Ordinal) From {
            "log", "toast", "host.info", "instances.list", "events", "storage", "settings"}
        Friend Shared ReadOnly EventWhitelist As New HashSet(Of String)(StringComparer.Ordinal) From {
            "launcher.started", "launcher.shutdown", "game.launching", "game.launched",
            "game.exited", "instance.listChanged", "settings.changed"}
        Friend Shared ReadOnly PlatformWhitelist As New HashSet(Of String)(StringComparer.Ordinal) From {
            "windows", "linux", "macos", "android"}

        ' 硬编码方法黑名单：协议里根本不存在这些能力，收到一律 -32001 并记日志，
        ' 防止后续误加 API 时被绕过。network.* 由前缀匹配兜底。
        Friend Shared ReadOnly DeniedMethods As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "file.read", "file.write", "exec", "spawn", "shell", "system", "http.fetch", "socket", "net",
            "request", "download", "upload", "credentials", "token", "account", "auth", "device", "code",
            "registry", "env", "clipboard", "screenshot", "update", "install", "launch.game",
            "settings.global", "reflect", "jni", "classloader", "extension.data"}
        Friend Shared ReadOnly DeniedPrefixes As String() = {
            "network.", "file.", "http.", "net.", "socket", "exec", "spawn", "shell", "system",
            "request", "download", "upload", "credential", "token", "account", "auth", "device",
            "code.", "registry", "env.", "clipboard", "screenshot", "update", "install", "launch.",
            "settings.global", "reflect", "jni", "classloader", "extension.data"}

        ' 子进程环境变量：只保留最小白名单，绝不继承启动器任何内部变量。
        Friend Shared ReadOnly AllowedEnvKeys As String() = {
            "PATH", "HOME", "USERPROFILE", "TMPDIR", "TEMP", "TMP", "LANG", "LC_ALL"}
        Friend Shared ReadOnly DeniedEnvKeys As String() = {
            "NODE_OPTIONS", "NODE_PATH", "PYTHONPATH", "PYTHONSTARTUP", "PYTHONHOME", "PYTHONINSPECT",
            "PYTHONWARNINGS", "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS", "CLASSPATH",
            "CDPATH", "LD_PRELOAD", "LD_LIBRARY_PATH", "DYLD_INSERT_LIBRARIES", "DYLD_LIBRARY_PATH",
            "DYLD_FRAMEWORK_PATH", "BASH_ENV", "ENV", "SHELLOPTS", "GLOBIGNORE", "IFS", "PS4"}

        Friend Shared ReadOnly IdRegex As New Regex("^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$", RegexOptions.CultureInvariant)
        Friend Shared ReadOnly ScriptEntryRegex As New Regex("^[A-Za-z0-9._/\-]+\.(py|js|mjs|cjs)$", RegexOptions.CultureInvariant)
        Friend Shared ReadOnly NativeEntryRegex As New Regex("^[A-Za-z0-9._/\-]+$", RegexOptions.CultureInvariant)
        Friend Shared ReadOnly NativeRuntimes As New HashSet(Of String) From {"c", "cpp", "rust", "go", "native", "zig"}

        ''' <summary>该 runtime 是否按原生可执行文件处理（编译型语言，包内二进制直接执行，不套解释器）。</summary>
        Friend Shared Function IsNativeRuntime(runtime As String) As Boolean
            If String.IsNullOrEmpty(runtime) Then Return False
            Return NativeRuntimes.Contains(runtime.Trim().ToLowerInvariant())
        End Function
        Friend Shared ReadOnly ClassNameRegex As New Regex("^[A-Za-z_$][A-Za-z0-9_$]*(\.[A-Za-z_$][A-Za-z0-9_$]*)+$", RegexOptions.CultureInvariant)
        Friend Shared ReadOnly KeyRegex As New Regex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)

        Private Shared ReadOnly _singleton As New ExtensionManager()

        ''' <summary>单例入口（对应 README 的 Shared）。</summary>
        Public Shared ReadOnly Property Instance As ExtensionManager
            Get
                Return _singleton
            End Get
        End Property

        Private ReadOnly _lock As New Object()
        Private ReadOnly _extensions As New Dictionary(Of String, ExtInfo)(StringComparer.Ordinal)
        Private ReadOnly _running As New Dictionary(Of String, ExtProcess)(StringComparer.Ordinal)

        ' 通过 Configure 注入的宿主能力（核心库不依赖 UI 框架）
        Private _log As Action(Of String, String) = Nothing
        Private _toast As Action(Of String) = Nothing
        Private _hostInfoProvider As Func(Of HostInfo) = Nothing
        Private _instancesProvider As Func(Of IList(Of InstanceInfo)) = Nothing
        Private _settingsReader As Func(Of String, String) = Nothing
        Private _settingsWriter As Action(Of String, String) = Nothing
        Private _extensionRootProvider As Func(Of String) = Nothing
        Private _configured As Boolean = False

        Private Sub New()
        End Sub

        ''' <summary>
        ''' 注入宿主能力：日志、toast、host.info、实例列表、设置读写、扩展根目录。
        ''' 日志回调为 (level, message)；settings 读写只作用于 ext.&lt;id&gt;. 命名空间。
        ''' </summary>
        Public Sub Configure(
            logCallback As Action(Of String, String),
            toastCallback As Action(Of String),
            hostInfoProvider As Func(Of HostInfo),
            instancesProvider As Func(Of IList(Of InstanceInfo)),
            settingsReader As Func(Of String, String),
            settingsWriter As Action(Of String, String),
            extensionRootProvider As Func(Of String))

            _log = logCallback
            _toast = toastCallback
            _hostInfoProvider = hostInfoProvider
            _instancesProvider = instancesProvider
            _settingsReader = settingsReader
            _settingsWriter = settingsWriter
            _extensionRootProvider = extensionRootProvider
            _configured = True
        End Sub

#Region "公开 API"

        ''' <summary>扫描 extensions/ 下的 .tmlext / .jar，严格校验清单，不启动任何进程，默认全部禁用。</summary>
        Public Function Scan() As List(Of ExtInfo)
            Dim root = RootDir()
            If String.IsNullOrEmpty(root) Then
                SafeLog("warn", "[ExtManager] 未通过 Configure 提供扩展根目录，Scan 跳过")
                Return GetAll()
            End If

            Dim found As New Dictionary(Of String, ExtInfo)(StringComparer.Ordinal)
            Try
                Directory.CreateDirectory(root)
                Dim files As New List(Of String)()
                files.AddRange(Directory.GetFiles(root, "*.tmlext"))
                files.AddRange(Directory.GetFiles(root, "*.jar"))
                For Each f In files
                    Dim info As New ExtInfo() With {.FilePath = f, .State = ExtState.Disabled}
                    Try
                        Dim isJar = f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                        Dim json = ReadAndValidateArchive(f, isJar)
                        ParseManifest(json, isJar, info)
                        info.Sha256 = ComputeSha256(f)
                        ' 包内容变化 -> 清信任并（在下方对账里）自动禁用
                        Dim trusted = ReadTrust(ei.Id)
                        If trusted IsNot Nothing AndAlso Not String.Equals(trusted, info.Sha256, StringComparison.OrdinalIgnoreCase) Then
                            ClearTrust(info.Id)
                            SafeLog("warn", "[ExtManager] 扩展 " & info.Id & " 包 SHA-256 已变化，已清除信任")
                        End If
                    Catch ex As Exception
                        info.State = ExtState.[Error]
                        info.ErrorMessage = ex.Message
                        SafeLog("warn", "[ExtManager] 校验失败 " & Path.GetFileName(f) & "：" & ex.Message)
                    End Try
                    If String.IsNullOrEmpty(info.Id) Then
                        info.Id = Path.GetFileNameWithoutExtension(f)
                    End If
                    found(info.Id) = info
                Next
            Catch ex As Exception
                SafeLog("error", "[ExtManager] Scan 遍历失败：" & ex.Message)
            End Try

            ' 与当前运行的进程对账：包失效 / 被移除 / SHA 变化 -> 结束进程并禁用
            Dim stale As New List(Of String)()
            SyncLock _lock
                For Each runningId In _running.Keys
                    Dim nf As ExtInfo = Nothing
                    Dim oldInfo = FindById(runningId)
                    Dim stillValid = found.TryGetValue(runningId, nf) AndAlso
                                     nf.State <> ExtState.[Error] AndAlso
                                     oldInfo IsNot Nothing AndAlso
                                     String.Equals(nf.Sha256, oldInfo.Sha256, StringComparison.OrdinalIgnoreCase)
                    If Not stillValid Then stale.Add(runningId)
                Next
                _extensions.Clear()
                For Each kv In found
                    _extensions(kv.Key) = kv.Value
                Next
                For Each runningId In _running.Keys
                    If Not stale.Contains(runningId) Then
                        Dim info = FindById(runningId)
                        If info IsNot Nothing Then info.State = ExtState.Enabled
                    End If
                Next
            End SyncLock
            For Each s In stale
                StopProcess(s, False)
            Next
            Return GetAll()
        End Function

        ''' <summary>返回当前已发现的扩展（默认全部禁用）。</summary>
        Public Function GetAll() As List(Of ExtInfo)
            Dim result As List(Of ExtInfo)
            SyncLock _lock
                result = New List(Of ExtInfo)(_extensions.Values)
            End SyncLock
            Return result
        End Function

        ''' <summary>用户看过权限清单并确认后启用；userConfirmed=False 一律拒绝。启用时记录包 SHA-256 到 trusted.json。</summary>
        Public Function Enable(id As String, userConfirmed As Boolean) As Boolean
            If Not userConfirmed Then
                SafeLog("warn", "[ExtManager] 启用 " & If(id, "") & " 被拒绝：未经用户确认")
                Return False
            End If
            If Not _configured Then
                SafeLog("warn", "[ExtManager] 尚未 Configure，无法启用扩展")
                Return False
            End If

            Dim info As ExtInfo = Nothing
            SyncLock _lock
                info = FindById(id)
            End SyncLock
            If info Is Nothing Then
                SafeLog("warn", "[ExtManager] 未找到扩展：" & If(id, ""))
                Return False
            End If
            If info.State = ExtState.[Error] Then
                SafeLog("warn", "[ExtManager] 扩展校验未通过，拒绝启用：" & info.Id)
                Return False
            End If

            Dim already As Boolean
            SyncLock _lock
                already = _running.ContainsKey(id)
            End SyncLock
            If already Then Return True

            ' 重新计算包 SHA-256 并检测包内容变化
            Dim sha As String
            Try
                sha = ComputeSha256(ei.FilePath)
            Catch ex As Exception
                SyncLock _lock
                    info.State = ExtState.[Error]
                    info.ErrorMessage = "无法读取扩展包：" & ex.Message
                End SyncLock
                Return False
            End Try
            Dim trusted = ReadTrust(info.Id)
            If trusted IsNot Nothing AndAlso Not String.Equals(trusted, sha, StringComparison.OrdinalIgnoreCase) Then
                ClearTrust(info.Id)
                StopProcess(id, False)
                SyncLock _lock
                    info.State = ExtState.Disabled
                    info.ErrorMessage = "包 SHA-256 已变化，已自动禁用并清除信任，请重新确认"
                End SyncLock
                SafeLog("warn", "[ExtManager] " & info.Id & " 包内容变化，自动禁用")
                Return False
            End If
            info.Sha256 = sha

            ' 启用前探测 runtime，找不到一律禁用，绝不下载或安装；原生扩展（c / rust / go …）不需要解释器
            Dim nativeExt = IsNativeRuntime(info.Runtime)
            Dim runtimePath = If(nativeExt, "", FindRuntime(info.Runtime))
            If String.IsNullOrEmpty(runtimePath) AndAlso Not nativeExt Then
                SyncLock _lock
                    info.State = ExtState.[Error]
                    info.ErrorMessage = "缺少运行时：" & info.Runtime & "，请自行安装后重试"
                    info.NeedsRuntime = True
                End SyncLock
                SafeLog("warn", "[ExtManager] " & info.Id & " 缺少运行时 " & info.Runtime)
                SafeToast("扩展 " & info.Name & " 缺少运行时：" & info.Runtime)
                Return False
            End If

            ' 每扩展最多 1 个进程，宿主总进程 <= 8
            Dim atCap As Boolean
            SyncLock _lock
                atCap = _running.Count >= MaxProcesses
            End SyncLock
            If atCap Then
                SafeLog("warn", "[ExtManager] 扩展进程数已达上限 " & MaxProcesses & "，无法启用 " & info.Id)
                Return False
            End If

            info.NeedsRuntime = False
            info.ErrorMessage = ""
            Dim proc As New ExtProcess(Me, info, runtimePath)
            If Not proc.Start() Then
                Return False
            End If
            SyncLock _lock
                _running(info.Id) = proc
                info.State = ExtState.Enabled
            End SyncLock
            WriteTrust(info.Id, sha)
            SafeLog("info", "[ExtManager] 已启用扩展 " & info.Id)
            Return True
        End Function

        ''' <summary>结束进程并禁用，保留私有数据与信任记录。</summary>
        Public Sub Disable(id As String)
            StopProcess(id, True)
            SyncLock _lock
                Dim info = FindById(id)
                If info IsNot Nothing Then
                    info.State = ExtState.Disabled
                    info.ErrorMessage = ""
                End If
            End SyncLock
            SafeLog("info", "[ExtManager] 已禁用扩展 " & If(id, ""))
        End Sub

        ''' <summary>查询是否处于启用状态。</summary>
        Public Function IsEnabled(id As String) As Boolean
            Dim enabled As Boolean
            SyncLock _lock
                Dim info = FindById(id)
                enabled = info IsNot Nothing AndAlso info.State = ExtState.Enabled
            End SyncLock
            Return enabled
        End Function

    ''' <summary>
    ''' 从本地临时文件安装拓展包：复制到拓展根目录 → 重新扫描 → 在用户确认后启用。
    ''' userConfirmed=False 时只把包放进拓展目录，等待用户在拓展页手动启用。
    ''' </summary>
    Public Function InstallPackage(localPath As String, userConfirmed As Boolean) As Boolean
        Try
            Dim root = RootDir()
            If String.IsNullOrWhiteSpace(root) Then
                SafeLog("warn", "[ExtManager] 未配置扩展根目录，无法安装拓展包")
                Return False
            End If
            Directory.CreateDirectory(root)
            Dim dest = Path.Combine(root, Path.GetFileName(localPath))
            File.Copy(localPath, dest, True)
            SafeLog("info", "[ExtManager] 已复制拓展包到 " & dest)
            Dim list = Scan()
            Dim id As String = Nothing
            For Each ei In list
                If String.Equals(info.FilePath, dest, StringComparison.OrdinalIgnoreCase) Then
                    id = info.Id
                    Exit For
                End If
            Next
            If id Is Nothing Then
                SafeLog("warn", "[ExtManager] 安装后未扫描到拓展：" & dest)
                Return False
            End If
            If Not userConfirmed Then
                SafeLog("info", "[ExtManager] 拓展已放入拓展目录，等待用户在拓展页启用：" & id)
                Return True
            End If
            Return Enable(id, True)
        Catch ex As Exception
            SafeLog("error", "[ExtManager] 安装拓展包失败：" & ex.Message)
            Return False
        End Try
    End Function

        ''' <summary>向已启用且 manifest.subscribe 含该事件的扩展广播（事件白名单 + 数据脱敏）。</summary>
        Public Sub Broadcast(eventType As String, data As Object)
            If String.IsNullOrEmpty(eventType) OrElse Not EventWhitelist.Contains(eventType) Then
                SafeLog("warn", "[ExtManager] 忽略非白名单事件：" & If(eventType, ""))
                Return
            End If
            Dim p As New Dictionary(Of String, Object)()
            p("type") = eventType
            p("data") = Sanitize(data, 0)
            Dim note As New Dictionary(Of String, Object)()
            note("method") = "event"
            note("params") = p
            Dim line = JsonSerializer.Serialize(note)

            Dim targets As New List(Of ExtProcess)()
            SyncLock _lock
                For Each kv In _running
                    Dim info = FindById(kv.Key)
                    If info IsNot Nothing AndAlso info.State = ExtState.Enabled AndAlso
                       info.Subscribe IsNot Nothing AndAlso info.Subscribe.Contains(eventType) Then
                        targets.Add(kv.Value)
                    End If
                Next
            End SyncLock
            For Each t In targets
                t.SendLine(line)
            Next
        End Sub

        ''' <summary>启动器退出时结束所有扩展进程。</summary>
        Public Sub ShutdownAll()
            Dim procs As New List(Of ExtProcess)()
            SyncLock _lock
                For Each kv In _running
                    procs.Add(kv.Value)
                Next
                _running.Clear()
            End SyncLock
            For Each p In procs
                p.Shutdown(True)
            Next
        End Sub

#End Region

#Region "注入回调的安全包装"

        Friend Sub SafeLog(level As String, message As String)
            Try
                If _log IsNot Nothing Then _log(level, message)
            Catch
            End Try
        End Sub

        Friend Sub SafeToast(message As String)
            Try
                If _toast IsNot Nothing Then _toast(message)
            Catch
            End Try
        End Sub

        Friend Function SnapshotHost() As HostInfo
            Dim hi As HostInfo = Nothing
            Try
                If _hostInfoProvider IsNot Nothing Then hi = _hostInfoProvider()
            Catch
            End Try
            If hi Is Nothing Then hi = New HostInfo()
            Dim snap As New HostInfo() With {
                .Platform = PLATFORM_NAME,
                .Arch = If(hi.Arch, ""),
                .Version = If(String.IsNullOrEmpty(hi.Version), "0.0.0", hi.Version),
                .Channel = If(String.IsNullOrEmpty(hi.Channel), "Release", hi.Channel),
                .Language = If(String.IsNullOrEmpty(hi.Language), "zh-CN", hi.Language)}
            Return snap
        End Function

        Friend Function GetInstances() As IList(Of InstanceInfo)
            Try
                If _instancesProvider IsNot Nothing Then
                    Dim r = _instancesProvider()
                    If r IsNot Nothing Then Return r
                End If
            Catch
            End Try
            Return New List(Of InstanceInfo)()
        End Function

        Friend Function GetSetting(key As String) As String
            If key Is Nothing OrElse Not key.StartsWith("ext.", StringComparison.Ordinal) Then Return Nothing
            Try
                If _settingsReader IsNot Nothing Then Return _settingsReader(key)
            Catch
            End Try
            Return Nothing
        End Function

        Friend Sub SetSetting(key As String, value As String)
            If key Is Nothing OrElse Not key.StartsWith("ext.", StringComparison.Ordinal) Then
                Throw New Exception("settings 命名空间越界")
            End If
            If _settingsWriter Is Nothing Then Throw New Exception("未配置 settings 写入回调")
            _settingsWriter(key, value)
        End Sub

#End Region

#Region "目录 / 信任 / SHA"

        Private Function RootDir() As String
            Try
                If _extensionRootProvider IsNot Nothing Then
                    Dim r = _extensionRootProvider()
                    If Not String.IsNullOrWhiteSpace(r) Then Return Path.GetFullPath(r)
                End If
            Catch
            End Try
            Return Nothing
        End Function

        Friend Function PrivateDir(id As String) As String
            Dim root = RootDir()
            If String.IsNullOrEmpty(root) Then Throw New Exception("未配置扩展根目录")
            Return Path.Combine(root, "data", id)
        End Function

        Friend Function StorageDir(id As String) As String
            Return Path.Combine(PrivateDir(id), "storage")
        End Function

        Friend Function PkgDir(id As String) As String
            Return Path.Combine(PrivateDir(id), "pkg")
        End Function

        Private Function TrustedPath(id As String) As String
            Return Path.Combine(PrivateDir(id), "trusted.json")
        End Function

        Private Function ComputeSha256(path As String) As String
            Using sha = SHA256.Create()
                Using fs = File.OpenRead(path)
                    Return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant()
                End Using
            End Using
        End Function

        Private Function ReadTrust(id As String) As String
            Try
                Dim p = TrustedPath(id)
                If Not File.Exists(p) Then Return Nothing
                Using doc = JsonDocument.Parse(File.ReadAllText(p, Encoding.UTF8))
                    Dim h As JsonElement
                    If doc.RootElement.TryGetProperty("sha256", h) AndAlso h.ValueKind = JsonValueKind.String Then
                        Return h.GetString()
                    End If
                End Using
            Catch
            End Try
            Return Nothing
        End Function

        Private Sub WriteTrust(id As String, sha As String)
            Try
                Dim d As New Dictionary(Of String, Object)()
                d("sha256") = sha
                AtomicWriteText(TrustedPath(id), JsonSerializer.Serialize(d))
            Catch ex As Exception
                SafeLog("warn", "[ExtManager] 写入 trusted.json 失败：" & ex.Message)
            End Try
        End Sub

        Private Sub ClearTrust(id As String)
            Try
                Dim p = TrustedPath(id)
                If File.Exists(p) Then File.Delete(p)
            Catch
            End Try
        End Sub

        Private Sub AtomicWriteText(targetPath As String, text As String)
            Dim folder = Path.GetDirectoryName(targetPath)
            Directory.CreateDirectory(folder)
            Dim tmp = Path.Combine(folder, "." & Guid.NewGuid().ToString("N") & ".tmp")
            File.WriteAllText(tmp, text, New UTF8Encoding(False))
            File.Move(tmp, targetPath, True)
        End Sub

#End Region

#Region "运行时探测（macOS：PATH 优先，再看系统兜底）"

        Private Function FindRuntime(runtime As String) As String
            Dim names As String()
            Select Case runtime
                Case "python3" : names = {"python3"}
                Case "node" : names = {"node"}
                Case "java" : names = {"java"}
                Case Else : Return Nothing
            End Select
            ' 探测顺序：先 PATH 里的解释器，再看 macOS 系统兜底路径
            For Each n In names
                Dim p = FindInPath(n)
                If p IsNot Nothing Then Return p
            Next
            For Each extra In ExtraCandidates(runtime)
                If Not String.IsNullOrEmpty(extra) AndAlso File.Exists(extra) Then Return extra
            Next
            Return Nothing
        End Function

        Private Function FindInPath(name As String) As String
            Dim pathVar = Environment.GetEnvironmentVariable("PATH")
            If String.IsNullOrEmpty(pathVar) Then Return Nothing
            For Each pathDir As String In pathVar.Split(Path.PathSeparator)
                If String.IsNullOrWhiteSpace(pathDir) Then Continue For
                Try
                    Dim candidate = Path.Combine(pathDir.Trim(), name)
                    If File.Exists(candidate) Then Return Path.GetFullPath(candidate)
                Catch
                End Try
            Next
            Return Nothing
        End Function

        ''' <summary>
        ''' macOS 在 PATH 之外再看系统级兜底：/usr/bin/python3 与 /usr/libexec/java_home。
        ''' 这些是系统固定路径，不硬编码任何用户机器路径；找不到一律由宿主报「缺少运行时」，不下载不安装。
        ''' </summary>
        Private Function ExtraCandidates(runtime As String) As String()
            If runtime = "python3" Then
                Return {"/usr/bin/python3"}
            End If
            If runtime = "java" Then
                Try
                    Dim psi As New ProcessStartInfo() With {
                        .FileName = "/usr/libexec/java_home",
                        .UseShellExecute = False,
                        .RedirectStandardOutput = True,
                        .CreateNoWindow = True
                    }
                    Using p = Process.Start(psi)
                        If p Is Nothing Then Return New String() {}
                        Dim out = p.StandardOutput.ReadLine()
                        p.WaitForExit(3000)
                        If Not String.IsNullOrWhiteSpace(out) Then
                            Dim java = Path.Combine(out.Trim(), "bin", "java")
                            If File.Exists(java) Then Return {java}
                        End If
                    End Using
                Catch
                End Try
            End If
            Return New String() {}
        End Function

#End Region

#Region "清单与归档校验"

        ''' <summary>读归档并做结构校验，返回清单 JSON 文本；任一失败即抛异常，绝不加载。</summary>
        Private Function ReadAndValidateArchive(filePath As String, isJar As Boolean) As String
            Const MaxFile As Long = 8L * 1024 * 1024
            Const MaxTotal As Long = 16L * 1024 * 1024
            Const MaxEntries As Integer = 64
            Dim manifestName = If(isJar, "META-INF/tml-extension.json", "manifest.json")
            Dim manifestText As String = Nothing
            Using z = ZipFile.OpenRead(filePath)
                Dim fileCount As Integer = 0
                Dim total As Long = 0
                For Each e In z.Entries
                    If Not IsSafeEntryName(e.FullName) Then Throw New Exception("不安全的归档条目：" & e.FullName)
                    If EntryIsSymlink(e) Then Throw New Exception("归档含符号链接：" & e.FullName)
                    Dim normalized = e.FullName.Replace("\"c, "/"c)
                    Dim isDir = normalized.EndsWith("/")
                    If Not isDir Then
                        If EntryIsExecutable(e) Then Throw New Exception("归档含可执行位：" & e.FullName)
                        If IsNestedArchive(normalized) Then Throw New Exception("归档含嵌套压缩包：" & e.FullName)
                        If e.Length > MaxFile Then Throw New Exception("单文件超过 8MiB：" & e.FullName)
                        total += e.Length
                        fileCount += 1
                        If fileCount > MaxEntries Then Throw New Exception("文件数超过 64")
                        If total > MaxTotal Then Throw New Exception("解压总大小超过 16MiB")
                    End If
                    If String.Equals(normalized, manifestName, StringComparison.Ordinal) Then
                        If e.Length > 65536 Then Throw New Exception("清单文件过大")
                        Using sr As New StreamReader(e.Open(), Encoding.UTF8, True)
                            manifestText = sr.ReadToEnd()
                        End Using
                    End If
                Next
            End Using
            If manifestText Is Nothing Then Throw New Exception("缺少清单文件：" & manifestName)
            Return manifestText
        End Function

        Private Sub ParseManifest(json As String, isJar As Boolean, info As ExtInfo)
            Using doc = JsonDocument.Parse(json)
                Dim r = doc.RootElement
                If r.ValueKind <> JsonValueKind.Object Then Throw New Exception("清单根不是对象")
                Dim v As JsonElement
                Dim api As Integer = 0
                If Not r.TryGetProperty("apiVersion", v) OrElse v.ValueKind <> JsonValueKind.Number OrElse
                   Not v.TryGetInt32(api) OrElse api <> 1 Then
                    Throw New Exception("apiVersion 必须为 1")
                End If

                Dim fmt = GetRequiredString(r, "format")
                If isJar Then
                    If fmt <> "jar" Then Throw New Exception("format 必须为 jar")
                Else
                    If fmt <> "tmlext" Then Throw New Exception("format 必须为 tmlext")
                End If
                info.Format = fmt

                Dim id = GetRequiredString(r, "id")
                If Not IdRegex.IsMatch(id) Then Throw New Exception("id 不合法：" & id)
                info.Id = id

                info.Name = GetStringOr(r, "name", "")
                info.Version = GetStringOr(r, "version", "")
                info.Author = GetStringOr(r, "author", "")
                info.Description = GetStringOr(r, "description", "")

                Dim rt = GetRequiredString(r, "runtime")
                If isJar Then
                    If rt <> "java" Then Throw New Exception("jar 的 runtime 必须为 java")
                ElseIf Not IsNativeRuntime(rt) Then
                    If rt <> "python3" AndAlso rt <> "node" Then
                        Throw New Exception("tmlext 的 runtime 必须为 python3 / node，或原生运行时 c / cpp / rust / go / native / zig")
                    End If
                End If
                info.Runtime = rt

                Dim entry = GetRequiredString(r, "entry")
                If Not IsSafeEntry(entry) Then Throw New Exception("entry 路径不合法：" & entry)
                If isJar Then
                    If Not ClassNameRegex.IsMatch(entry) Then Throw New Exception("jar 的 entry 必须为主类名")
                ElseIf IsNativeRuntime(rt) Then
                    If Not NativeEntryRegex.IsMatch(entry) Then Throw New Exception("原生扩展的 entry 必须为包内可执行文件路径")
                Else
                    If Not ScriptEntryRegex.IsMatch(entry) Then Throw New Exception("tmlext 的 entry 必须为脚本路径")
                End If
                info.Entry = entry

                Dim platforms = GetStringArray(r, "platforms", True)
                For Each p In platforms
                    If Not PlatformWhitelist.Contains(p) Then Throw New Exception("platforms 含未知值：" & p)
                Next
                If Not platforms.Contains(PLATFORM_NAME) Then Throw New Exception("当前平台 " & PLATFORM_NAME & " 不在 platforms 中")
                info.Platforms = platforms

                Dim perms = GetStringArray(r, "permissions", True)
                If perms.Count > 7 Then Throw New Exception("permissions 超过 7 项")
                For Each p In perms
                    If Not CapabilityWhitelist.Contains(p) Then Throw New Exception("permissions 含未知值：" & p)
                Next
                info.Permissions = perms

                Dim subs = GetStringArray(r, "subscribe", False)
                If subs.Count > 7 Then Throw New Exception("subscribe 超过 7 项")
                For Each s In subs
                    If Not EventWhitelist.Contains(s) Then Throw New Exception("subscribe 含未知事件：" & s)
                Next
                info.Subscribe = subs
            End Using
        End Sub

        Private Function GetRequiredString(r As JsonElement, name As String) As String
            Dim v As JsonElement
            If Not r.TryGetProperty(name, v) OrElse v.ValueKind <> JsonValueKind.String Then
                Throw New Exception("缺少或非法字段：" & name)
            End If
            Return If(v.GetString(), "")
        End Function

        Private Function GetStringOr(r As JsonElement, name As String, def As String) As String
            Dim v As JsonElement
            If r.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then
                Return If(v.GetString(), def)
            End If
            Return def
        End Function

        Private Function GetStringArray(r As JsonElement, name As String, required As Boolean) As List(Of String)
            Dim result As New List(Of String)()
            Dim arr As JsonElement
            If Not r.TryGetProperty(name, arr) Then
                If required Then Throw New Exception("缺少字段：" & name)
                Return result
            End If
            If arr.ValueKind <> JsonValueKind.Array Then Throw New Exception(name & " 必须为数组")
            For Each it In arr.EnumerateArray()
                If it.ValueKind <> JsonValueKind.String Then Throw New Exception(name & " 含非字符串值")
                Dim s = it.GetString()
                If result.Contains(s) Then Throw New Exception(name & " 含重复值：" & s)
                result.Add(s)
            Next
            If required AndAlso result.Count = 0 Then Throw New Exception(name & " 不能为空")
            Return result
        End Function

        Private Function IsSafeEntry(entry As String) As Boolean
            If String.IsNullOrEmpty(entry) OrElse entry.Length > 200 Then Return False
            If entry.Contains("..") Then Return False
            If entry.Contains("\") Then Return False
            If entry.Contains(":") Then Return False
            If entry.StartsWith("/") Then Return False
            Return True
        End Function

        ' 归档条目名：禁止绝对路径、盘符/冒号、.. 段（防 ZIP Slip）
        Friend Function IsSafeEntryName(name As String) As Boolean
            If String.IsNullOrEmpty(name) Then Return False
            Dim n = name.Replace("\"c, "/"c)
            If n.StartsWith("/") Then Return False
            If n.Contains(":") Then Return False
            For Each seg In n.Split("/"c)
                If seg = ".." Then Return False
            Next
            Return True
        End Function

        ' 外部属性高 16 位是 Unix 模式；S_IFLNK = 0xA000 即符号链接
        Friend Function EntryIsSymlink(e As ZipArchiveEntry) As Boolean
            Dim mode = (e.ExternalAttributes >> 16) And &HFFFF
            Return (mode And &HF000) = &HA000
        End Function

        Private Function EntryIsExecutable(e As ZipArchiveEntry) As Boolean
            Dim mode = (e.ExternalAttributes >> 16) And &HFFFF
            Return (mode And &H111) <> 0
        End Function

        Private Function IsNestedArchive(name As String) As Boolean
            Dim l = name.ToLowerInvariant()
            Return l.EndsWith(".zip") OrElse l.EndsWith(".jar") OrElse l.EndsWith(".tmlext") OrElse
                   l.EndsWith(".gz") OrElse l.EndsWith(".tgz") OrElse l.EndsWith(".tar") OrElse l.EndsWith(".7z")
        End Function

#End Region

#Region "方法映射 / 白名单 / 通用校验"

        Friend Function IsDenied(method As String) As Boolean
            If String.IsNullOrEmpty(method) Then Return True
            If DeniedMethods.Contains(method) Then Return True
            For Each p In DeniedPrefixes
                If method.StartsWith(p, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        End Function

        Friend Function RequiredPermission(method As String) As String
            Select Case method
                Case "log" : Return "log"
                Case "toast" : Return "toast"
                Case "host.info" : Return "host.info"
                Case "instances.list" : Return "instances.list"
                Case "event.subscribe" : Return "events"
                Case "storage.get", "storage.set" : Return "storage"
                Case "settings.get", "settings.set" : Return "settings"
                Case Else : Return Nothing
            End Select
        End Function

        Friend Function GetStr(p As JsonElement, name As String) As String
            Dim v As JsonElement
            If p.ValueKind = JsonValueKind.Object AndAlso p.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then
                Return If(v.GetString(), "")
            End If
            Return ""
        End Function

        Friend Function IsValidKey(key As String) As Boolean
            Return Not String.IsNullOrEmpty(key) AndAlso key.Length <= MaxKeyLength AndAlso KeyRegex.IsMatch(key)
        End Function

        Friend Function IsSymlink(path As String) As Boolean
            Try
                If Not (File.Exists(path) OrElse Directory.Exists(path)) Then Return False
                Return (File.GetAttributes(path) And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint
            Catch
                Return True   ' 无法确认时按不安全处理
            End Try
        End Function

        Friend Function StorageUsage(folder As String) As Long
            Dim total As Long = 0
            Try
                If Not Directory.Exists(folder) Then Return 0
                For Each f In Directory.GetFiles(folder)
                    Try
                        total += New FileInfo(f).Length
                    Catch
                    End Try
                Next
            Catch
            End Try
            Return total
        End Function

        Friend Shared Function NewNonce() As String
            Return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
        End Function

#End Region

#Region "广播数据脱敏"

        ' 事件 data 只保留简单类型（字符串/数字/布尔/数组/字典），
        ' 丢弃路径、URL、账号、token、设备码等敏感键与值。
        Private Function Sanitize(value As Object, depth As Integer) As Object
            If value Is Nothing OrElse depth > 8 Then Return Nothing
            If TypeOf value Is String Then Return ScrubString(DirectCast(value, String))
            If TypeOf value Is Boolean OrElse TypeOf value Is Byte OrElse TypeOf value Is SByte OrElse
               TypeOf value Is Short OrElse TypeOf value Is UShort OrElse TypeOf value Is Integer OrElse
               TypeOf value Is UInteger OrElse TypeOf value Is Long OrElse TypeOf value Is ULong OrElse
               TypeOf value Is Single OrElse TypeOf value Is Double OrElse TypeOf value Is Decimal Then
                Return value
            End If
            If TypeOf value Is JsonElement Then Return SanitizeElement(DirectCast(value, JsonElement), depth)
            If TypeOf value Is IDictionary(Of String, Object) Then
                Dim src = DirectCast(value, IDictionary(Of String, Object))
                Dim d As New Dictionary(Of String, Object)()
                Dim n As Integer = 0
                For Each kv In src
                    If n >= 128 Then Exit For
                    If IsSensitiveKey(kv.Key) Then Continue For
                    d(kv.Key) = Sanitize(kv.Value, depth + 1)
                    n += 1
                Next
                Return d
            End If
            If TypeOf value Is System.Collections.IEnumerable Then
                Dim l As New List(Of Object)()
                Dim n As Integer = 0
                For Each it In DirectCast(value, System.Collections.IEnumerable)
                    If n >= 256 Then Exit For
                    l.Add(Sanitize(it, depth + 1))
                    n += 1
                Next
                Return l
            End If
            Return Nothing
        End Function

        Private Function SanitizeElement(je As JsonElement, depth As Integer) As Object
            Select Case je.ValueKind
                Case JsonValueKind.String : Return ScrubString(je.GetString())
                Case JsonValueKind.Number : Return je.GetDouble()
                Case JsonValueKind.True : Return True
                Case JsonValueKind.False : Return False
                Case JsonValueKind.Null, JsonValueKind.Undefined : Return Nothing
                Case JsonValueKind.Object
                    Dim d As New Dictionary(Of String, Object)()
                    Dim n As Integer = 0
                    For Each prop In je.EnumerateObject()
                        If n >= 128 Then Exit For
                        If IsSensitiveKey(prop.Name) Then Continue For
                        d(prop.Name) = Sanitize(prop.Value, depth + 1)
                        n += 1
                    Next
                    Return d
                Case JsonValueKind.Array
                    Dim l As New List(Of Object)()
                    Dim n As Integer = 0
                    For Each it In je.EnumerateArray()
                        If n >= 256 Then Exit For
                        l.Add(Sanitize(it, depth + 1))
                        n += 1
                    Next
                    Return l
                Case Else
                    Return Nothing
            End Select
        End Function

        Private Function IsSensitiveKey(key As String) As Boolean
            If String.IsNullOrEmpty(key) Then Return True
            Dim l = key.ToLowerInvariant()
            For Each bad In {"path", "file", "token", "account", "secret", "password", "passwd", "server",
                             "url", "uri", "address", "cmd", "command", "env", "device", "code",
                             "credential", "private", "host", "port", "cookie", "auth"}
                If l.Contains(bad) Then Return True
            Next
            Return False
        End Function

        Private Function ScrubString(s As String) As String
            If s Is Nothing Then Return ""
            If s.Length > 4096 Then s = s.Substring(0, 4096)
            If s.StartsWith("/") OrElse s.Contains(":\") OrElse s.Contains("\\") OrElse s.Contains("://") Then
                Return "[redacted]"
            End If
            Return s
        End Function

#End Region

#Region "内部进程管理"

        Private Function FindById(id As String) As ExtInfo
            Dim info As ExtInfo = Nothing
            _extensions.TryGetValue(id, info)
            Return info
        End Function

        Private Sub StopProcess(id As String, graceful As Boolean)
            Dim proc As ExtProcess = Nothing
            SyncLock _lock
                If _running.TryGetValue(id, proc) Then
                    _running.Remove(id)
                End If
            End SyncLock
            If proc IsNot Nothing Then proc.Shutdown(graceful)
        End Sub

        ' 进程崩溃 / 超时 / 协议错误 -> 结束进程并自动禁用
        Friend Sub OnProcessFailed(proc As ExtProcess, reason As String)
            SafeLog("error", "[ExtManager] 扩展 " & proc.Info.Id & " 异常：" & reason & "，已自动禁用")
            Try
                proc.ForceKill()
            Catch
            End Try
            SyncLock _lock
                Dim cur As ExtProcess = Nothing
                If _running.TryGetValue(proc.Info.Id, cur) AndAlso cur Is proc Then
                    _running.Remove(proc.Info.Id)
                End If
                Dim info = FindById(proc.Info.Id)
                If info IsNot Nothing Then
                    info.State = ExtState.Disabled
                    info.ErrorMessage = reason
                End If
            End SyncLock
        End Sub

#End Region

    End Class

    ''' <summary>单个扩展子进程的封装：一个扩展最多一个进程，走 NDJSON 协议。</summary>
    Friend Class ExtProcess
        Private Const ShutdownLine As String = "{""method"":""shutdown""}"

        Private ReadOnly _owner As ExtensionManager
        Friend ReadOnly Info As ExtInfo
        Private ReadOnly _runtimePath As String

        Private _psi As ProcessStartInfo
        Private _proc As Process
        Private _writer As StreamWriter
        Private ReadOnly _sendLock As New Object()
        Private ReadOnly _dispatchLock As New Object()
        Private ReadOnly _readyEvent As New ManualResetEventSlim(False)
        Private _handshakeOk As Boolean = False
        Private _handshaken As Boolean = False
        Private _stopping As Boolean = False
        Private _failed As Integer = 0
        Private _rateWindowStart As Long = 0
        Private _rateCount As Integer = 0
        Private _stderrLines As Integer = 0

        Friend Sub New(owner As ExtensionManager, info As ExtInfo, runtimePath As String)
            _owner = owner
            Info = info
            _runtimePath = runtimePath
        End Sub

        ''' <summary>启动进程、发 initialize、等 10s 握手（ready=true）。</summary>
        Friend Function Start() As Boolean
            Try
                BuildStartInfo()
                _proc = Process.Start(_psi)
                If _proc Is Nothing Then Throw New Exception("Process.Start 返回空")
                _writer = _proc.StandardInput
                _writer.AutoFlush = True
                _writer.NewLine = vbLf
                Task.Run(Sub() StdoutLoop())
                Task.Run(Sub() StderrLoop())
                SendLine(BuildInitialize())
                If Not _readyEvent.Wait(ExtensionManager.HandshakeTimeoutMs) Then
                    Throw New Exception("初始化握手超时（10s）")
                End If
                If Not _handshakeOk Then Throw New Exception("初始化握手失败（ready != true）")
                _handshaken = True
                Return True
            Catch ex As Exception
                Fail("启动/握手失败：" & ex.Message)
                Return False
            End Try
        End Function

        Private Sub BuildStartInfo()
            Dim priv = _owner.PrivateDir(Info.Id)
            Directory.CreateDirectory(priv)
            Dim pkg = _owner.PkgDir(Info.Id)
            Dim jarPath As String = Nothing
            Dim mainClass As String = Nothing
            Dim entryFull As String = Nothing

            If String.Equals(Info.Format, "jar", StringComparison.Ordinal) Then
                ' jar 拷进私有 pkg 目录，用 java -cp <jar> <主类> 启动，绝不加载进宿主进程
                Directory.CreateDirectory(pkg)
                jarPath = Path.Combine(pkg, "extension.jar")
                File.Copy(Info.FilePath, jarPath, True)
                mainClass = Info.Entry
            Else
                ' .tmlext 先解压到只读私有 pkg/ 目录
                ExtractPackage(pkg)
                entryFull = ResolveEntry(pkg, Info.Entry)
                If ExtensionManager.IsNativeRuntime(Info.Runtime) Then MakeExecutable(entryFull)
            End If

            Dim native = ExtensionManager.IsNativeRuntime(Info.Runtime)
            Dim psi As New ProcessStartInfo()
            psi.FileName = If(native, entryFull, _runtimePath)
            psi.UseShellExecute = False
            psi.RedirectStandardInput = True
            psi.RedirectStandardOutput = True
            psi.RedirectStandardError = True
            psi.CreateNoWindow = True
            psi.WorkingDirectory = priv
            psi.StandardInputEncoding = New UTF8Encoding(False)
            psi.StandardOutputEncoding = Encoding.UTF8
            psi.StandardErrorEncoding = Encoding.UTF8
            If String.Equals(Info.Format, "jar", StringComparison.Ordinal) Then
                psi.ArgumentList.Add("-cp")
                psi.ArgumentList.Add(jarPath)
                psi.ArgumentList.Add(mainClass)
            ElseIf Not native Then
                psi.ArgumentList.Add(entryFull)
            End If
            ApplyEnvironment(psi)
            _psi = psi
        End Sub

        ''' <summary>原生扩展的二进制解压后没有执行位，宿主自己补（只 chmod +x，不做提权）。</summary>
        Private Sub MakeExecutable(path As String)
            Try
                Dim psi As New ProcessStartInfo("chmod", "+x """ & path & """") With {
                    .UseShellExecute = False, .CreateNoWindow = True}
                Dim p = Process.Start(psi)
                If p IsNot Nothing Then p.WaitForExit()
            Catch
            End Try
        End Sub

        Private Sub ExtractPackage(pkg As String)
            Directory.CreateDirectory(pkg)
            Using z = ZipFile.OpenRead(Info.FilePath)
                For Each e In z.Entries
                    If Not _owner.IsSafeEntryName(e.FullName) Then Throw New Exception("不安全的归档条目：" & e.FullName)
                    If _owner.EntryIsSymlink(e) Then Throw New Exception("归档含符号链接：" & e.FullName)
                    Dim name = e.FullName.Replace("\"c, "/"c)
                    Dim dest = Path.GetFullPath(Path.Combine(pkg, name))
                    Dim root = Path.GetFullPath(pkg)
                    If Not dest.StartsWith(root & Path.DirectorySeparatorChar, StringComparison.Ordinal) Then
                        Throw New Exception("ZIP Slip：" & e.FullName)
                    End If
                    If name.EndsWith("/") Then
                        Directory.CreateDirectory(dest)
                    Else
                        Directory.CreateDirectory(Path.GetDirectoryName(dest))
                        e.ExtractToFile(dest, True)
                        Try
                            File.SetAttributes(dest, FileAttributes.ReadOnly)
                        Catch
                        End Try
                    End If
                Next
            End Using
        End Sub

        Private Function ResolveEntry(pkg As String, entry As String) As String
            Dim full = Path.GetFullPath(Path.Combine(pkg, entry))
            Dim root = Path.GetFullPath(pkg)
            If Not full.StartsWith(root & Path.DirectorySeparatorChar, StringComparison.Ordinal) Then
                Throw New Exception("entry 越界：" & entry)
            End If
            If Not File.Exists(full) Then Throw New Exception("entry 不存在：" & entry)
            If _owner.IsSymlink(full) Then Throw New Exception("entry 为符号链接：" & entry)
            Return full
        End Function

        ' 清空并只重设最小环境变量白名单；不继承启动器任何内部变量；显式移除注入变量。
        Private Sub ApplyEnvironment(psi As ProcessStartInfo)
            Dim keep As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each key In ExtensionManager.AllowedEnvKeys
                Dim v = Environment.GetEnvironmentVariable(key)
                If Not String.IsNullOrEmpty(v) Then keep(key) = v
            Next
            psi.Environment.Clear()
            For Each kv In keep
                psi.Environment(kv.Key) = kv.Value
            Next
            psi.Environment("PYTHONIOENCODING") = "utf-8"
            For Each key In ExtensionManager.DeniedEnvKeys
                psi.Environment.Remove(key)
            Next
        End Sub

        Private Function BuildInitialize() As String
            Dim hi = _owner.SnapshotHost()
            Dim p As New Dictionary(Of String, Object)()
            p("apiVersion") = ExtensionManager.ApiVersion
            p("extId") = Info.Id
            p("platform") = ExtensionManager.PLATFORM_NAME
            p("launcherVersion") = hi.Version
            p("channel") = hi.Channel
            p("language") = hi.Language
            p("privateDir") = _owner.PrivateDir(Info.Id)
            p("permissions") = If(Info.Permissions, New List(Of String)())
            p("nonce") = ExtensionManager.NewNonce()
            Dim m As New Dictionary(Of String, Object)()
            m("id") = ExtensionManager.HandshakeId
            m("method") = "initialize"
            m("params") = p
            Return JsonSerializer.Serialize(m)
        End Function

        Private Function BuildResult(id As Integer, result As Object) As String
            Dim d As New Dictionary(Of String, Object)()
            d("id") = id
            d("result") = If(result, New Dictionary(Of String, Object)())
            Return JsonSerializer.Serialize(d)
        End Function

        Private Function BuildError(id As Integer, code As Integer, message As String) As String
            Dim d As New Dictionary(Of String, Object)()
            d("id") = id
            Dim e As New Dictionary(Of String, Object)()
            e("code") = code
            e("message") = message
            d("error") = e
            Return JsonSerializer.Serialize(d)
        End Function

        Friend Sub SendLine(line As String)
            If line Is Nothing OrElse _writer Is Nothing Then Exit Sub
            If Encoding.UTF8.GetByteCount(line) > ExtensionManager.MaxLineBytes Then
                _owner.SafeLog("warn", "[ext " & Info.Id & "] 跳过超长协议行")
                Exit Sub
            End If
            SyncLock _sendLock
                Try
                    _writer.WriteLine(line)
                Catch ex As Exception
                    Fail("写入标准输入失败：" & ex.Message)
                End Try
            End SyncLock
        End Sub

        Private Sub StdoutLoop()
            Try
                Do
                    Dim line = _proc.StandardOutput.ReadLine()
                    If line Is Nothing Then Exit Do
                    HandleLine(line)
                Loop
            Catch
            Finally
                If Not _stopping Then Fail("扩展进程输出结束或读取失败")
            End Try
        End Sub

        Private Sub StderrLoop()
            Try
                Do
                    Dim line = _proc.StandardError.ReadLine()
                    If line Is Nothing Then Exit Do
                    If _stderrLines >= 500 Then Exit Do   ' 输出总量限制
                    _stderrLines += 1
                    If Encoding.UTF8.GetByteCount(line) > ExtensionManager.MaxLineBytes Then Continue Do
                    _owner.SafeLog("warn", "[ext " & Info.Id & " stderr] " & line)
                Loop
            Catch
            End Try
        End Sub

        Private Sub HandleLine(line As String)
            If line Is Nothing Then Exit Sub
            If Encoding.UTF8.GetByteCount(line) > ExtensionManager.MaxLineBytes Then
                _owner.SafeLog("warn", "[ext " & Info.Id & "] 忽略超过 64KiB 的协议行")
                Exit Sub
            End If
            Dim root As JsonElement
            Try
                Using doc = JsonDocument.Parse(line)
                    root = doc.RootElement.Clone()
                End Using
            Catch
                Exit Sub   ' 忽略格式错误的行
            End Try
            If root.ValueKind <> JsonValueKind.Object Then Exit Sub

            Dim methodEl As JsonElement, idEl As JsonElement, paramsEl As JsonElement, tmpEl As JsonElement
            Dim idVal As Integer = 0
            Dim hasMethod As Boolean = root.TryGetProperty("method", methodEl) AndAlso methodEl.ValueKind = JsonValueKind.String
            Dim hasId As Boolean = root.TryGetProperty("id", idEl) AndAlso idEl.ValueKind = JsonValueKind.Number AndAlso idEl.TryGetInt32(idVal)
            Dim hasResult As Boolean = root.TryGetProperty("result", tmpEl)
            Dim hasError As Boolean = root.TryGetProperty("error", tmpEl)

            ' 扩展 -> 宿主：带 id 的请求
            If hasMethod AndAlso hasId Then
                If Not _handshaken Then Exit Sub   ' 握手完成前不处理扩展请求
                Dim p As JsonElement = Nothing
                If root.TryGetProperty("params", paramsEl) Then p = paramsEl.Clone()
                Dim reply As String = HandleRequest(idVal, methodEl.GetString(), p)
                If reply IsNot Nothing Then SendLine(reply)
                Exit Sub
            End If

            ' 宿主请求的响应（initialize 的握手应答）
            If hasId AndAlso (hasResult OrElse hasError) Then
                If idVal = ExtensionManager.HandshakeId Then
                    Dim ready As Boolean = False
                    If hasResult Then
                        Dim rEl As JsonElement, rdEl As JsonElement
                        If root.TryGetProperty("result", rEl) AndAlso rEl.ValueKind = JsonValueKind.Object Then
                            ready = rEl.TryGetProperty("ready", rdEl) AndAlso rdEl.ValueKind = JsonValueKind.True
                        End If
                    End If
                    _handshakeOk = ready
                    _readyEvent.Set()
                End If
                Exit Sub
            End If
            ' 其余（通知 / 格式错误）忽略
        End Sub

        Private Function HandleRequest(id As Integer, method As String, params As JsonElement) As String
            If _owner.IsDenied(method) Then
                _owner.SafeLog("warn", "[ExtManager] 扩展 " & Info.Id & " 调用被禁止的方法 " & method & "，已拒绝(-32001)")
                Return BuildError(id, -32001, "method denied: " & method)
            End If
            Dim perm As String = _owner.RequiredPermission(method)
            If perm Is Nothing Then Return BuildError(id, -32601, "method not found: " & method)
            If Info.Permissions Is Nothing OrElse Not Info.Permissions.Contains(perm) Then
                Return BuildError(id, -32001, "permission denied: " & method)
            End If
            ' 单次请求 5s 超时：注入的宿主回调不得长时间阻塞宿主
            Try
                Dim work As Task(Of String) = Task.Run(Function() DispatchLocked(id, method, params))
                If Not work.Wait(ExtensionManager.RequestTimeoutMs) Then
                    Fail("单次请求超时（5s）：" & method)
                    Return Nothing
                End If
                Return work.Result
            Catch ex As Exception
                Return BuildError(id, -32000, "host error: " & ex.Message)
            End Try
        End Function

        Private Function DispatchLocked(id As Integer, method As String, p As JsonElement) As String
            Dim result As String
            SyncLock _dispatchLock
                result = Dispatch(id, method, p)
            End SyncLock
            Return result
        End Function

        Private Function Dispatch(id As Integer, method As String, p As JsonElement) As String
            Select Case method
                Case "log" : Return DoLog(id, p)
                Case "toast" : Return DoToast(id, p)
                Case "host.info" : Return DoHostInfo(id)
                Case "instances.list" : Return DoInstances(id)
                Case "event.subscribe" : Return DoSubscribe(id, p)
                Case "storage.get" : Return DoStorageGet(id, p)
                Case "storage.set" : Return DoStorageSet(id, p)
                Case "settings.get" : Return DoSettingsGet(id, p)
                Case "settings.set" : Return DoSettingsSet(id, p)
                Case Else : Return BuildError(id, -32601, "method not found: " & method)
            End Select
        End Function

        Private Function DoLog(id As Integer, p As JsonElement) As String
            Dim message = _owner.GetStr(p, "message")
            Dim level = _owner.GetStr(p, "level")
            If level = "" Then level = "info"
            If level <> "info" AndAlso level <> "warn" AndAlso level <> "error" Then level = "info"
            If Encoding.UTF8.GetByteCount(message) > ExtensionManager.MaxLogBytes Then
                Return BuildError(id, -32002, "message 超过 4KiB")
            End If
            If Not RateAllow() Then Return BuildResult(id, Nothing)   ' 限频：静默丢弃
            _owner.SafeLog(level, "[ext " & Info.Id & "] " & message)
            Return BuildResult(id, Nothing)
        End Function

        Private Function RateAllow() As Boolean
            Dim now = Environment.TickCount64
            If now - _rateWindowStart > 1000 Then
                _rateWindowStart = now
                _rateCount = 0
            End If
            _rateCount += 1
            Return _rateCount <= 50
        End Function

        Private Function DoToast(id As Integer, p As JsonElement) As String
            Dim message = _owner.GetStr(p, "message")
            If message.Length > ExtensionManager.MaxToastChars Then
                Return BuildError(id, -32002, "toast 超过 300 字")
            End If
            If message.Contains("://") OrElse message.Contains("<") OrElse message.Contains(">") OrElse ContainsControl(message) Then
                Return BuildError(id, -32602, "toast 含不允许的内容")
            End If
            _owner.SafeToast(message)
            Return BuildResult(id, Nothing)
        End Function

        Private Function ContainsControl(s As String) As Boolean
            For Each ch In s
                If Convert.ToInt32(ch) < 32 Then Return True
            Next
            Return False
        End Function

        Private Function DoHostInfo(id As Integer) As String
            Dim hi = _owner.SnapshotHost()
            Dim d As New Dictionary(Of String, Object)()
            d("platform") = ExtensionManager.PLATFORM_NAME
            d("arch") = hi.Arch
            d("launcherVersion") = hi.Version
            d("channel") = hi.Channel
            d("language") = hi.Language
            d("apiVersion") = ExtensionManager.ApiVersion
            Return BuildResult(id, d)
        End Function

        Private Function DoInstances(id As Integer) As String
            Dim list As New List(Of Dictionary(Of String, Object))()
            Try
                For Each it In _owner.GetInstances()
                    If it Is Nothing Then Continue For
                    Dim d As New Dictionary(Of String, Object)()
                    d("id") = If(it.Id, "")
                    d("name") = If(it.Name, "")
                    d("gameVersion") = If(it.GameVersion, "")
                    d("loader") = If(it.Loader, "")
                    list.Add(d)
                Next
            Catch
            End Try
            Dim res As New Dictionary(Of String, Object)()
            res("instances") = list
            Return BuildResult(id, res)
        End Function

        Private Function DoSubscribe(id As Integer, p As JsonElement) As String
            Dim types As New List(Of String)()
            Dim arr As JsonElement
            If p.ValueKind = JsonValueKind.Object AndAlso p.TryGetProperty("types", arr) AndAlso arr.ValueKind = JsonValueKind.Array Then
                For Each it In arr.EnumerateArray()
                    If it.ValueKind <> JsonValueKind.String Then Continue For
                    Dim t = it.GetString()
                    If Not ExtensionManager.EventWhitelist.Contains(t) Then
                        Return BuildError(id, -32602, "未知事件：" & t)
                    End If
                    If Not types.Contains(t) Then types.Add(t)
                Next
            End If
            Dim res As New Dictionary(Of String, Object)()
            res("subscribed") = types
            Return BuildResult(id, res)
        End Function

        Private Function DoStorageGet(id As Integer, p As JsonElement) As String
            Dim key = _owner.GetStr(p, "key")
            If Not _owner.IsValidKey(key) Then Return BuildError(id, -32602, "key 不合法")
            Dim full As String
            Try
                full = ResolveStoragePath(key)
            Catch ex As Exception
                Return BuildError(id, -32602, ex.Message)
            End Try
            Dim res As New Dictionary(Of String, Object)()
            Try
                If File.Exists(full) Then
                    Using doc = JsonDocument.Parse(File.ReadAllText(full, Encoding.UTF8))
                        res("value") = doc.RootElement.Clone()
                    End Using
                End If
            Catch
                Return BuildError(id, -32000, "storage 读取失败")
            End Try
            Return BuildResult(id, res)
        End Function

        Private Function DoStorageSet(id As Integer, p As JsonElement) As String
            Dim key = _owner.GetStr(p, "key")
            If Not _owner.IsValidKey(key) Then Return BuildError(id, -32602, "key 不合法")
            Dim valEl As JsonElement
            If p.ValueKind <> JsonValueKind.Object OrElse Not p.TryGetProperty("value", valEl) Then
                Return BuildError(id, -32602, "缺少 value")
            End If
            Dim raw = valEl.GetRawText()
            Dim size As Long = Encoding.UTF8.GetByteCount(raw)
            If size > ExtensionManager.MaxStorageValueBytes Then
                Return BuildError(id, -32002, "value 超过 64KiB")
            End If
            Dim full As String
            Try
                full = ResolveStoragePath(key)
            Catch ex As Exception
                Return BuildError(id, -32602, ex.Message)
            End Try

            Dim storageDir = Path.GetFullPath(_owner.StorageDir(Info.Id))
            Dim usage = _owner.StorageUsage(storageDir)
            Dim oldSize As Long = 0
            Try
                If File.Exists(full) Then oldSize = New FileInfo(full).Length
            Catch
            End Try
            If usage - oldSize + size > ExtensionManager.MaxStorageTotalBytes Then
                Return BuildError(id, -32002, "storage 总量超过 1MiB")
            End If

            ' 临时文件 + 原子替换
            Dim tmp = Path.Combine(storageDir, "." & Guid.NewGuid().ToString("N") & ".tmp")
            Try
                File.WriteAllText(tmp, raw, New UTF8Encoding(False))
                File.Move(tmp, full, True)
            Catch ex As Exception
                Try
                    If File.Exists(tmp) Then File.Delete(tmp)
                Catch
                End Try
                Return BuildError(id, -32000, "storage 写入失败：" & ex.Message)
            End Try
            Return BuildResult(id, Nothing)
        End Function

        ' storage 实际路径必须落在扩展私有目录内；禁止 ..、绝对路径、符号链接。
        Private Function ResolveStoragePath(key As String) As String
            Dim storageDir = Path.GetFullPath(_owner.StorageDir(Info.Id))
            If Not Directory.Exists(storageDir) Then Directory.CreateDirectory(storageDir)
            If _owner.IsSymlink(storageDir) Then Throw New Exception("storage 目录为符号链接")
            Dim full = Path.GetFullPath(Path.Combine(storageDir, key))
            If Not full.StartsWith(storageDir & Path.DirectorySeparatorChar, StringComparison.Ordinal) Then
                Throw New Exception("storage key 越界：" & key)
            End If
            If _owner.IsSymlink(full) Then Throw New Exception("storage 目标为符号链接")
            Dim parent = Path.GetDirectoryName(full)
            If Not String.IsNullOrEmpty(parent) AndAlso _owner.IsSymlink(parent) Then
                Throw New Exception("storage 父目录为符号链接")
            End If
            Return full
        End Function

        Private Function DoSettingsGet(id As Integer, p As JsonElement) As String
            Dim key = _owner.GetStr(p, "key")
            If Not _owner.IsValidKey(key) Then Return BuildError(id, -32602, "key 不合法")
            ' 强制 ext.<id>. 命名空间，扩展无法读写其它命名空间
            Dim fullKey = "ext." & Info.Id & "." & key
            Dim res As New Dictionary(Of String, Object)()
            Try
                Dim raw = _owner.GetSetting(fullKey)
                If Not String.IsNullOrEmpty(raw) Then
                    Using doc = JsonDocument.Parse(raw)
                        res("value") = doc.RootElement.Clone()
                    End Using
                End If
            Catch
                Return BuildError(id, -32000, "settings 读取失败")
            End Try
            Return BuildResult(id, res)
        End Function

        Private Function DoSettingsSet(id As Integer, p As JsonElement) As String
            Dim key = _owner.GetStr(p, "key")
            If Not _owner.IsValidKey(key) Then Return BuildError(id, -32602, "key 不合法")
            Dim valEl As JsonElement
            If p.ValueKind <> JsonValueKind.Object OrElse Not p.TryGetProperty("value", valEl) Then
                Return BuildError(id, -32602, "缺少 value")
            End If
            Dim raw = valEl.GetRawText()
            If Encoding.UTF8.GetByteCount(raw) > ExtensionManager.MaxSettingsValueBytes Then
                Return BuildError(id, -32002, "value 超过 8KiB")
            End If
            Dim fullKey = "ext." & Info.Id & "." & key
            Try
                _owner.SetSetting(fullKey, raw)
            Catch ex As Exception
                Return BuildError(id, -32000, "settings 写入失败：" & ex.Message)
            End Try
            Return BuildResult(id, Nothing)
        End Function

        Private Sub Fail(reason As String)
            If _stopping Then Exit Sub
            If Interlocked.Exchange(_failed, 1) <> 0 Then Exit Sub
            Try
                _readyEvent.Set()
            Catch
            End Try
            _owner.OnProcessFailed(Me, reason)
        End Sub

        Friend Sub ForceKill()
            Try
                If _proc IsNot Nothing AndAlso Not _proc.HasExited Then _proc.Kill(True)
            Catch
            End Try
        End Sub

        Friend Sub Shutdown(graceful As Boolean)
            If _stopping Then Exit Sub
            _stopping = True
            Try
                If graceful Then
                    SendLine(ShutdownLine)
                    Try
                        If Not _proc.WaitForExit(1500) Then _proc.Kill(True)
                    Catch
                        Try
                            _proc.Kill(True)
                        Catch
                        End Try
                    End Try
                Else
                    Try
                        _proc.Kill(True)
                    Catch
                    End Try
                End If
            Catch
            End Try
            Try
                _readyEvent.Set()
            Catch
            End Try
        End Sub

    End Class

End Namespace
