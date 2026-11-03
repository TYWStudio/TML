Imports System.IO
Imports System.IO.Compression
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.Json

''' <summary>
''' EasyTier 联机后端。对应 Windows 版 PageLinkMain 的加载器与进程部分。
''' 房间发现走服务端 config.json 的 Link 段（含 DownloadsLinux）。
''' </summary>
Public Module ModLink

    Public Enum LinkStates
        Waiting
        Loading
        Failed
        Finished
    End Enum

    Public Const InviteCodeVersion As Integer = 2

    ''' <summary>EasyTier 所在目录。</summary>
    Public ReadOnly PathEasyTier As String = Path.Combine(ModPaths.Root, "EasyTier")

    '---- 当前房间状态 ----
    Public Property State As LinkStates = LinkStates.Waiting
    Public Property IsServerSide As Boolean
    Public Property ServerPort As Integer
    Public Property ClientPort As Integer
    Public Property RpcPort As Integer
    Public Property NetworkName As String = ""
    Public Property NetworkSecret As String = ""
    Public Property DiscoverNodeID As Integer = -1
    Public Property ClientAddress As String = ""
    Public Property LastError As String = ""
    Public Property Progress As Double = 0
    Public Property StageText As String = ""

    '---- 随机码 ----
    Private ReadOnly Rnd As New Random()

    Public Function GenerateRandomCode() As String
        Dim chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
        Dim result As New StringBuilder
        For i = 1 To 5
            result.Append(chars(Rnd.Next(0, 36)))
        Next
        Return result.ToString().Replace("O", "0").Replace("I", "1")
    End Function

    ''' <summary>创建房间：生成网络名与密码。返回邀请码。</summary>
    Public Function CreateRoom(port As Integer) As String
        IsServerSide = True
        ServerPort = port
        NetworkName = $"P{port.ToString("X").PadLeft(4, "0"c)}-{GenerateRandomCode()}"
        NetworkSecret = GenerateRandomCode()
        DiscoverNodeID = -1
        Return GetInviteCode()
    End Function

    ''' <summary>生成邀请码：P{port十六进制4位}-{随机5}-{密码5}-{版本2}{节点3}。</summary>
    Public Function GetInviteCode() As String
        Dim ver = InviteCodeVersion.ToString().PadLeft(2, "0"c)
        Dim node = If(DiscoverNodeID = -1, 0, DiscoverNodeID)
        Dim hex = node.ToString("X").PadLeft(3, "0"c)
        Return $"{NetworkName}-{NetworkSecret}-{ver}{hex}"
    End Function

    ''' <summary>邀请码格式校验，返回错误信息；合法返回 Nothing。</summary>
    Public Function ValidateCodeFormat(code As String) As String
        If String.IsNullOrWhiteSpace(code) Then Return "邀请码为空！"
        code = FixCodeFormat(code)
        If Not (code.Length >= 14 AndAlso code(0) = "P"c AndAlso code(5) = "-"c AndAlso code(11) = "-"c) Then
            If code.StartsWith("U/") Then Return "请让房主使用 TYW Launcher 创建房间！"
            If code.Length = 10 Then Return "请让房主使用非社区版的 TYW Launcher 创建房间！"
            Return "邀请码有误，请让房主使用 TYW Launcher 创建房间！"
        End If
        If code.Length >= 23 AndAlso code(17) = "-"c Then
            Dim v = 0
            If Integer.TryParse(code.Substring(18, 2), v) AndAlso v > InviteCodeVersion Then
                Return "你的 TYW Launcher 版本太老了，请在更新 TYW Launcher 之后再联机！"
            End If
        End If
        Return Nothing
    End Function

    ''' <summary>从一段消息里提取邀请码并修正常见输入错误。</summary>
    Public Function FixCodeFormat(code As String) As String
        code = code.Trim()
        '从【】/[]内提取
        Dim i1 = code.IndexOf("【"), i2 = code.LastIndexOf("】")
        If i1 >= 0 AndAlso i2 > i1 Then code = code.Substring(i1 + 1, i2 - i1 - 1)
        i1 = code.IndexOf("["c) : i2 = code.LastIndexOf("]"c)
        If i1 >= 0 AndAlso i2 > i1 Then code = code.Substring(i1 + 1, i2 - i1 - 1)
        code = code.ToUpper().Replace("O", "0").Replace("I", "1")
        '版本 1 兼容：没有版本段时补 -0105E
        If code.Length >= 17 AndAlso (code.Length < 23 OrElse (code.Length >= 18 AndAlso code(17) <> "-"c)) Then
            code = code.Substring(0, 17) & "-0105E"
        End If
        Return code
    End Function

    ''' <summary>解析邀请码并设置房间信息。</summary>
    Public Function JoinRoom(rawCode As String) As String
        Dim code = FixCodeFormat(rawCode)
        Dim err = ValidateCodeFormat(code)
        If err IsNot Nothing Then Return err
        IsServerSide = False
        ServerPort = Convert.ToInt32(code.Substring(1, 4), 16)
        NetworkName = code.Substring(0, 11)
        NetworkSecret = code.Substring(12, 5)
        If code.Substring(20, 3) = "000" Then
            DiscoverNodeID = -2
        Else
            DiscoverNodeID = Convert.ToInt32(code.Substring(20, 3), 16)
        End If
        Return Nothing
    End Function

    ''' <summary>CPU 架构（EasyTier 资产命名：x86_64 / aarch64）。</summary>
    Public Function EtArch() As String
        Return If(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture =
                  System.Runtime.InteropServices.Architecture.Arm64, "aarch64", "x86_64")
    End Function

    ''' <summary>
    ''' 准备 EasyTier：本地版本过旧则下载 zip 并解压。每步更新 StageText。
    ''' </summary>
    Public Sub EnsureEasyTier(progress As Action(Of Double, String))
        If Not ModPlatform.SupportsLink Then
            Throw New Exception("macOS 版暂不支持联机（EasyTier 创建 TUN 设备需要管理员权限，正在适配）")
        End If
        Dim cfg = ModServer.Link
        Dim version = If(cfg IsNot Nothing, cfg.EasyTierVersion, 20264)
        progress(0.05, "正在检查联机模块……")
        Directory.CreateDirectory(PathEasyTier)
        Dim corePath = Path.Combine(PathEasyTier, "easytier-core")
        Dim cliPath = Path.Combine(PathEasyTier, "easytier-cli")
        Dim localVersion = ModSettings.GetInt("LinkEasyTierVersion", -1)
        If Not (File.Exists(corePath) AndAlso File.Exists(cliPath)) Then localVersion = -1
        If localVersion >= version Then Return

        progress(0.08, "正在下载联机模块……")
        Dim urls As New List(Of String)
        If cfg IsNot Nothing AndAlso cfg.Downloads.Count > 0 Then
            For Each u In cfg.Downloads
                urls.Add(u.Replace("{arch}", EtArch()))
            Next
        Else
            '内置兜底
            urls.Add($"https://ghfast.top/https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-linux-{EtArch()}-v2.6.4.zip")
            urls.Add($"https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-linux-{EtArch()}-v2.6.4.zip")
        End If

        Dim zipPath = Path.Combine(ModPaths.TempDir, "EasyTier.zip")
        Dim ok = False, lastEx As Exception = Nothing
        For Each url In urls
            Try
                If ModNet.DownloadFile(url, zipPath, Sub(p) progress(0.08 + 0.3 * p, "正在下载联机模块… " & CInt(p * 100) & "%")) Then
                    If New FileInfo(zipPath).Length > 1024 * 1024 * 2 Then
                        ok = True
                        Exit For
                    End If
                End If
            Catch ex As Exception
                lastEx = ex
            End Try
        Next
        If Not ok Then Throw New Exception("下载联机模块失败：" & If(lastEx?.Message, "所有下载源均不可用"))

        progress(0.42, "正在解压联机模块……")
        Dim extract = Path.Combine(ModPaths.TempDir, "EasyTierExtract-" & Path.GetRandomFileName().Substring(0, 4))
        ZipFile.ExtractToDirectory(zipPath, extract, True)
        'zip 里有顶层目录，找到 easytier-core 所在目录
        Dim srcDir = extract
        If Not File.Exists(Path.Combine(srcDir, "easytier-core")) Then
            For Each d In Directory.GetDirectories(extract)
                If File.Exists(Path.Combine(d, "easytier-core")) Then
                    srcDir = d
                    Exit For
                End If
            Next
        End If
        For Each name In {"easytier-core", "easytier-cli"}
            Dim src = Path.Combine(srcDir, name)
            If File.Exists(src) Then
                Dim dst = Path.Combine(PathEasyTier, name)
                If File.Exists(dst) Then File.Delete(dst)
                File.Move(src, dst)
                ChmodExecutable(dst)
            End If
        Next
        Try
            Directory.Delete(extract, True)
            File.Delete(zipPath)
        Catch
        End Try
        If Not File.Exists(corePath) Then Throw New Exception("解压后的联机模块缺少 easytier-core")
        ModSettings.SetValue("LinkEasyTierVersion", version.ToString())
        ModSettings.Save()
    End Sub

    Private Sub ChmodExecutable(path As String)
        Try
            Using p = System.Diagnostics.Process.Start(New System.Diagnostics.ProcessStartInfo With
                {.FileName = "chmod", .Arguments = "+x """ & path.Replace("""", "\""") & """", .UseShellExecute = False})
                p?.WaitForExit(3000)
            End Using
        Catch
        End Try
    End Sub

    ''' <summary>获取节点列表（简单版：服务端 Peers + 发现节点 + 强制节点）。</summary>
    Public Function GetTargetPeers() As List(Of String)
        Dim cfg = ModServer.Link
        Dim peers As New List(Of String)
        Dim fallbackPeer = If(cfg IsNot Nothing AndAlso cfg.DiscoverPeer <> "", cfg.DiscoverPeer, "tcp://boi.de5.net:11010")
        Dim fallbackId = If(cfg IsNot Nothing, cfg.DiscoverPeerId, 0)
        If cfg IsNot Nothing AndAlso cfg.Peers.Count > 0 Then peers.AddRange(cfg.Peers)
        peers.Add(fallbackPeer)
        If cfg IsNot Nothing Then peers.AddRange(cfg.MandatoryPeers)
        DiscoverNodeID = If(DiscoverNodeID = -2 OrElse DiscoverNodeID > 0, DiscoverNodeID, fallbackId)
        Return peers.Distinct().ToList()
    End Function

    ''' <summary>获取从 startPort 起 count 个空闲端口（避开 startPort 本身）。</summary>
    Public Function FindFreePorts(count As Integer, avoid As Integer) As List(Of Integer)
        Dim result As New List(Of Integer)
        Dim port = 1024
        While result.Count < count
            If port = avoid Then port += 1 : Continue While
            Try
                Dim l = New TcpListener(IPAddress.Loopback, port)
                l.Start()
                l.Stop()
                result.Add(port)
            Catch
            End Try
            port += 1
        End While
        Return result
    End Function

    ''' <summary>拼 EasyTier 启动参数并启动进程。</summary>
    Public Sub StartCore()
        Dim free = FindFreePorts(5, ServerPort)
        ClientPort = free(0)
        RpcPort = free(1)
        Dim listeners = free(2)

        Dim args As String = ""
        If ModServer.Link IsNot Nothing AndAlso Not String.IsNullOrEmpty(ModServer.Link.Argument) Then
            args &= ModServer.Link.Argument & " "
        End If
        args &= $"--network-name={NetworkName} --network-secret={NetworkSecret} --listeners {listeners} --rpc-portal {RpcPort} --private-mode true"
        Dim hostName = If(IsServerSide, "Server-", "Client-") &
            Math.Abs(NetworkName.GetHashCode()).ToString("X").PadLeft(6, "0"c).Substring(0, 6)
        If IsServerSide Then
            args &= $" -i 10.114.114.114 --hostname={hostName} --tcp-whitelist={ServerPort} --udp-whitelist={ServerPort}"
            ClientAddress = ""
        Else
            args &= $" -d --hostname={hostName} --tcp-whitelist=0 --udp-whitelist=0"
            ClientAddress = $"localhost:{ClientPort}"
            args &= $" --port-forward tcp://127.0.0.1:{ClientPort}/10.114.114.114:{ServerPort}"
            args &= $" --port-forward udp://127.0.0.1:{ClientPort}/10.114.114.114:{ServerPort}"
        End If
        For Each peer In GetTargetPeers()
            args &= $" -p=""{peer}"""
        Next
        ' 延迟优先（设置 → 联机 → 延迟优化），对应 Windows 版的 LinkLatencyMode
        If ModSettings.GetInt("LinkLatencyMode", 0) = 1 Then args &= " --latency-first"

        Dim psi As New System.Diagnostics.ProcessStartInfo With
        {
            .FileName = Path.Combine(PathEasyTier, "easytier-core"),
            .Arguments = args,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True
        }
        ModLog.Info("启动 EasyTier：" & args)
        CoreProcess = System.Diagnostics.Process.Start(psi)
        If CoreProcess Is Nothing Then Throw New Exception("联机模块启动失败")
        '★ 必须用异步事件把 stdout/stderr 持续读走。
        '之前用 StandardOutput.Peek() 轮询：没有新输出时 Peek 会一直阻塞，
        '等待连接的循环就此卡死在「正在连接……」，而且 stderr 没人读，管道写满后 EasyTier 自己也会阻塞。
        AddHandler CoreProcess.OutputDataReceived, Sub(s, e)
                                                       If e.Data IsNot Nothing Then ProcessLogLine(e.Data)
                                                   End Sub
        AddHandler CoreProcess.ErrorDataReceived, Sub(s, e)
                                                      If e.Data IsNot Nothing Then ProcessLogLine(e.Data)
                                                  End Sub
        CoreProcess.BeginOutputReadLine()
        CoreProcess.BeginErrorReadLine()
    End Sub

    Public CoreProcess As System.Diagnostics.Process = Nothing
    Public ReadOnly LogHistory As New Queue(Of String)(10)

    Public Sub ProcessLogLine(line As String)
        SyncLock LogHistory
            LogHistory.Enqueue(line)
            While LogHistory.Count > 10
                LogHistory.Dequeue()
            End While
        End SyncLock
    End Sub

    ''' <summary>兼容旧调用：日志已改为异步事件读取，这里只把进程状态同步进日志。</summary>
    Public Sub ReadCoreLogs()
        '输出由 OutputDataReceived/ErrorDataReceived 异步收集，不再需要主动轮询（Peek 会阻塞）
    End Sub

    ''' <summary>EasyTier 是否已退出（崩溃）。</summary>
    Public Function HasCrashed() As Boolean
        Return CoreProcess Is Nothing OrElse CoreProcess.HasExited
    End Function

    ''' <summary>停止 EasyTier 进程。</summary>
    Public Sub ProcessStop()
        Try
            If CoreProcess IsNot Nothing AndAlso Not CoreProcess.HasExited Then
                CoreProcess.Kill(True)
                ModLog.Info("已停止联机模块")
            End If
        Catch
        End Try
        CoreProcess = Nothing
    End Sub

    '---- 节点信息（CLI） ----

    Public Class Peer
        Public Property Type As String = "Misc" 'Client / Server / Misc / Self
        Public Property Ping As Double = 0
        Public Property Name As String = ""
        Public Property Relay As Boolean = False
        Public Property NatType As Integer = 0
    End Class

    Public Property Peers As List(Of Peer) = Nothing
    Public Property NatTypeSelf As Integer = 0
    Public Property CliFailCount As Integer = 0

    ''' <summary>调用 easytier-cli 拉取节点信息并解析。</summary>
    Public Sub RefreshPeers()
        Try
            Dim psi As New System.Diagnostics.ProcessStartInfo With
            {
                .FileName = Path.Combine(PathEasyTier, "easytier-cli"),
                .Arguments = $"-o json -p 127.0.0.1:{RpcPort} peer",
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True
            }
            Using proc = System.Diagnostics.Process.Start(psi)
                If proc Is Nothing Then Throw New Exception("CLI 启动失败")
                Dim outTask = proc.StandardOutput.ReadToEndAsync()
                If Not outTask.Wait(2000) Then
                    Try : proc.Kill() : Catch : End Try
                    Throw New Exception("联机模块 CLI 调用超时")
                End If
                Dim result = outTask.Result
                If Not result.Contains("lat_ms") Then Throw New Exception("CLI 调用失败：" & result)
                Dim newPeers As New List(Of Peer)
                Using doc = JsonDocument.Parse(result)
                    For Each item In doc.RootElement.EnumerateArray()
                        Try
                            Dim peer As New Peer
                            Dim name = ""
                            Dim nameEl As JsonElement
                            If item.TryGetProperty("hostname", nameEl) Then name = nameEl.GetString()
                            Dim costEl As JsonElement
                            Dim cost = If(item.TryGetProperty("cost", costEl), costEl.GetString(), "")
                            If cost = "Local" Then
                                peer.Type = "Self"
                            ElseIf name.StartsWith("Client") Then
                                peer.Type = "Client"
                            ElseIf item.TryGetProperty("ipv4", nameEl) AndAlso nameEl.GetString().StartsWith("10.114.114.114") Then
                                peer.Type = "Server"
                            End If
                            Dim latEl As JsonElement
                            If item.TryGetProperty("lat_ms", latEl) Then
                                Double.TryParse(latEl.GetString(), Globalization.NumberStyles.Any,
                                                Globalization.CultureInfo.InvariantCulture, peer.Ping)
                            End If
                            peer.Name = name
                            peer.Relay = cost.ToLowerInvariant().Contains("relay")
                            If peer.Type = "Self" Then
                                NatTypeSelf = peer.NatType
                            Else
                                newPeers.Add(peer)
                            End If
                        Catch
                        End Try
                    Next
                End Using
                Peers = newPeers
                CliFailCount = 0
            End Using
        Catch ex As Exception
            ModLog.Info("获取节点信息失败：" & ex.Message)
            If State = LinkStates.Finished Then
                CliFailCount += 1
            End If
        End Try
    End Sub

    ''' <summary>目标节点（客户端=房主，服务端=任意已连接节点）；没有返回 Nothing。</summary>
    Public Function GetTargetPeer() As Peer
        If Peers Is Nothing Then Return Nothing
        Dim best As Peer = Nothing
        For Each p In Peers
            If p.Ping <= 0 Then Continue For
            If Not IsServerSide AndAlso p.Type <> "Server" Then Continue For
            If best Is Nothing OrElse p.Ping < best.Ping Then best = p
        Next
        Return best
    End Function

    Public Function GetPeerPing() As Double
        Dim p = GetTargetPeer()
        Return If(p Is Nothing, 0, p.Ping)
    End Function

    ''' <summary>房间里的人数（含自己）。</summary>
    Public Function PeopleCount() As Integer
        If Peers Is Nothing Then Return 0
        Return Peers.Where(Function(p) p.Type <> "Misc").Count() + 1
    End Function

    ''' <summary>向 4445 端口广播房间端口，让同机玩家的多人游戏列表直接看到房间。</summary>
    Public Sub BroadcastRoom()
        If IsServerSide Then Return
        Try
            Using udp As New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                Dim data = Encoding.UTF8.GetBytes(
                    $"[MOTD]TYW Launcher 联机房间[/MOTD][AD]{ClientPort}[/AD]")
                udp.SendTo(data, New IPEndPoint(IPAddress.Loopback, 4445))
            End Using
        Catch
        End Try
    End Sub

End Module
