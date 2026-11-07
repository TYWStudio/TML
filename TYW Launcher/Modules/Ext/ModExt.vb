Imports System.IO.Compression
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' TML Extension API v1 / Windows 宿主（规范见仓库 extensions/README.md）。
'''
''' 扩展永远运行在启动器进程之外，只通过 stdio 的 NDJSON 交换白名单方法。
''' 这里只做：清单校验、信任闸门、能力分发、事件广播。
''' 进程管理在 ModExtHost.vb。
'''
''' 安全前提：默认全部拒绝；敏感能力与一切能造成漏洞的能力，协议里根本不存在。
''' 扩展进程拿不到启动器内存、凭据、配置、任意文件 / 进程 / 网络能力。
''' </summary>
Public Module ModExt

#Region "常量与白名单"

    Public Const ApiVersion As Integer = 1
    Public Const PlatformName As String = "windows"

    Friend Const MaxLineBytes As Integer = 64 * 1024
    Friend Const MaxFileBytes As Long = 2 * 1024 * 1024
    Friend Const MaxTotalBytes As Long = 8 * 1024 * 1024
    Friend Const MaxFiles As Integer = 64
    Friend Const MaxExtensions As Integer = 8
    Friend Const MaxStorageBytes As Long = 1024 * 1024
    Friend Const MaxSettingsBytes As Integer = 8 * 1024

    ''' <summary>唯一允许出现在 manifest.permissions 里的能力。</summary>
    Friend ReadOnly AllowedPermissions As New HashSet(Of String) From {
        "log", "toast", "host.info", "instances.list", "events", "storage", "settings"}

    ''' <summary>唯一允许出现的事件。</summary>
    Friend ReadOnly AllowedEvents As New HashSet(Of String) From {
        "launcher.started", "launcher.shutdown", "game.launching", "game.launched",
        "game.exited", "instance.listChanged", "settings.changed"}

    ''' <summary>
    ''' 硬编码黑名单：即使以后误加了新方法，这些方法名也永远返回 -32001。
    ''' 任意文件 / 进程 / 网络 / 凭据 / 提权 / 反射 / 宿主内部对象，一律不开放。
    ''' </summary>
    Friend ReadOnly DeniedMethods As New HashSet(Of String) From {
        "file.read", "file.write", "file.list", "file.delete", "fs.read", "fs.write",
        "exec", "spawn", "shell", "system", "process.start", "process.kill", "run",
        "http.fetch", "http.request", "socket", "network", "net.request", "request",
        "download", "upload", "fetch", "dns", "proxy",
        "credentials", "credential", "token", "account", "accounts", "auth", "auth.token",
        "device", "device.code", "beta.code", "update.code", "secret", "secrets", "key",
        "server.address", "server.url", "registry", "env", "environment", "clipboard",
        "screenshot", "input", "keyboard", "sudo", "elevate",
        "update", "autoupdate", "install", "uninstall", "download.game", "launch.game",
        "game.start", "game.stop", "settings.global", "settings.set.global",
        "reflect", "reflection", "jni", "classloader", "load.jar", "load.dex",
        "extension.data", "extension.other", "ext.call", "host.object", "host.internal",
        "notice", "report", "quit", "restart", "shutdown.host"}

    ''' <summary>方法 → 所需权限。</summary>
    Friend ReadOnly MethodPermissions As New Dictionary(Of String, String) From {
        {"log", "log"},
        {"toast", "toast"},
        {"host.info", "host.info"},
        {"instances.list", "instances.list"},
        {"event.subscribe", "events"},
        {"storage.get", "storage"},
        {"storage.set", "storage"},
        {"settings.get", "settings"},
        {"settings.set", "settings"}}

    Private ReadOnly IdRegex As New Regex("^[a-z0-9][a-z0-9._-]{1,62}[a-z0-9]$", RegexOptions.Compiled)
    Private ReadOnly KeyRegex As New Regex("^[A-Za-z0-9._-]{1,128}$", RegexOptions.Compiled)
    Private ReadOnly SensitiveWords As String() = {
        "token", "password", "passwd", "secret", "refresh", "authorization", "cookie",
        "deviceid", "identify", "betacode", "privatekey", "credential"}

#End Region

#Region "模型与注入"

    Public Enum ExtState
        Disabled
        Enabled
        [Error]
    End Enum

    Public Class ExtInfo
        Public Property Id As String = ""
        Public Property Name As String = ""
        Public Property Version As String = ""
        Public Property Author As String = ""
        Public Property Description As String = ""
        Public Property Runtime As String = ""
        Public Property Format As String = ""
        Public Property Entry As String = ""
        Public Property FilePath As String = ""
        Public Property Sha256 As String = ""
        Public Property Platforms As New List(Of String)
        Public Property Permissions As New List(Of String)
        Public Property Subscribe As New List(Of String)
        Public Property State As ExtState = ExtState.Disabled
        Public Property ErrorMessage As String = ""
        Public Property NeedsRuntime As Boolean = False
        Public Property PrivateDir As String = ""
        Public Property PackageDir As String = ""
        <JsonIgnore> Friend Property Host As ExtHost = Nothing
    End Class

    ''' <summary>协议错误，由 ExtHost 转成 JSON-RPC error。</summary>
    Public Class ExtApiException
        Inherits Exception
        Public ReadOnly Code As Integer
        Public Sub New(code As Integer, message As String)
            MyBase.New(message)
            Me.Code = code
        End Sub
    End Class

    ' 宿主回调：核心逻辑不直接依赖 UI / 设置系统，由上层注入。
    Private LogCallback As Action(Of String, String) = Nothing          ' level, message
    Private ToastCallback As Action(Of String) = Nothing                ' message
    Private HostInfoProvider As Func(Of JObject) = Nothing
    Private InstancesProvider As Func(Of JArray) = Nothing
    Private SettingsReader As Func(Of String, Object) = Nothing
    Private SettingsWriter As Action(Of String, Object) = Nothing
    Private RootProvider As Func(Of String) = Nothing
    Private VersionProvider As Func(Of String) = Nothing
    Private ChannelProvider As Func(Of String) = Nothing
    Private LanguageProvider As Func(Of String) = Nothing

    Private ReadOnly LockObj As New Object
    Private ReadOnly Extensions As New Dictionary(Of String, ExtInfo)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>
    ''' 注入宿主能力。所有参数都可省略：未注入时退化为「只写日志、不弹提示、只读默认信息、
    ''' 设置与实例列表为空」，绝不回退到调用任意系统能力。
    ''' </summary>
    Public Sub Configure(Optional logCallback As Action(Of String, String) = Nothing,
                         Optional toastCallback As Action(Of String) = Nothing,
                         Optional hostInfoProvider As Func(Of JObject) = Nothing,
                         Optional instancesProvider As Func(Of JArray) = Nothing,
                         Optional settingsReader As Func(Of String, Object) = Nothing,
                         Optional settingsWriter As Action(Of String, Object) = Nothing,
                         Optional rootProvider As Func(Of String) = Nothing,
                         Optional versionProvider As Func(Of String) = Nothing,
                         Optional channelProvider As Func(Of String) = Nothing,
                         Optional languageProvider As Func(Of String) = Nothing)
        ModExt.LogCallback = logCallback
        ModExt.ToastCallback = toastCallback
        ModExt.HostInfoProvider = hostInfoProvider
        ModExt.InstancesProvider = instancesProvider
        ModExt.SettingsReader = settingsReader
        ModExt.SettingsWriter = settingsWriter
        ModExt.RootProvider = rootProvider
        ModExt.VersionProvider = versionProvider
        ModExt.ChannelProvider = channelProvider
        ModExt.LanguageProvider = languageProvider
    End Sub

#Region "安装 / 卸载 / 启动初始化"

    Private IsExtInit As Boolean = False

    ''' <summary>在启动器启动时调用一次，注入真实宿主能力。重复调用安全。</summary>
    Public Sub Init()
        If IsExtInit Then Return
        SyncLock LockObj
            If IsExtInit Then Return
            IsExtInit = True
        End SyncLock
        Configure(logCallback:=AddressOf DefaultLog,
                  toastCallback:=AddressOf DefaultToast,
                  hostInfoProvider:=AddressOf DefaultHostInfo,
                  instancesProvider:=AddressOf DefaultInstances,
                  settingsReader:=AddressOf DefaultSettingsGet,
                  settingsWriter:=AddressOf DefaultSettingsSet,
                  rootProvider:=AddressOf ExtRoot,
                  versionProvider:=Function() VersionDisplay,
                  channelProvider:=AddressOf DefaultChannel,
                  languageProvider:=Function() System.Threading.Thread.CurrentThread.CurrentUICulture.Name)
    End Sub

    Private Sub DefaultLog(level As String, message As String)
        Select Case level
            Case "warn" : Logger.Log(message, LogLevel.Warn)
            Case "error" : Logger.Log(message, LogLevel.Error)
            Case Else : Logger.Log(message, LogLevel.Info)
        End Select
    End Sub

    Private Sub DefaultToast(message As String)
        Hint(message, HintType.Blue, False)
    End Sub

    Private Function DefaultHostInfo() As JObject
        Dim o As New JObject()
        o("platform") = PlatformName
        o("arch") = If(System.Environment.Is64BitOperatingSystem, "x64", "x86")
        o("launcherVersion") = VersionDisplay
        o("channel") = DefaultChannel()
        o("language") = System.Threading.Thread.CurrentThread.CurrentUICulture.Name
        Return o
    End Function

    Private Function DefaultChannel() As String
        Dim ch As String = BuildType.ToString().ToLower()
        If ch = "debug" Then ch = "beta"
        Return ch
    End Function

    Private Function DefaultInstances() As JArray
        Dim arr As New JArray()
        Try
            For Each list In McInstanceList.Values
                For Each inst In list
                    Dim o As New JObject()
                    o("id") = inst.Name
                    o("name") = inst.Name
                    Try
                        If inst.Version IsNot Nothing Then o("gameVersion") = inst.Version.ToString()
                    Catch : End Try
                    o("loader") = ""
                    arr.Add(o)
                Next
            Next
        Catch ex As Exception
            Log("warn", "枚举实例失败：" & ex.Message)
        End Try
        Return arr
    End Function

    Private Function DefaultSettingsGet(key As String) As Object
        Return Settings.Get(Of String)(key)
    End Function

    Private Sub DefaultSettingsSet(key As String, value As Object)
        Settings.Set(key, If(value Is Nothing, "", value.ToString()))
    End Sub

    ''' <summary>导入扩展包：先校验清单，再复制到扩展根目录，最后重新扫描。</summary>
    Public Function Install(sourcePath As String) As ExtInfo
        If String.IsNullOrEmpty(sourcePath) OrElse Not File.Exists(sourcePath) Then
            Throw New ExtApiException(-32000, "扩展文件不存在")
        End If
        Dim ext As String = Path.GetExtension(sourcePath).ToLower()
        If ext <> ".tmlext" AndAlso ext <> ".jar" Then
            Throw New ExtApiException(-32000, "扩展文件必须是 .tmlext 或 .jar")
        End If
        Dim probe As ExtInfo = ParseOne(sourcePath, ExtRoot())
        If probe.State = ExtState.Error Then
            Throw New ExtApiException(-32600, "扩展清单无效：" & probe.ErrorMessage)
        End If
        Dim dest As String = Path.Combine(ExtRoot(), Path.GetFileName(sourcePath))
        File.Copy(sourcePath, dest, True)
        SyncLock LockObj
            Scan()
            If Extensions.TryGetValue(probe.Id, probe) Then Return probe
        End SyncLock
        Return probe
    End Function

    ''' <summary>卸载扩展：禁用、删除包文件、删除私有数据与信任记录。</summary>
    Public Sub Uninstall(id As String)
        Dim info As ExtInfo = GetInfo(id)
        If info Is Nothing Then Return
        Disable(id)
        Try
            If File.Exists(info.FilePath) Then File.Delete(info.FilePath)
        Catch ex As Exception
            Log("warn", "删除扩展文件失败：" & ex.Message)
        End Try
        Try
            If Directory.Exists(info.PrivateDir) Then Directory.Delete(info.PrivateDir, True)
        Catch
        End Try
        Dim root As String = ExtRoot()
        Dim trusted As JObject = ReadTrusted(root)
        If trusted.Property(id) IsNot Nothing Then
            trusted.Remove(id)
            WriteTrusted(root, trusted)
        End If
        SyncLock LockObj
            Extensions.Remove(id)
        End SyncLock
    End Sub

#End Region

    Private Function ExtRoot() As String
        Dim root As String = If(RootProvider IsNot Nothing, RootProvider(), "")
        If String.IsNullOrWhiteSpace(root) Then
            Try
                ' 优先放到用户可写目录；不放在 exe 旁边（Program Files 下无写权限，且与二进制同目录不安全）
                root = Path.Combine(Paths.AppDataThenName, "Extensions")
            Catch
                root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions")
            End Try
        End If
        Try
            Directory.CreateDirectory(root)
        Catch
        End Try
        Return root
    End Function

    Friend Sub Log(level As String, message As String)
        If String.IsNullOrEmpty(message) Then Return
        If message.Length > 4096 Then message = message.Substring(0, 4096)
        Try
            If LogCallback IsNot Nothing Then
                LogCallback(level, message)
            Else
                Logger.Log("[Ext] " & message, If(String.Equals(level, "error", StringComparison.OrdinalIgnoreCase),
                                                  LogLevel.Error, LogLevel.Info))
            End If
        Catch
        End Try
    End Sub

    Friend Sub Toast(message As String)
        If String.IsNullOrEmpty(message) Then Return
        If message.Length > 300 Then message = message.Substring(0, 300)
        Try
            If ToastCallback IsNot Nothing Then
                ToastCallback(message)
            Else
                Hint("[扩展] " & message, HintType.Blue)
            End If
        Catch ex As Exception
            Log("warn", "扩展提示显示失败：" & ex.Message)
        End Try
    End Sub

#End Region

#Region "扫描与清单校验"

    ''' <summary>扫描 extensions 目录。只解析清单，不启动任何进程。</summary>
    Public Function Scan() As List(Of ExtInfo)
        Dim result As New List(Of ExtInfo)
        Dim root As String = ExtRoot()
        Dim trusted As JObject = ReadTrusted(root)
        Dim files As New List(Of String)
        Try
            files.AddRange(Directory.GetFiles(root, "*.tmlext", SearchOption.TopDirectoryOnly))
            files.AddRange(Directory.GetFiles(root, "*.jar", SearchOption.TopDirectoryOnly))
        Catch ex As Exception
            Log("warn", "扫描扩展目录失败：" & ex.Message)
        End Try
        SyncLock LockObj
            Extensions.Clear()
            For Each file In files
                Dim info As ExtInfo = ParseOne(file, root)
                Try
                    If trusted IsNot Nothing AndAlso trusted.Property(info.Id) IsNot Nothing Then
                        ' 包内容变了：信任失效，自动禁用，等用户重新确认
                        Dim saved As String = If(trusted.Value(Of String)(info.Id), "")
                        If saved <> "" AndAlso Not String.Equals(saved, info.Sha256, StringComparison.OrdinalIgnoreCase) Then
                            info.State = ExtState.Disabled
                            info.ErrorMessage = "扩展包已变更，需要重新确认权限后启用"
                            trusted.Remove(info.Id)
                        End If
                    End If
                Catch
                End Try
                If Not Extensions.ContainsKey(info.Id) Then Extensions.Add(info.Id, info)
                result.Add(info)
            Next
        End SyncLock
        If trusted IsNot Nothing Then WriteTrusted(root, trusted)
        Return result
    End Function

    Public Function GetAll() As List(Of ExtInfo)
        SyncLock LockObj
            Return New List(Of ExtInfo)(Extensions.Values)
        End SyncLock
    End Function

    Public Function GetInfo(id As String) As ExtInfo
        SyncLock LockObj
            Dim info As ExtInfo = Nothing
            If id IsNot Nothing AndAlso Extensions.TryGetValue(id, info) Then Return info
            Return Nothing
        End SyncLock
    End Function

    Private Function ParseOne(file As String, root As String) As ExtInfo
        Dim info As New ExtInfo With {
            .FilePath = file,
            .Format = If(file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase), "jar", "tmlext"),
            .State = ExtState.Disabled}
        Try
            info.Sha256 = ComputeSha256(file)
            Dim manifestName As String = If(info.Format = "jar", "META-INF/tml-extension.json", "manifest.json")
            Using zip As ZipArchive = ZipFile.OpenRead(file)
                Dim manifestEntry As ZipArchiveEntry = Nothing
                Dim total As Long = 0
                Dim count As Integer = 0
                For Each entry As ZipArchiveEntry In zip.Entries
                    count += 1
                    If count > MaxFiles Then Throw New ExtApiException(-32600, "包内文件数超过 " & MaxFiles)
                    If entry.Length > MaxFileBytes Then Throw New ExtApiException(-32600, "包内单文件超过 2 MiB：" & entry.FullName)
                    total += entry.Length
                    If total > MaxTotalBytes Then Throw New ExtApiException(-32600, "解压后总大小超过 8 MiB")
                    ' ZIP Slip / 绝对路径 / 反斜杠 / 盘符 防护
                    If Not SafeEntryName(entry.FullName) Then Throw New ExtApiException(-32600, "包内路径不合法：" & entry.FullName)
                    If String.Equals(entry.FullName.Replace("\"c, "/"c), manifestName, StringComparison.OrdinalIgnoreCase) Then
                        manifestEntry = entry
                    End If
                Next
                If manifestEntry Is Nothing Then Throw New ExtApiException(-32600, "缺少 " & manifestName)
                If manifestEntry.Length > 64 * 1024 Then Throw New ExtApiException(-32600, "清单过大")
                Dim text As String
                Using sr As New StreamReader(manifestEntry.Open(), Encoding.UTF8, True)
                    text = sr.ReadToEnd()
                End Using
                Dim m As JObject = JObject.Parse(text)
                ValidateManifest(m, info)
            End Using
            info.PrivateDir = Path.Combine(root, "data", info.Id)
            info.PackageDir = Path.Combine(info.PrivateDir, "pkg")
        Catch ex As ExtApiException
            info.State = ExtState.Error
            info.ErrorMessage = ex.Message
        Catch ex As Exception
            info.State = ExtState.Error
            info.ErrorMessage = "扩展包解析失败：" & ex.Message
        End Try
        Return info
    End Function

    Friend Function SafeEntryName(name As String) As Boolean
        If String.IsNullOrWhiteSpace(name) Then Return False
        If name.Contains("..") OrElse name.Contains("\"c) OrElse name.Contains(":"c) Then Return False
        If name.StartsWith("/") OrElse name.StartsWith("~") Then Return False
        If name.EndsWith("/") Then Return False
        Dim full As String
        Try
            full = Path.GetFullPath(Path.Combine("C:\dummy-root", name))
            If Not full.StartsWith("C:\dummy-root\", StringComparison.OrdinalIgnoreCase) Then Return False
        Catch
            Return False
        End Try
        Return True
    End Function

    Private Sub ValidateManifest(m As JObject, info As ExtInfo)
        If m.Value(Of Integer?)("apiVersion") <> ApiVersion Then Throw New ExtApiException(-32600, "apiVersion 必须为 1")
        info.Id = If(m.Value(Of String)("id"), "")
        If Not IdRegex.IsMatch(info.Id) Then Throw New ExtApiException(-32600, "id 不合法：" & info.Id)
        info.Name = If(m.Value(Of String)("name"), info.Id)
        info.Version = If(m.Value(Of String)("version"), "")
        info.Author = If(m.Value(Of String)("author"), "")
        info.Description = If(m.Value(Of String)("description"), "")
        info.Runtime = If(m.Value(Of String)("runtime"), "")
        info.Entry = If(m.Value(Of String)("entry"), "")
        Dim fmt As String = If(m.Value(Of String)("format"), "")
        If fmt <> info.Format Then Throw New ExtApiException(-32600, "format 与文件后缀不一致")
        If info.Entry.Length = 0 OrElse info.Entry.Contains("..") OrElse info.Entry.Contains("\"c) OrElse
           info.Entry.Contains(":"c) OrElse info.Entry.StartsWith("/") Then
            Throw New ExtApiException(-32600, "entry 路径不合法")
        End If
        If info.Format = "tmlext" Then
            If info.Runtime <> "python3" AndAlso info.Runtime <> "node" Then Throw New ExtApiException(-32600, "tmlext 只支持 python3 / node")
            If Not Regex.IsMatch(info.Entry, "^[A-Za-z0-9._/\-]+\.(py|js|mjs|cjs)$") Then Throw New ExtApiException(-32600, "entry 必须是 .py / .js 文件")
        Else
            If info.Runtime <> "java" Then Throw New ExtApiException(-32600, "jar 的 runtime 必须是 java")
            If Not Regex.IsMatch(info.Entry, "^[A-Za-z_$][A-Za-z0-9_$]*(\.[A-Za-z_$][A-Za-z0-9_$]*)+$") Then Throw New ExtApiException(-32600, "entry 必须是主类全名")
        End If
        info.Platforms = ReadStringArray(m, "platforms")
        info.Permissions = ReadStringArray(m, "permissions")
        info.Subscribe = ReadStringArray(m, "subscribe")
        If info.Platforms.Count = 0 OrElse Not info.Platforms.Contains(PlatformName) Then Throw New ExtApiException(-32600, "扩展不支持当前平台")
        For Each p As String In info.Platforms
            If Not {"windows", "linux", "macos", "android"}.Contains(p) Then Throw New ExtApiException(-32600, "平台不合法：" & p)
        Next
        For Each p As String In info.Permissions
            If Not AllowedPermissions.Contains(p) Then Throw New ExtApiException(-32001, "申请了未开放的能力：" & p)
        Next
        For Each e As String In info.Subscribe
            If Not AllowedEvents.Contains(e) Then Throw New ExtApiException(-32001, "申请了未开放的事件：" & e)
        Next
        ' 申请了 events 权限才允许订阅；没申请就不允许带 subscribe
        If info.Subscribe.Count > 0 AndAlso Not info.Permissions.Contains("events") Then
            Throw New ExtApiException(-32001, "订阅事件需要 events 权限")
        End If
    End Sub

    Private Function ReadStringArray(m As JObject, key As String) As List(Of String)
        Dim result As New List(Of String)
        Dim arr As JArray = TryCast(m.Property(key)?.Value, JArray)
        If arr Is Nothing Then Return result
        For Each t As JToken In arr
            If t.Type = JTokenType.String Then result.Add(t.Value(Of String)())
        Next
        Return result
    End Function

#End Region

#Region "信任与启用"

    Private Function TrustedPath(root As String) As String
        Return Path.Combine(root, "trusted.json")
    End Function

    Private Function ReadTrusted(root As String) As JObject
        Try
            Dim p As String = TrustedPath(root)
            If File.Exists(p) Then Return JObject.Parse(File.ReadAllText(p, Encoding.UTF8))
        Catch ex As Exception
            Log("warn", "读取扩展信任记录失败：" & ex.Message)
        End Try
        Return New JObject()
    End Function

    Private Sub WriteTrusted(root As String, trusted As JObject)
        Try
            File.WriteAllText(TrustedPath(root), trusted.ToString(Formatting.Indented), Encoding.UTF8)
        Catch ex As Exception
            Log("warn", "写入扩展信任记录失败：" & ex.Message)
        End Try
    End Sub

    ''' <summary>用户看过权限清单并确认后才能启用；userConfirmed=False 一律拒绝。</summary>
    Public Function Enable(id As String, userConfirmed As Boolean) As Boolean
        If Not userConfirmed Then Return False
        Dim info As ExtInfo = GetInfo(id)
        If info Is Nothing OrElse info.State = ExtState.Error Then Return False
        If info.Host IsNot Nothing AndAlso info.Host.IsAlive Then Return True
        If info.Permissions.Count > 0 Then Log("info", "启用扩展 " & info.Id & "，已授权：" & String.Join("、", info.Permissions))
        Dim host As New ExtHost(info)
        Try
            host.Start()
        Catch ex As Exception
            info.State = ExtState.Error
            info.ErrorMessage = ex.Message
            info.NeedsRuntime = ex.Message.Contains("缺少运行时")
            Log("warn", "扩展 " & info.Id & " 启动失败：" & ex.Message)
            Return False
        End Try
        info.Host = host
        info.State = ExtState.Enabled
        Dim trusted As JObject = ReadTrusted(ExtRoot())
        trusted(info.Id) = info.Sha256
        WriteTrusted(ExtRoot(), trusted)
        Return True
    End Function

    Public Sub Disable(id As String)
        Dim info As ExtInfo = GetInfo(id)
        If info Is Nothing Then Return
        Try
            If info.Host IsNot Nothing Then info.Host.Shutdown()
        Catch
        End Try
        info.Host = Nothing
        info.State = ExtState.Disabled
    End Sub

    Public Function IsEnabled(id As String) As Boolean
        Dim info As ExtInfo = GetInfo(id)
        Return info IsNot Nothing AndAlso info.State = ExtState.Enabled AndAlso info.Host IsNot Nothing AndAlso info.Host.IsAlive
    End Function

    ''' <summary>扩展进程异常退出时由 ExtHost 回调：自动禁用，绝不影响启动器。</summary>
    Friend Sub OnHostExited(info As ExtInfo, reason As String)
        If info Is Nothing Then Return
        info.Host = Nothing
        info.State = ExtState.Error
        info.ErrorMessage = reason
        Log("warn", "扩展 " & info.Id & " 已自动禁用：" & reason)
    End Sub

    Public Sub ShutdownAll()
        Dim list As List(Of ExtInfo) = GetAll()
        For Each info As ExtInfo In list
            Try
                If info.Host IsNot Nothing Then info.Host.Shutdown()
            Catch
            End Try
            info.Host = Nothing
            info.State = ExtState.Disabled
        Next
    End Sub

    ''' <summary>只广播事件白名单，且只发给已启用 / 已订阅的扩展；data 一律脱敏。</summary>
    Public Sub Broadcast(eventType As String, data As JObject)
        If Not AllowedEvents.Contains(eventType) Then Return
        Dim clean As JObject = Sanitize(data)
        For Each info As ExtInfo In GetAll()
            If info.State <> ExtState.Enabled OrElse info.Host Is Nothing Then Continue For
            If Not info.Subscribe.Contains(eventType) Then Continue For
            Try
                info.Host.SendEvent(eventType, clean)
            Catch ex As Exception
                Log("warn", "广播事件到 " & info.Id & " 失败：" & ex.Message)
            End Try
        Next
    End Sub

    ''' <summary>事件数据只保留简单类型，并过滤任何含敏感词或路径的键。</summary>
    Friend Function Sanitize(data As JObject) As JObject
        Dim result As New JObject()
        If data Is Nothing Then Return result
        For Each p As JProperty In data.Properties()
            Dim key As String = p.Name.ToLowerInvariant()
            If SensitiveWords.Any(Function(w) key.Contains(w)) Then Continue For
            If TypeOf p.Value Is JValue Then
                Dim v As JValue = DirectCast(p.Value, JValue)
                If v.Type = JTokenType.String Then
                    Dim s As String = v.Value(Of String)()
                    If s Is Nothing Then Continue For
                    If s.Length > 200 Then s = s.Substring(0, 200)
                    If s.Contains("\") OrElse s.Contains("/") OrElse s.Contains(":") Then Continue For
                    If SensitiveWords.Any(Function(w) s.ToLowerInvariant().Contains(w)) Then Continue For
                    result(p.Name) = s
                ElseIf v.Type = JTokenType.Integer OrElse v.Type = JTokenType.Float OrElse
                       v.Type = JTokenType.Boolean OrElse v.Type = JTokenType.Null Then
                    result(p.Name) = v.DeepClone()
                End If
            ElseIf TypeOf p.Value Is JArray AndAlso DirectCast(p.Value, JArray).Count <= 32 Then
                result(p.Name) = p.Value.DeepClone()
            End If
        Next
        Return result
    End Function

#End Region

#Region "白名单 API 分发"

    ''' <summary>
    ''' 处理扩展发来的方法调用。任何未授权 / 不存在 / 被禁止的方法都抛 ExtApiException。
    ''' 这里没有、也永远不会有任何文件、进程、网络、凭据相关的分支。
    ''' </summary>
    Friend Function HandleRequest(info As ExtInfo, method As String, params As JObject) As JObject
        If params Is Nothing Then params = New JObject()
        If DeniedMethods.Contains(method) OrElse method.StartsWith("network.", StringComparison.OrdinalIgnoreCase) OrElse
           method.StartsWith("file.", StringComparison.OrdinalIgnoreCase) OrElse
           method.StartsWith("process.", StringComparison.OrdinalIgnoreCase) Then
            Log("warn", "扩展 " & info.Id & " 调用了被禁止的方法 " & method & "，已拒绝")
            Throw New ExtApiException(-32001, "permission denied: " & method)
        End If
        If Not MethodPermissions.ContainsKey(method) Then Throw New ExtApiException(-32601, "method not found: " & method)
        Dim need As String = MethodPermissions(method)
        If Not info.Permissions.Contains(need) Then Throw New ExtApiException(-32001, "permission denied: " & method)

        Select Case method
            Case "log"
                Dim level As String = If(params.Value(Of String)("level"), "info")
                Dim message As String = If(params.Value(Of String)("message"), "")
                If message.Length > 4096 Then Throw New ExtApiException(-32602, "日志过长")
                If Not {"info", "warn", "error"}.Contains(level) Then level = "info"
                Log(level, "[" & info.Id & "] " & message)
                Return New JObject()

            Case "toast"
                Dim message As String = If(params.Value(Of String)("message"), "")
                If message.Length > 300 Then Throw New ExtApiException(-32602, "提示过长")
                Toast(message)
                Return New JObject()

            Case "host.info"
                Dim result As JObject = If(HostInfoProvider IsNot Nothing, HostInfoProvider(), New JObject())
                If result Is Nothing Then result = New JObject()
                result("platform") = PlatformName
                result("apiVersion") = ApiVersion
                result("launcherVersion") = If(VersionProvider IsNot Nothing, VersionProvider(), "")
                result("channel") = If(ChannelProvider IsNot Nothing, ChannelProvider(), "")
                result("language") = If(LanguageProvider IsNot Nothing, LanguageProvider(), "")
                result("architecture") = If(Environment.Is64BitOperatingSystem, "x64", "x86")
                Return result

            Case "instances.list"
                Dim list As JArray = If(InstancesProvider IsNot Nothing, InstancesProvider(), New JArray())
                If list Is Nothing Then list = New JArray()
                ' 只保留四个脱敏字段
                Dim clean As New JArray()
                For Each item As JToken In list
                    If Not TypeOf item Is JObject Then Continue For
                    Dim o As JObject = DirectCast(item, JObject)
                    clean.Add(New JObject From {
                        {"id", If(o.Value(Of String)("id"), "")},
                        {"name", If(o.Value(Of String)("name"), "")},
                        {"gameVersion", If(o.Value(Of String)("gameVersion"), "")},
                        {"loader", If(o.Value(Of String)("loader"), "")}})
                Next
                Return New JObject From {{"instances", clean}}

            Case "event.subscribe"
                Dim arr As JArray = TryCast(params.Property("types")?.Value, JArray)
                If arr Is Nothing Then Throw New ExtApiException(-32600, "types 必须是数组")
                Dim accepted As New JArray()
                For Each t As JToken In arr
                    Dim name As String = If(t.Type = JTokenType.String, t.Value(Of String)(), "")
                    If AllowedEvents.Contains(name) AndAlso info.Subscribe.Contains(name) Then accepted.Add(name)
                Next
                Return New JObject From {{"accepted", accepted}}

            Case "storage.get"
                Dim key As String = CheckKey(params.Value(Of String)("key"))
                Dim path As String = ResolveStorage(info, key)
                If Not File.Exists(path) Then Return New JObject From {{"value", Nothing}}
                Dim raw As String = File.ReadAllText(path, Encoding.UTF8)
                Return New JObject From {{"value", JsonConvert.DeserializeObject(Of JToken)(raw)}}

            Case "storage.set"
                Dim key As String = CheckKey(params.Value(Of String)("key"))
                Dim value As JToken = If(params.Property("value") IsNot Nothing, params("value").DeepClone(), JValue.CreateNull())
                Dim text As String = JsonConvert.SerializeObject(value)
                If text.Length > 64 * 1024 Then Throw New ExtApiException(-32002, "存储值超过 64 KiB")
                Dim dir As String = StorageDir(info)
                Dim total As Long = 0
                For Each f As String In Directory.GetFiles(dir, "*.json")
                    total += New FileInfo(f).Length
                Next
                If total + text.Length > MaxStorageBytes Then Throw New ExtApiException(-32002, "扩展存储配额不足")
                Dim path As String = ResolveStorage(info, key)
                Dim temp As String = path & ".tmp"
                File.WriteAllText(temp, text, Encoding.UTF8)
                If File.Exists(path) Then File.Delete(path)
                File.Move(temp, path)
                Return New JObject()

            Case "settings.get"
                Dim key As String = CheckSettingsKey(params.Value(Of String)("key"), info)
                Dim value As Object = Nothing
                If SettingsReader IsNot Nothing Then
                    value = SettingsReader(key)
                Else
                    value = LocalSettings(info)(key)
                End If
                Return New JObject From {{"value", If(value Is Nothing, JValue.CreateNull(), JToken.FromObject(value))}}

            Case "settings.set"
                Dim key As String = CheckSettingsKey(params.Value(Of String)("key"), info)
                Dim value As JToken = If(params.Property("value") IsNot Nothing, params("value").DeepClone(), JValue.CreateNull())
                Dim text As String = JsonConvert.SerializeObject(value)
                If text.Length > MaxSettingsBytes Then Throw New ExtApiException(-32002, "设置值超过 8 KiB")
                If SettingsWriter IsNot Nothing Then
                    SettingsWriter(key, JsonConvert.DeserializeObject(Of Object)(text))
                Else
                    Dim all As JObject = LocalSettings(info)
                    all(key) = value
                    File.WriteAllText(LocalSettingsPath(info), all.ToString(Formatting.None), Encoding.UTF8)
                End If
                Return New JObject()

            Case Else
                Throw New ExtApiException(-32601, "method not found: " & method)
        End Select
    End Function

    Private Function CheckKey(key As String) As String
        If String.IsNullOrEmpty(key) OrElse Not KeyRegex.IsMatch(key) Then Throw New ExtApiException(-32600, "key 不合法")
        Return key
    End Function

    Private Function CheckSettingsKey(key As String, info As ExtInfo) As String
        CheckKey(key)
        If Not key.StartsWith("ext." & info.Id & ".", StringComparison.Ordinal) Then
            Throw New ExtApiException(-32001, "只能读写自身命名空间 ext." & info.Id & ".*")
        End If
        Return key
    End Function

    Private Function StorageDir(info As ExtInfo) As String
        Dim dir As String = Path.Combine(info.PrivateDir, "storage")
        Directory.CreateDirectory(dir)
        Return dir
    End Function

    Private Function ResolveStorage(info As ExtInfo, key As String) As String
        Dim dir As String = StorageDir(info)
        ' 局部变量不要叫 path：VB 大小写不敏感，会遮蔽 System.IO.Path 类型
        Dim target As String = Path.GetFullPath(Path.Combine(dir, key & ".json"))
        Dim root As String = Path.GetFullPath(dir) & Path.DirectorySeparatorChar
        If Not target.StartsWith(root, StringComparison.OrdinalIgnoreCase) Then
            Throw New ExtApiException(-32001, "存储路径越界")
        End If
        Return target
    End Function

    Private Function LocalSettingsPath(info As ExtInfo) As String
        Return Path.Combine(info.PrivateDir, "settings.json")
    End Function

    Private Function LocalSettings(info As ExtInfo) As JObject
        Try
            Dim p As String = LocalSettingsPath(info)
            If File.Exists(p) Then Return JObject.Parse(File.ReadAllText(p, Encoding.UTF8))
        Catch
        End Try
        Return New JObject()
    End Function

#End Region

#Region "工具"

    Friend Function ComputeSha256(path As String) As String
        Using sha As SHA256 = SHA256.Create()
            Using fs As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
                Return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant()
            End Using
        End Using
    End Function

    Friend Function NewNonce() As String
        Dim bytes(15) As Byte
        Using rng As RandomNumberGenerator = RandomNumberGenerator.Create()
            rng.GetBytes(bytes)
        End Using
        Return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant()
    End Function

    ''' <summary>探测系统 PATH 里的解释器；宿主绝不下载或安装运行时。</summary>
    Friend Function FindRuntime(runtime As String) As String()
        Dim candidates As New List(Of String)
        Select Case runtime
            Case "python3"
                candidates.Add("py.exe")
                candidates.Add("python.exe")
                candidates.Add("python3.exe")
            Case "node"
                candidates.Add("node.exe")
            Case "java"
                Dim jh As String = Environment.GetEnvironmentVariable("JAVA_HOME")
                If Not String.IsNullOrEmpty(jh) Then
                    Dim full As String = Path.Combine(jh, "bin", "java.exe")
                    If File.Exists(full) Then Return New String() {full, ""}
                End If
                candidates.Add("java.exe")
        End Select
        Dim pathVar As String = If(Environment.GetEnvironmentVariable("PATH"), "")
        For Each dir As String In pathVar.Split(Path.PathSeparator)
            If String.IsNullOrWhiteSpace(dir) Then Continue For
            For Each exe As String In candidates
                Try
                    Dim full As String = Path.Combine(dir.Trim(""""c), exe)
                    If File.Exists(full) Then
                        If exe = "py.exe" Then Return New String() {full, "-3 "}
                        Return New String() {full, ""}
                    End If
                Catch
                End Try
            Next
        Next
        Return Nothing
    End Function

#End Region

End Module
