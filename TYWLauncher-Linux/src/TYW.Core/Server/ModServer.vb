Imports System.IO
Imports System.Text.Json

''' <summary>服务端公告。</summary>
Public Class NoticeData
    Public Property Title As String = ""
    Public Property Code As String = ""
    Public Property Content As String = ""
    Public Property Detail As String = ""
End Class

''' <summary>EasyTier 联机配置（config.json 的 Link 段）。</summary>
Public Class LinkConfig
    Public Property EasyTierVersion As Integer = 0
    Public Property Downloads As New List(Of String)
    Public Property Argument As String = ""
    Public Property DiscoverPeerId As Integer = 0
    Public Property DiscoverPeer As String = ""
    Public Property RandomPeer As Integer = 0
    Public Property Peers As New List(Of String)
    Public Property MandatoryPeers As New List(Of String)
End Class

''' <summary>
''' 服务端接口。对应 Windows 版 ModServer / ModSecret 的公告与云控部分。
''' </summary>
Public Module ModServer

    ''' <summary>Notice.cfg 原始十段：标题|快照码|正式码|正式公告|快照公告|内测码|内测公告|正式详情|快照详情|内测详情。</summary>
    Private _segments As String() = Nothing

    ''' <summary>云控 settings（config.json 的 settings 段，服务端强制下发）。</summary>
    Private _cloudSettings As Dictionary(Of String, String) = Nothing

    ''' <summary>联机配置（config.json 的 Link 段）。</summary>
    Public Link As LinkConfig = Nothing

    Public ReadOnly Property HasServerData As Boolean
        Get
            Return _segments IsNot Nothing
        End Get
    End Property

    ''' <summary>拉取并解析 Notice.cfg + config.json。成功返回 True，失败（没网等）返回 False。</summary>
    Public Function Refresh() As Boolean
        Try
            Dim raw = ModNet.GetServerStrict("Notice.cfg")
            Dim parts = raw.Split("|"c)
            If parts.Length < 3 Then Throw New Exception("服务端 Notice.cfg 格式错误")
            _segments = parts

            Try
                Dim cfg = ModNet.GetServerStrict("config.json", 8)
                Using doc = JsonDocument.Parse(cfg)
                    Dim root = doc.RootElement

                    '云控 settings：强制写入本地设置
                    Dim settingsEl As JsonElement
                    If root.TryGetProperty("settings", settingsEl) AndAlso settingsEl.ValueKind = JsonValueKind.Object Then
                        Dim cloud As New Dictionary(Of String, String)
                        For Each p In settingsEl.EnumerateObject()
                            Select Case p.Value.ValueKind
                                Case JsonValueKind.True : cloud(p.Name) = "True"
                                Case JsonValueKind.False : cloud(p.Name) = "False"
                                Case JsonValueKind.Number : cloud(p.Name) = p.Value.GetRawText()
                                Case JsonValueKind.String : cloud(p.Name) = p.Value.GetString()
                            End Select
                        Next
                        _cloudSettings = cloud
                    End If

                    '联机 Link 段
                    Dim linkEl As JsonElement
                    If root.TryGetProperty("Link", linkEl) AndAlso linkEl.ValueKind = JsonValueKind.Object Then
                        Dim lc As New LinkConfig
                        Dim e As JsonElement
                        If linkEl.TryGetProperty("EasyTierVersion", e) Then lc.EasyTierVersion = e.GetInt32()
                        If linkEl.TryGetProperty("Argument", e) Then lc.Argument = e.GetString()
                        If linkEl.TryGetProperty("DiscoverPeerId", e) Then lc.DiscoverPeerId = e.GetInt32()
                        If linkEl.TryGetProperty("DiscoverPeer", e) Then lc.DiscoverPeer = e.GetString()
                        If linkEl.TryGetProperty("RandomPeer", e) Then lc.RandomPeer = e.GetInt32()
                        lc.Downloads = ReadStringList(linkEl, ModPlatform.LinkDownloadsKey)
                        If lc.Downloads.Count = 0 Then lc.Downloads = ReadStringList(linkEl, "Downloads")
                        lc.Peers = ReadStringList(linkEl, "Peers")
                        lc.MandatoryPeers = ReadStringList(linkEl, "MandatoryPeers")
                        Link = lc
                    End If
                End Using
            Catch
                '服务端未提供 config.json 时忽略
            End Try
            Return True
        Catch ex As Exception
            ModLog.Info("连接服务端失败（离线模式）：" & ex.Message)
            Return False
        End Try
    End Function

    Private Function ReadStringList(obj As JsonElement, name As String) As List(Of String)
        Dim list As New List(Of String)
        Try
            Dim e As JsonElement
            If obj.TryGetProperty(name, e) AndAlso e.ValueKind = JsonValueKind.Array Then
                For Each item In e.EnumerateArray()
                    If item.ValueKind = JsonValueKind.String Then list.Add(item.GetString())
                Next
            End If
        Catch
        End Try
        Return list
    End Function

    ''' <summary>应用云控 settings 到本地设置（每次启动生效）。</summary>
    Public Sub ApplyCloudConfig()
        If _cloudSettings Is Nothing Then Return
        Try
            For Each kv In _cloudSettings
                '跳过 Linux 版没有的 Windows 专属键
                If kv.Key.StartsWith("UiHidden") Then Continue For
                ModSettings.SetValue(kv.Key, kv.Value)
            Next
            ModSettings.Save()
            ModLog.Info("已从服务端应用云控配置 " & _cloudSettings.Count & " 项")
        Catch ex As Exception
            ModLog.Err(ex, "应用云控配置")
        End Try
    End Sub

    ''' <summary>本通道的更新详情：Notice.cfg 第 8/9/10 段 = 正式/快照/内测；留空时回退到正式段。</summary>
    Public Function GetUpdateDetail(Optional channel As String = "") As String
        If _segments Is Nothing Then Return ""
        If channel = "" Then channel = ModApp.ChannelKey
        Dim index = If(channel = "Beta", 9, If(channel = "Snapshot", 8, 7))
        Dim text = ""
        If _segments.Length > index Then text = If(_segments(index), "")
        If text.Trim() = "" AndAlso _segments.Length > 7 Then text = If(_segments(7), "")
        Return text.Replace("\n", Environment.NewLine)
    End Function

    ''' <summary>本通道公告（第 4/5/7 段，按通道取）；本通道没写就回退到正式版公告。</summary>
    Public Function GetNotice(Optional channel As String = "") As NoticeData
        If _segments Is Nothing Then Return Nothing
        If channel = "" Then channel = ModApp.ChannelKey
        Dim noticeIndex = If(channel = "Beta", 6, If(channel = "Snapshot", 4, 3))
        If _segments.Length <= noticeIndex OrElse _segments(noticeIndex) = "" Then noticeIndex = 3
        Dim n As New NoticeData With {
            .Title = If(_segments(0), ""),
            .Code = If(_segments.Length > 2, _segments(2), ""),
            .Content = "",
            .Detail = GetUpdateDetail(channel)
        }
        If _segments.Length > noticeIndex Then n.Content = If(_segments(noticeIndex), "").Replace("\n", Environment.NewLine)
        Return n
    End Function

    ''' <summary>
    ''' 读某个通道的安装包信息：先看 config.json 的 &lt;平台&gt;.Channels.&lt;通道&gt;
    ''' 没有则回退到旧的 Linux.LauncherVersionCode / LauncherUrl（正式通道）。
    ''' </summary>
    Public Function GetChannelInfo(Optional channel As String = "") As (Code As Integer, File As String)
        If channel = "" Then channel = ModApp.ChannelKey
        Try
            Dim cfg = ModNet.GetServer("config.json", 8)
            If String.IsNullOrWhiteSpace(cfg) Then Return (0, "")
            Using doc = JsonDocument.Parse(cfg)
                Dim root = doc.RootElement
                Dim e As JsonElement
                If Not root.TryGetProperty(ModPlatform.ServerSection, e) Then Return (0, "")
                Dim code = 0
                Dim file = ""
                '新格式：<平台>.Channels.<通道>
                Dim channels As JsonElement
                If e.TryGetProperty("Channels", channels) AndAlso channels.ValueKind = JsonValueKind.Object Then
                    Dim ch As JsonElement
                    If channels.TryGetProperty(channel, ch) AndAlso ch.ValueKind = JsonValueKind.Object Then
                        Dim el As JsonElement
                        If ch.TryGetProperty("VersionCode", el) Then code = el.GetInt32()
                        If ch.TryGetProperty("File", el) Then file = el.GetString()
                        If file = "" AndAlso ch.TryGetProperty("Url", el) Then file = el.GetString()
                        'macOS 一个通道有两个架构包：File 存 arm64，FileX64 存 Intel
                        If ModPlatform.IsMac AndAlso ModPlatform.ArchName = "x64" Then
                            If ch.TryGetProperty("FileX64", el) AndAlso el.ValueKind = JsonValueKind.String Then
                                Dim x64File = el.GetString()
                                If Not String.IsNullOrWhiteSpace(x64File) Then file = x64File
                            End If
                        End If
                        'Linux 同样分架构：File 存 x64（amd64），FileArm64 存 arm64（aarch64）
                        If ModPlatform.IsLinux AndAlso ModPlatform.IsArm64 Then
                            Dim armFile = ""
                            If ch.TryGetProperty("FileArm64", el) AndAlso el.ValueKind = JsonValueKind.String Then
                                armFile = el.GetString()
                            End If
                            '旧配置里没有 FileArm64：按命名规律把 File 里的 x64 换成 arm64，换不出来就当没有更新
                            If String.IsNullOrWhiteSpace(armFile) AndAlso Not String.IsNullOrWhiteSpace(file) Then
                                armFile = file.Replace("linux-x64", "linux-arm64")
                                If armFile = file Then armFile = ""
                            End If
                            file = armFile
                        End If
                    End If
                End If
                '旧格式回退（只有 Linux 正式版用过这种字段）
                If code = 0 AndAlso channel = "Release" AndAlso ModPlatform.IsLinux Then
                    Dim el As JsonElement
                    If e.TryGetProperty("LauncherVersionCode", el) Then code = el.GetInt32()
                    If file = "" AndAlso e.TryGetProperty("LauncherUrl", el) Then file = el.GetString()
                End If
                Return (code, file)
            End Using
        Catch ex As Exception
            ModLog.Err(ex, "读取通道版本信息")
        End Try
        Return (0, "")
    End Function

    ''' <summary>
    ''' 检查当前通道的 Linux 启动器更新。有新版本返回（版本码, 下载地址）；没有更新返回 Nothing。
    ''' 下载地址统一拼当前 ServerAddress（config.json 里只写文件名），也允许写完整 http 地址。
    ''' </summary>
    Public Function CheckUpdate(Optional channel As String = "") As (Code As Integer, Url As String)?
        If channel = "" Then channel = ModApp.ChannelKey
        Try
            Dim info = GetChannelInfo(channel)
            If info.Code <= 0 OrElse String.IsNullOrWhiteSpace(info.File) Then Return Nothing
            If info.Code > ModApp.VersionCode Then
                Dim url = info.File
                If Not url.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                    url = ModNet.ServerAddress.TrimEnd("/"c) & "/" & url.TrimStart("/"c)
                End If
                '内测包分发受服务端保护：带上设备码 + 内测码
                If channel = "Beta" Then
                    url &= If(url.Contains("?"), "&", "?") & "ident=" & Uri.EscapeDataString(ModSecret.DeviceCode) &
                           "&key=" & Uri.EscapeDataString(ModSettings.GetString("BetaCode", ""))
                End If
                Return (info.Code, url)
            End If
        Catch ex As Exception
            ModLog.Err(ex, "检查更新")
        End Try
        Return Nothing
    End Function

    ''' <summary>同一条公告只自动弹一次：指纹存在设置文件里，公告内容变了才会再弹。</summary>
    Public Function ShouldShowNotice(title As String, content As String) As Boolean
        Dim raw = If(String.IsNullOrEmpty(title), "R", "T") & "|" & title & "|" & content
        Dim hash As String
        Using md5 = System.Security.Cryptography.MD5.Create()
            hash = BitConverter.ToString(md5.ComputeHash(Text.Encoding.UTF8.GetBytes(raw))).Replace("-", "")
        End Using
        If ModSettings.GetString("NoticeShownHash", "") = hash Then Return False
        ModSettings.SetValue("NoticeShownHash", hash)
        ModSettings.Save()
        Return True
    End Function

    ''' <summary>下载启动器更新包到临时目录，返回保存路径；失败返回空字符串。</summary>
    Public Function DownloadUpdate(url As String, progress As Action(Of Double)) As String
        Try
            Directory.CreateDirectory(ModPaths.TempDir)
            Dim name As String = If(url.Contains("?"), url.Substring(0, url.IndexOf("?"c)), url)
            name = name.Substring(name.LastIndexOf("/"c) + 1)
            If name = "" Then name = "TYWLauncher-update.tar.gz"
            Dim filePath As String = IO.Path.Combine(ModPaths.TempDir, name)
            If ModNet.DownloadFile(url, filePath, progress) Then Return filePath
            Return ""
        Catch ex As Exception
            ModLog.Err(ex, "下载更新包")
            Return ""
        End Try
    End Function

End Module
