Imports System.IO
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text

''' <summary>
''' TYW 自建服务端交互。对应 Windows 版 ModSecret：
''' 设备码 / 封禁查询 / 错误匿名上报（断网缓存补报）/ 公告弹窗指纹。
''' 没网 / 连不上服务器不算错误：不弹框、不上报，调用方只做左下角提示。
''' </summary>
Public Module ModSecret

    ''' <summary>错误上报缓存目录（断网时先落盘，联网后补报）。</summary>
    Public ReadOnly ReportCacheDir As String = Path.Combine(ModPaths.TempDir, "ReportCache")

    Private ReadOnly ReportLock As New Object()

#Region "设备码"

    Private _deviceCode As String = Nothing

    ''' <summary>
    ''' 展示用设备识别码（形如 A1B2-C3D4-E5F6-7890）。
    ''' Linux：/etc/machine-id；macOS：IOPlatformUUID（ioreg）——同一台机器稳定不变，不含明文个人信息。
    ''' </summary>
    Public ReadOnly Property DeviceCode As String
        Get
            If _deviceCode IsNot Nothing Then Return _deviceCode
            Try
                Dim seed As New StringBuilder()
                If ModPlatform.IsMac Then
                    seed.Append(MacPlatformUuid())
                Else
                    For Each p In {"/etc/machine-id", "/var/lib/dbus/machine-id"}
                        Try
                            If File.Exists(p) Then
                                seed.Append(File.ReadAllText(p).Trim())
                                Exit For
                            End If
                        Catch
                        End Try
                    Next
                End If
                seed.Append("|").Append(Environment.MachineName)
                seed.Append("|").Append(Environment.UserName)
                Using md5 = System.Security.Cryptography.MD5.Create()
                    Dim hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed.ToString()))
                    Dim hex = BitConverter.ToString(hash).Replace("-", "").Substring(0, 16).ToUpperInvariant()
                    _deviceCode = $"{hex.Substring(0, 4)}-{hex.Substring(4, 4)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}"
                End Using
            Catch ex As Exception
                ModLog.Err(ex, "生成设备识别码")
                _deviceCode = "0000-0000-0000-0000"
            End Try
            Return _deviceCode
        End Get
    End Property

    ''' <summary>macOS 的硬件 UUID（ioreg -c IOPlatformExpertDevice 里的 IOPlatformUUID）。取不到返回空串。</summary>
    Private Function MacPlatformUuid() As String
        Try
            Dim psi As New System.Diagnostics.ProcessStartInfo With {
                .FileName = "/usr/sbin/ioreg",
                .Arguments = "-rd1 -c IOPlatformExpertDevice",
                .UseShellExecute = False,
                .RedirectStandardOutput = True,
                .CreateNoWindow = True
            }
            Using p = System.Diagnostics.Process.Start(psi)
                If p Is Nothing Then Return ""
                Dim out = p.StandardOutput.ReadToEnd()
                p.WaitForExit(3000)
                Dim m = System.Text.RegularExpressions.Regex.Match(out, "IOPlatformUUID""\s*=\s*""([^""]+)""")
                If m.Success Then Return m.Groups(1).Value
            End Using
        Catch
        End Try
        Return ""
    End Function

#End Region

#Region "封禁查询"

    ''' <summary>
    ''' 启动时向服务端查询本设备是否被封禁（GET /ban?ident=设备码）。
    ''' 被封禁返回封禁原因；未封禁返回空字符串；网络失败返回 Nothing（放行，避免断网无法使用）。
    ''' </summary>
    Public Function CheckDeviceBanned() As String
        Try
            Dim raw = ModNet.GetServer("ban?ident=" & Uri.EscapeDataString(DeviceCode), 8000)
            If String.IsNullOrWhiteSpace(raw) Then Return Nothing
            Using doc = System.Text.Json.JsonDocument.Parse(raw)
                Dim root = doc.RootElement
                Dim bannedEl As System.Text.Json.JsonElement
                If root.TryGetProperty("banned", bannedEl) AndAlso bannedEl.GetBoolean() Then
                    Dim reasonEl As System.Text.Json.JsonElement
                    Return If(root.TryGetProperty("reason", reasonEl), reasonEl.GetString(), "")
                End If
            End Using
        Catch ex As Exception
            Try : ModLog.Info("封禁状态查询失败（已放行）：" & ex.Message) : Catch : End Try
        End Try
        Return Nothing
    End Function

#End Region

#Region "错误匿名上报"

    ''' <summary>
    ''' 把任意内容上报到服务端（POST /report）。先写入本地缓存再异步发送：
    ''' 发送成功即删除缓存；断网则留在 ReportCache，联网后启动时自动补报。
    ''' </summary>
    Public Sub ReportToServer(title As String, detail As String)
        Try
            Dim report =
                title & vbCrLf &
                "时间：" & Date.Now.ToString("yyyy-MM-dd HH:mm:ss") & vbCrLf &
                "版本：" & ModApp.VersionBaseName & "（版本码 " & ModApp.VersionCode & "）" & vbCrLf &
                "平台：" & ModPlatform.DisplayName & "（" & ModApp.RuntimeArch & "）" & vbCrLf &
                "设备码：" & DeviceCode & vbCrLf &
                New String("="c, 48) & vbCrLf &
                If(detail, "")
            '★ 先落盘：即使进程随后崩溃或断网也不会丢
            SavePendingReport(report)
            System.Threading.Tasks.Task.Run(Sub() FlushPendingReports())
        Catch
        End Try
    End Sub

    Public Sub ReportErrorToServer(detail As String, Optional kind As String = "错误")
        ReportToServer("TYW Launcher " & ModPlatform.DisplayName & " 匿名上报（" & kind & "）", If(detail, ""))
    End Sub

    Private Sub SavePendingReport(content As String)
        Try
            Directory.CreateDirectory(ReportCacheDir)
            Dim name = "report-" & Date.Now.ToString("yyyyMMddHHmmss") & "-" & Path.GetRandomFileName().Substring(0, 4) & ".log"
            File.WriteAllText(Path.Combine(ReportCacheDir, name), content, Encoding.UTF8)
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' 把离线缓存的上报按时间顺序补发到服务端，成功一条删一条；一旦失败就停下，等下次连上再试。
    ''' </summary>
    Public Sub FlushPendingReports()
        SyncLock ReportLock
            Try
                If Not Directory.Exists(ReportCacheDir) Then Return
                Dim files = Directory.GetFiles(ReportCacheDir, "report-*.log")
                If files.Length = 0 Then Return
                Array.Sort(files, StringComparer.OrdinalIgnoreCase)
                Dim sent = 0
                For Each f In files
                    Dim content As String = Nothing
                    Try
                        content = File.ReadAllText(f, Encoding.UTF8)
                    Catch
                        Continue For
                    End Try
                    If content = "" Then
                        Try : File.Delete(f) : Catch : End Try
                        Continue For
                    End If
                    If TryPostReport(content) Then
                        Try : File.Delete(f) : Catch : End Try
                        sent += 1
                    Else
                        Exit For '还是连不上，保留剩余记录等下次
                    End If
                Next
                If sent > 0 Then ModLog.Info("已上报 " & sent & " 条记录到服务端（含离线补报）")
            Catch ex As Exception
                ModLog.Err(ex, "补报离线记录失败")
            End Try
        End SyncLock
    End Sub

    Private Function TryPostReport(content As String) As Boolean
        Try
            Using req As New HttpRequestMessage(HttpMethod.Post, ModNet.ServerAddress & "report")
                req.Content = New StringContent(content, Encoding.UTF8, "text/plain")
                Using resp = ModNet.PostClient.SendAsync(req).GetAwaiter().GetResult()
                    Return resp.IsSuccessStatusCode
                End Using
            End Using
        Catch ex As Exception
            ModLog.Info("上报失败（已缓存）：" & ex.Message)
            Return False
        End Try
    End Function

#End Region

End Module
