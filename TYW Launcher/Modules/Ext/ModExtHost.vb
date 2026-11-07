Imports System.IO.Compression
Imports System.Text
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' 单个扩展的子进程宿主。职责只有：安全地启动进程、收发 NDJSON、隔离崩溃。
''' 所有能力判定都在 ModExt.HandleRequest 里，这里不提供任何额外接口。
'''
''' 子进程：
'''  * 工作目录 = 扩展私有目录；
'''  * 环境变量全部清空后只重设最小白名单（PATH / SystemRoot / 临时目录 / 语言 /
'''    PYTHONIOENCODING=utf-8），NODE_OPTIONS、PYTHONPATH、JAVA_TOOL_OPTIONS、
'''    CLASSPATH、LD_PRELOAD 等注入变量一律不带，启动器内部变量一律不带；
'''  * 不继承启动器进程句柄、不提升权限、不执行 shell。
''' </summary>
Friend Class ExtHost

    Private ReadOnly Info As ExtInfo
    Private Proc As Process = Nothing
    Private StdIn As StreamWriter = Nothing
    Private ReadOnly SendLock As New Object
    Private ReadOnly Handshake As New Threading.ManualResetEventSlim(False)
    Private Ready As Boolean = False
    Private Stopping As Boolean = False
    Private ExitReported As Boolean = False
    Private ReadOnly Nonce As String = ModExt.NewNonce()

    Public ReadOnly Property IsAlive As Boolean
        Get
            Try
                Return Not Stopping AndAlso Proc IsNot Nothing AndAlso Not Proc.HasExited AndAlso Ready
            Catch
                Return False
            End Try
        End Get
    End Property

    Public Sub New(info As ExtInfo)
        Me.Info = info
    End Sub

#Region "启动"

    Public Sub Start()
        Dim exe As String = ""
        Dim prefix As String = ""
        Dim args As String = ""

        If ModExt.IsNativeRuntime(Info.Runtime) Then
            ' 原生可执行（C/C++/Rust/Go 等编译产物）：解压后直接拉起包内二进制，不套解释器
            args = ""
        Else
            Dim runtime As String() = ModExt.FindRuntime(Info.Runtime)
            If runtime Is Nothing Then
                Throw New Exception("缺少运行时：" & Info.Runtime & "（宿主不会自动下载或安装）")
            End If
            exe = runtime(0)
            prefix = runtime(1)
        End If

        Directory.CreateDirectory(Info.PrivateDir)

        If Info.Format = "tmlext" Then
            Dim pkgDir As String = Info.PackageDir
            ExtractPackage(pkgDir)
            Dim entryPath As String = Path.GetFullPath(Path.Combine(pkgDir, Info.Entry.Replace("/"c, Path.DirectorySeparatorChar)))
            If Not File.Exists(entryPath) AndAlso Environment.OSVersion.Platform = PlatformID.Win32NT Then
                Dim tryExe As String = entryPath & ".exe"
                If File.Exists(tryExe) Then entryPath = tryExe
            End If
            Dim root As String = Path.GetFullPath(pkgDir) & Path.DirectorySeparatorChar
            If Not entryPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) OrElse Not File.Exists(entryPath) Then
                Throw New Exception("entry 不存在或越界：" & Info.Entry)
            End If
            If ModExt.IsNativeRuntime(Info.Runtime) Then
                ' Unix 下补执行位；Windows 下 .exe 自带
                If Environment.OSVersion.Platform <> PlatformID.Win32NT Then
                    Try
                        Dim chmodPsi As New ProcessStartInfo("chmod", "+x """ & entryPath & """") With {.UseShellExecute = False, .CreateNoWindow = True}
                        Dim chmodProc As Process = Process.Start(chmodPsi)
                        If chmodProc IsNot Nothing Then chmodProc.WaitForExit()
                    Catch
                    End Try
                End If
                exe = entryPath
                args = ""
            Else
                args = prefix & """" & entryPath & """"
            End If
        Else
            ' jar 不解压到宿主进程，直接交给独立 JVM；绝不 ClassLoader / 反射加载
            args = "-cp """ & Info.FilePath & """ " & Info.Entry
        End If

        Dim psi As New ProcessStartInfo()
        psi.FileName = exe
        psi.Arguments = args
        psi.WorkingDirectory = Info.PrivateDir
        psi.UseShellExecute = False
        psi.CreateNoWindow = True
        psi.RedirectStandardInput = True
        psi.RedirectStandardOutput = True
        psi.RedirectStandardError = True
        psi.StandardOutputEncoding = Encoding.UTF8
        psi.StandardErrorEncoding = Encoding.UTF8
        ' 清空环境：扩展进程不继承启动器的任何环境变量
        psi.EnvironmentVariables.Clear()
        For Each key As String In New String() {"PATH", "Path", "SystemRoot", "TEMP", "TMP",
                                               "USERPROFILE", "APPDATA", "LOCALAPPDATA", "LANG", "LC_ALL"}
            Dim value As String = Environment.GetEnvironmentVariable(key)
            If Not String.IsNullOrEmpty(value) Then psi.EnvironmentVariables(key) = value
        Next
        ' 向扩展进程下发 TML 协议配置（宿主自身设置，非继承，安全）；所有运行时通用，方便 C 等原生扩展读取
        psi.EnvironmentVariables("TML_EXT_ID") = Info.Id
        psi.EnvironmentVariables("TML_EXT_API") = ModExt.ApiVersion.ToString()
        psi.EnvironmentVariables("TML_EXT_PLATFORM") = ModExt.PlatformName
        psi.EnvironmentVariables("TML_EXT_LAUNCHER_VERSION") = GetVersion()
        psi.EnvironmentVariables("TML_EXT_CHANNEL") = GetChannel()
        psi.EnvironmentVariables("TML_EXT_LANGUAGE") = Threading.Thread.CurrentThread.CurrentUICulture.Name
        psi.EnvironmentVariables("TML_EXT_PRIVATE") = Info.PrivateDir
        psi.EnvironmentVariables("TML_EXT_NONCE") = Nonce
        psi.EnvironmentVariables("TML_EXT_PERMISSIONS") = String.Join(",", Info.Permissions)
        psi.EnvironmentVariables("PYTHONIOENCODING") = "utf-8"
        ' 以下变量即使存在于当前进程也绝不传递（可用来注入任意代码）
        For Each key As String In New String() {"NODE_OPTIONS", "PYTHONPATH", "PYTHONHOME",
                                               "PYTHONSTARTUP", "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS",
                                               "JDK_JAVA_OPTIONS", "CLASSPATH", "LD_PRELOAD",
                                               "DYLD_INSERT_LIBRARIES", "CDPATH", "TYW_SERVER",
                                               "TYW_SERVER_URL", "TYW_SNAPSHOT"}
            If psi.EnvironmentVariables.ContainsKey(key) Then psi.EnvironmentVariables.Remove(key)
        Next

        Proc = Process.Start(psi)
        If Proc Is Nothing Then Throw New Exception("无法启动扩展进程")
        StdIn = New StreamWriter(Proc.StandardInput.BaseStream, New UTF8Encoding(False)) With {.AutoFlush = True}

        Dim outThread As New Threading.Thread(AddressOf ReadStdOut) With {.IsBackground = True}
        Dim errThread As New Threading.Thread(AddressOf ReadStdErr) With {.IsBackground = True}
        outThread.Start()
        errThread.Start()

        Dim init As New JObject()
        init("id") = 1
        init("method") = "initialize"
        Dim p As New JObject()
        p("apiVersion") = ModExt.ApiVersion
        p("extId") = Info.Id
        p("platform") = ModExt.PlatformName
        p("launcherVersion") = GetVersion()
        p("channel") = GetChannel()
        p("language") = Threading.Thread.CurrentThread.CurrentUICulture.Name
        p("privateDir") = Info.PrivateDir
        p("permissions") = JArray.FromObject(Info.Permissions)
        p("nonce") = Nonce
        init("params") = p
        SendObject(init)

        If Not Handshake.Wait(10000) OrElse Not Ready Then
            Shutdown()
            Throw New Exception("扩展握手超时或未响应 initialize")
        End If
        ModExt.Log("info", "扩展 " & Info.Id & " 已启动（" & Info.Runtime & "）")
    End Sub

    Private Function GetVersion() As String
        Try
            Return If(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(), "")
        Catch
            Return ""
        End Try
    End Function

    Private Function GetChannel() As String
#If SNAPSHOT Then
        Return "Snapshot"
#ElseIf BETA Then
        Return "Beta"
#Else
        Return "Release"
#End If
    End Function

    ''' <summary>安全解压 .tmlext：路径规范化校验，禁止 ZIP Slip / 绝对路径 / 符号链接。</summary>
    Private Sub ExtractPackage(target As String)
        If Directory.Exists(target) Then Directory.Delete(target, True)
        Directory.CreateDirectory(target)
        Dim root As String = Path.GetFullPath(target) & Path.DirectorySeparatorChar
        Using zip As ZipArchive = ZipFile.OpenRead(Info.FilePath)
            For Each entry As ZipArchiveEntry In zip.Entries
                If Not ModExt.SafeEntryName(entry.FullName) Then Throw New Exception("包内路径不合法：" & entry.FullName)
                Dim rel As String = entry.FullName.Replace("/"c, Path.DirectorySeparatorChar)
                Dim dest As String = Path.GetFullPath(Path.Combine(target, rel))
                If Not dest.StartsWith(root, StringComparison.OrdinalIgnoreCase) Then Throw New Exception("ZIP Slip 已拦截")
                If String.IsNullOrEmpty(entry.Name) Then
                    Directory.CreateDirectory(dest)
                    Continue For
                End If
                Directory.CreateDirectory(Path.GetDirectoryName(dest))
                Using src As Stream = entry.Open()
                    Using dst As New FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None)
                        src.CopyTo(dst)
                    End Using
                End Using
            Next
        End Using
    End Sub

#End Region

#Region "协议收发"

    Private Sub SendObject(obj As JObject)
        Dim line As String = obj.ToString(Formatting.None)
        If Encoding.UTF8.GetByteCount(line) > ModExt.MaxLineBytes Then Return
        SyncLock SendLock
            If StdIn Is Nothing Then Return
            StdIn.WriteLine(line)
            StdIn.Flush()
        End SyncLock
    End Sub

    Friend Sub SendEvent(eventType As String, data As JObject)
        If Not IsAlive Then Return
        Dim obj As New JObject()
        obj("method") = "event"
        Dim p As New JObject()
        p("type") = eventType
        p("data") = If(data, New JObject())
        obj("params") = p
        SendObject(obj)
    End Sub

    Private Sub SendResult(id As JToken, result As JObject)
        Dim obj As New JObject()
        obj("id") = If(id, JValue.CreateNull())
        obj("result") = If(result, New JObject())
        SendObject(obj)
    End Sub

    Private Sub SendError(id As JToken, code As Integer, message As String)
        Dim obj As New JObject()
        obj("id") = If(id, JValue.CreateNull())
        Dim e As New JObject()
        e("code") = code
        e("message") = If(message, "host error")
        obj("error") = e
        SendObject(obj)
    End Sub

    Private Sub ReadStdOut()
        Try
            Dim reader As New StreamReader(Proc.StandardOutput.BaseStream, Encoding.UTF8, False, 4096)
            Dim line As String = reader.ReadLine()
            Do While line IsNot Nothing
                If Encoding.UTF8.GetByteCount(line) <= ModExt.MaxLineBytes Then HandleLine(line)
                line = reader.ReadLine()
            Loop
        Catch ex As Exception
            If Not Stopping Then ModExt.Log("warn", "扩展 " & Info.Id & " 输出异常：" & ex.Message)
        Finally
            ReportExit("扩展进程已退出")
        End Try
    End Sub

    Private Sub ReadStdErr()
        Try
            Dim reader As New StreamReader(Proc.StandardError.BaseStream, Encoding.UTF8, False, 4096)
            Dim line As String = reader.ReadLine()
            Do While line IsNot Nothing
                If line.Length > 500 Then line = line.Substring(0, 500)
                ModExt.Log("warn", "[" & Info.Id & "] " & line)
                line = reader.ReadLine()
            Loop
        Catch
        End Try
    End Sub

    Private Sub HandleLine(line As String)
        If String.IsNullOrWhiteSpace(line) Then Return
        Dim msg As JObject
        Try
            msg = JObject.Parse(line)
        Catch
            Return                       ' 非协议行直接忽略，绝不解析成命令
        End Try
        If msg.Property("method") IsNot Nothing Then
            Dim method As String = If(msg.Value(Of String)("method"), "")
            Dim id As JToken = msg.Property("id")?.Value
            If id Is Nothing Then Return  ' 事件通知由宿主发出，扩展不允许反向广播
            Dim params As JObject = TryCast(msg.Property("params")?.Value, JObject)
            Threading.ThreadPool.QueueUserWorkItem(
                Sub(state As Object)
                    Try
                        ' 变量别叫 task：VB 大小写不敏感，会遮蔽 Task 类型
                        Dim work As Task(Of JObject) = System.Threading.Tasks.Task.Factory.StartNew(
                            Function() ModExt.HandleRequest(Info, method, params))
                        If Not work.Wait(5000) Then
                            SendError(id, -32000, "host timeout")
                            Return
                        End If
                        SendResult(id, work.Result)
                    Catch ex As AggregateException
                        Dim inner As Exception = If(ex.InnerException, ex)
                        ReplyException(id, inner)
                    Catch ex As Exception
                        ReplyException(id, ex)
                    End Try
                End Sub)
            Return
        End If
        If msg.Property("id") IsNot Nothing AndAlso Convert.ToString(msg.Property("id").Value) = "1" Then
            Dim result As JObject = TryCast(msg.Property("result")?.Value, JObject)
            If result IsNot Nothing AndAlso result.Value(Of Boolean?)("ready") = True Then
                Ready = True
                Handshake.Set()
            End If
        End If
    End Sub

    Private Sub ReplyException(id As JToken, ex As Exception)
        If TypeOf ex Is ExtApiException Then
            Dim api As ExtApiException = DirectCast(ex, ExtApiException)
            SendError(id, api.Code, api.Message)
        Else
            ModExt.Log("warn", "扩展 " & Info.Id & " 请求处理失败：" & ex.Message)
            SendError(id, -32000, "host error")
        End If
    End Sub

    Private Sub ReportExit(reason As String)
        If ExitReported Then Return
        ExitReported = True
        Ready = False
        Handshake.Set()
        If Not Stopping Then
            Try
                ModExt.OnHostExited(Info, reason)
            Catch
            End Try
        End If
    End Sub

#End Region

#Region "停止"

    Public Sub Shutdown()
        If Stopping Then Return
        Stopping = True
        Try
            Dim obj As New JObject()
            obj("method") = "shutdown"
            SendObject(obj)
        Catch
        End Try
        Try
            If StdIn IsNot Nothing Then StdIn.Close()
        Catch
        End Try
        Try
            If Proc IsNot Nothing AndAlso Not Proc.HasExited Then
                If Not Proc.WaitForExit(1500) Then Proc.Kill()
            End If
        Catch
        End Try
        Ready = False
        Handshake.Set()
    End Sub

#End Region

End Class
