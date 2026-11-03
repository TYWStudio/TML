Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Sockets
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>版本清单里的一项。</summary>
Public Class McVersionSummary
    Public Property Id As String = ""
    Public Property Type As String = ""
    Public Property ReleaseTime As String = ""
    Public Property Url As String = ""
End Class

''' <summary>
''' 原版游戏下载。对应 Windows 版 ModDownload 的原版安装部分。
''' 全部走 BMCLAPI 镜像（国内直连 Mojang 会超时），镜像不可用时自动回退官方源。
''' </summary>
Public Module ModDownload

    Private ReadOnly Handler As HttpMessageHandler = CreateHandler()

    Public ReadOnly Client As HttpClient = CreateClient()

    Private Function CreateHandler() As HttpMessageHandler
        Dim s As New SocketsHttpHandler()
        s.AutomaticDecompression = DecompressionMethods.All
        s.MaxConnectionsPerServer = 64
        s.ConnectTimeout = TimeSpan.FromSeconds(15)
        s.PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        s.ConnectCallback = AddressOf ConnectPreferred
        Return s
    End Function

    ''' <summary>
    ''' 优先用 IPv4 建连。Mojang 的 CDN 域名带 AAAA 记录，但很多网络（包括这台测试机）
    ''' 的 IPv6 只是有路由、实际不通，.NET 会一直挂在 IPv6 上直到超时，表现为「下载卡死」。
    ''' </summary>
    Private Function ConnectPreferred(ctx As SocketsHttpConnectionContext, ct As CancellationToken) As ValueTask(Of Stream)
        Dim host = ctx.DnsEndPoint.Host
        Dim port = ctx.DnsEndPoint.Port
        Dim addrs = Dns.GetHostAddresses(host)
        Dim ordered = addrs.OrderBy(Function(a) If(a.AddressFamily = AddressFamily.InterNetwork, 0, 1)).ToArray()
        Dim lastEx As Exception = Nothing
        For Each a In ordered
            Dim sock As New Socket(a.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            Try
                sock.Connect(New IPEndPoint(a, port))
                sock.NoDelay = True
                Return New ValueTask(Of Stream)(New NetworkStream(sock, True))
            Catch ex As Exception
                lastEx = ex
                sock.Dispose()
            End Try
        Next
        Throw New IOException("无法连接 " & host & "：" & lastEx?.Message, lastEx)
    End Function

    Private Function CreateClient() As HttpClient
        Dim c As New HttpClient(Handler)
        c.Timeout = Timeout.InfiniteTimeSpan   ' 由每次请求自己控制超时
        c.DefaultRequestVersion = New Version(1, 1)
        Return c
    End Function

    ''' <summary>镜像地址，可在设置里改。</summary>
    Public Property Mirror As String = "https://bmclapi2.bangbang93.com"

    ''' <summary>官方源。镜像整体不可用时自动切到这里。</summary>
    Public Const OfficialBase As String = "https://launchermeta.mojang.com"

    Private _active As String = Nothing

    ''' <summary>
    ''' 当前实际使用的源。首次为镜像，一旦镜像整体失败就切到官方并保持，
    ''' 避免每个文件都要先等一次超时。
    ''' </summary>
    Public ReadOnly Property Active As String
        Get
            If String.IsNullOrEmpty(_active) Then _active = LoadRememberedSource()
            Return _active
        End Get
    End Property

    Private Function LoadRememberedSource() As String
        If ModSettings.GetBool("UseOfficialSource") Then Return OfficialBase
        If ModSettings.GetString("SourceLastGood", "") = "official" Then
            Dim ts As Long = 0
            Long.TryParse(ModSettings.GetString("SourceLastGoodTime", "0"), ts)
            ' 一天内有效，之后重新试一次镜像（镜像恢复的话速度更好）
            If DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts < 86400 Then Return OfficialBase
        End If
        Return Mirror
    End Function

    Private Sub RememberSource(kind As String)
        ModSettings.SetValue("SourceLastGood", kind)
        ModSettings.SetValue("SourceLastGoodTime", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        ModSettings.Save()
    End Sub

    Public Sub ResetSource()
        _active = Nothing
        ModSettings.SetValue("SourceLastGood", "")
        ModSettings.Save()
    End Sub

    Private ReadOnly Property UseMirror As Boolean
        Get
            Return Not Active.StartsWith(OfficialBase, StringComparison.OrdinalIgnoreCase)
        End Get
    End Property

    Private Sub FailoverToOfficial()
        If UseMirror Then
            ModLog.Info("镜像不可用，切换到官方源：" & OfficialBase)
            _active = OfficialBase
            RememberSource("official")
        End If
    End Sub

    Private ReadOnly ManifestCache As String = Path.Combine(ModPaths.CacheDir, "version_manifest.json")
    Private _manifest As List(Of McVersionSummary) = Nothing

    ''' <summary>发起请求，统一的 UA 与失败重试。默认 25 秒超时，便于快速判定源不可用。</summary>
    Public Function GetBytes(url As String, Optional retry As Integer = 2, Optional timeoutSec As Integer = 25) As Byte()
        Dim lastEx As Exception = Nothing
        For i = 0 To retry
            Try
                Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec))
                    Using req As New HttpRequestMessage(HttpMethod.Get, url)
                        req.Headers.UserAgent.ParseAdd("TYWLauncher/1.0 (Linux)")
                        Using resp = Client.Send(req, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                            resp.EnsureSuccessStatusCode()
                            Return resp.Content.ReadAsByteArrayAsync(cts.Token).GetAwaiter().GetResult()
                        End Using
                    End Using
                End Using
            Catch ex As Exception
                lastEx = ex
                If i < retry Then Thread.Sleep(300 * (i + 1))
            End Try
        Next
        Throw New Exception("下载失败：" & url & " —— " & lastEx?.Message, lastEx)
    End Function

    ''' <summary>下载到文件（先写 .tmp 再改名，避免中断留下坏文件）。</summary>
    Public Sub DownloadFile(url As String, dest As String, Optional retry As Integer = 2)
        Dim dir = Path.GetDirectoryName(dest)
        If Not String.IsNullOrEmpty(dir) AndAlso Not Directory.Exists(dir) Then Directory.CreateDirectory(dir)
        Dim tmp = dest & ".tmp"
        Dim lastEx As Exception = Nothing
        For i = 0 To retry
            Try
                Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(40))
                    Using req As New HttpRequestMessage(HttpMethod.Get, url)
                        req.Headers.UserAgent.ParseAdd("TYWLauncher/1.0 (Linux)")
                        Using resp = Client.Send(req, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                            resp.EnsureSuccessStatusCode()
                            Using src = resp.Content.ReadAsStreamAsync(cts.Token).GetAwaiter().GetResult()
                                Using dst = New FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920)
                                    Dim buf(81919) As Byte
                                    Do
                                        Dim n = src.ReadAsync(buf, 0, buf.Length, cts.Token).GetAwaiter().GetResult()
                                        If n <= 0 Then Exit Do
                                        dst.Write(buf, 0, n)
                                    Loop
                                End Using
                            End Using
                        End Using
                    End Using
                End Using
                If File.Exists(dest) Then File.Delete(dest)
                File.Move(tmp, dest)
                Return
            Catch ex As Exception
                lastEx = ex
                Try
                    If File.Exists(tmp) Then File.Delete(tmp)
                Catch
                End Try
                Thread.Sleep(400 * (i + 1))
            End Try
        Next
        Throw New Exception("下载失败：" & url & " —— " & lastEx?.Message, lastEx)
    End Sub

    ''' <summary>镜像优先、官方兜底；镜像失败会永久切到官方。</summary>
    Public Sub DownloadWithMirror(mirrorUrl As String, officialUrl As String, dest As String, Optional retry As Integer = 1)
        Try
            DownloadFile(mirrorUrl, dest, retry)
        Catch ex As Exception
            If String.IsNullOrEmpty(officialUrl) OrElse officialUrl = mirrorUrl Then Throw
            FailoverToOfficial()
            Dim fallback = If(UseMirror, officialUrl, officialUrl)
            DownloadFile(fallback, dest, retry)
        End Try
    End Sub

#Region "地址重写"

    ''' <summary>把 Mojang 官方地址换到镜像；已切到官方源时原样返回。</summary>
    Public Function Rewrite(url As String) As String
        If String.IsNullOrEmpty(url) Then Return url
        If Not UseMirror Then Return url
        Dim m = Active.TrimEnd("/"c)
        If url.Contains("launchermeta.mojang.com") Then Return url.Replace("https://launchermeta.mojang.com", m)
        If url.Contains("piston-meta.mojang.com") Then Return url.Replace("https://piston-meta.mojang.com", m)
        If url.Contains("launcher.mojang.com") Then Return url.Replace("https://launcher.mojang.com", m)
        If url.Contains("libraries.minecraft.net") Then Return url.Replace("https://libraries.minecraft.net", m & "/maven")
        If url.Contains("resources.download.minecraft.net") Then Return url.Replace("https://resources.download.minecraft.net", m & "/assets")
        If url.Contains("piston-data.mojang.com") Then Return url.Replace("https://piston-data.mojang.com", m)
        Return url
    End Function

#End Region

#Region "版本清单"

    Public Function Manifest(Optional force As Boolean = False) As List(Of McVersionSummary)
        If _manifest IsNot Nothing AndAlso Not force Then Return _manifest
        Dim data As Byte() = Nothing
        ' 缓存 1 小时内直接用
        Try
            If Not force AndAlso File.Exists(ManifestCache) AndAlso
               DateTime.Now - File.GetLastWriteTime(ManifestCache) < TimeSpan.FromHours(1) Then
                data = File.ReadAllBytes(ManifestCache)
            End If
        Catch
        End Try
        If data Is Nothing Then
            Dim url = Active.TrimEnd("/"c) & "/mc/game/version_manifest_v2.json"
            Try
                ' 探测时限给短一点，镜像不通时不要卡着用户
                data = GetBytes(url, 0, 10)
                If UseMirror Then RememberSource("mirror")
            Catch ex As Exception
                ' 镜像整体不可用时切官方重来
                FailoverToOfficial()
                url = Active.TrimEnd("/"c) & "/mc/game/version_manifest_v2.json"
                data = GetBytes(url, 2)
            End Try
            Try
                Directory.CreateDirectory(ModPaths.CacheDir)
                File.WriteAllBytes(ManifestCache, data)
            Catch
            End Try
        End If

        Dim list As New List(Of McVersionSummary)
        Using doc = JsonDocument.Parse(data)
            Dim versions As JsonElement
            If doc.RootElement.TryGetProperty("versions", versions) Then
                For Each v In versions.EnumerateArray()
                    list.Add(New McVersionSummary With {
                        .Id = Str(v, "id"),
                        .Type = Str(v, "type"),
                        .ReleaseTime = Str(v, "releaseTime"),
                        .Url = Str(v, "url")
                    })
                Next
            End If
        End Using
        _manifest = list
        Return list
    End Function

    Private Function Str(el As JsonElement, name As String) As String
        Dim v As JsonElement
        If el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        Return ""
    End Function

#End Region

#Region "库文件路径"

    ''' <summary>把 Maven 坐标转成相对路径：group:artifact:version[:classifier][@ext]</summary>
    Public Function MavenToPath(name As String) As String
        If String.IsNullOrEmpty(name) Then Return ""
        Dim ext = "jar"
        Dim at = name.IndexOf("@"c)
        If at >= 0 Then
            ext = name.Substring(at + 1)
            name = name.Substring(0, at)
        End If
        Dim parts = name.Split(":"c)
        If parts.Length < 3 Then Return ""
        Dim group = parts(0).Replace("."c, "/"c)
        Dim artifact = parts(1)
        Dim version = parts(2)
        Dim classifier = If(parts.Length > 3, parts(3), "")
        Dim file = artifact & "-" & version & If(classifier <> "", "-" & classifier, "") & "." & ext
        Return $"{group}/{artifact}/{version}/{file}"
    End Function

#End Region

#Region "安装"

    ''' <summary>
    ''' 安装一个原版版本。report(progress 0~1, 当前动作)，cancel 返回 True 时中止。
    ''' </summary>
    Public Sub Install(id As String, versionUrl As String, report As Action(Of Double, String), cancel As Func(Of Boolean))
        InstallAsync(id, versionUrl, report, cancel).GetAwaiter().GetResult()
    End Sub

    Private Async Function InstallAsync(id As String, versionUrl As String, report As Action(Of Double, String), cancel As Func(Of Boolean)) As Task
        ModPaths.EnsureAll()
        ' 同步阻塞式下载会占满线程池，默认线程增长太慢会让并发形同虚设
        Dim minWorker As Integer, minIo As Integer
        ThreadPool.GetMinThreads(minWorker, minIo)
        If minWorker < 128 Then ThreadPool.SetMinThreads(128, Math.Max(minIo, 128))

        Dim mcDir = ModPaths.McFolder
        Dim verDir = Path.Combine(mcDir, "versions", id)
        Directory.CreateDirectory(verDir)
        Dim jsonPath = Path.Combine(verDir, id & ".json")

        report(0.01, "获取版本信息")
        ' 已有 JSON 就不再重复下载（老版本重装文件缺失时只补文件）
        If Not File.Exists(jsonPath) AndAlso Not String.IsNullOrEmpty(versionUrl) Then
            Dim bytes = GetBytes(Rewrite(versionUrl), 1)
            File.WriteAllBytes(jsonPath, bytes)
        End If
        If Not File.Exists(jsonPath) Then Throw New Exception("拿不到版本 " & id & " 的 JSON")

        Using doc = JsonDocument.Parse(File.ReadAllBytes(jsonPath))
            Dim root = doc.RootElement

            ' --- client jar ---
            report(0.04, "下载游戏本体")
            Dim clientUrl = ""
            Dim dl As JsonElement
            If root.TryGetProperty("downloads", dl) Then
                Dim c As JsonElement
                If dl.TryGetProperty("client", c) Then clientUrl = Str(c, "url")
            End If
            Dim jarPath = Path.Combine(verDir, id & ".jar")
            If Not String.IsNullOrEmpty(clientUrl) AndAlso (Not File.Exists(jarPath) OrElse New FileInfo(jarPath).Length = 0) Then
                DownloadWithMirror(Rewrite(clientUrl), clientUrl, jarPath)
            End If
            If cancel() Then Throw New OperationCanceledException

            ' --- libraries ---
            Dim libTasks As New List(Of Tuple(Of String, String))  ' (url, 目标路径)
            Dim libsEl As JsonElement
            If root.TryGetProperty("libraries", libsEl) Then
                For Each libEl In libsEl.EnumerateArray()
                    If Not LibAllowed(libEl) Then Continue For
                    Dim libName = Str(libEl, "name")
                    Dim dls As JsonElement
                    If libEl.TryGetProperty("downloads", dls) Then
                        Dim art As JsonElement
                        If dls.TryGetProperty("artifact", art) Then
                            Dim u = Str(art, "url")
                            Dim p = Str(art, "path")
                            If String.IsNullOrEmpty(p) Then p = MavenToPath(libName)
                            If u <> "" AndAlso p <> "" Then
                                libTasks.Add(Tuple.Create(Rewrite(u), Path.Combine(mcDir, "libraries", p.Replace("/"c, Path.DirectorySeparatorChar))))
                            End If
                        End If
                        Dim cls As JsonElement
                        If dls.TryGetProperty("classifiers", cls) Then
                            ' 只下本平台+本架构最合适的那个 native（macOS 的 JSON 里通常同时有
                            ' natives-macos 与 natives-macos-arm64，全下会白拉几十 MB）
                            Dim keys As New List(Of String)
                            For Each cProp In cls.EnumerateObject()
                                keys.Add(cProp.Name)
                            Next
                            Dim bestKey = ModPlatform.BestClassifier(keys)
                            If bestKey <> "" Then
                                Dim chosen As JsonElement
                                If cls.TryGetProperty(bestKey, chosen) Then
                                    Dim u = Str(chosen, "url")
                                    Dim p = Str(chosen, "path")
                                    If String.IsNullOrEmpty(p) Then p = MavenToPath(libName & ":" & bestKey)
                                    If u <> "" AndAlso p <> "" Then
                                        libTasks.Add(Tuple.Create(Rewrite(u), Path.Combine(mcDir, "libraries", p.Replace("/"c, Path.DirectorySeparatorChar))))
                                    End If
                                End If
                            End If
                        End If
                    Else
                        ' 老版本没有 downloads 字段
                        Dim p = MavenToPath(libName)
                        If p <> "" Then
                            libTasks.Add(Tuple.Create(Rewrite("https://libraries.minecraft.net/" & p),
                                                     Path.Combine(mcDir, "libraries", p.Replace("/"c, Path.DirectorySeparatorChar))))
                        End If
                    End If
                Next
            End If

            ' --- assets 索引 ---
            report(0.10, "获取资源索引")
            Dim assetIndexId = "legacy"
            Dim indexTasks As New List(Of Tuple(Of String, String))
            Dim ai As JsonElement
            If root.TryGetProperty("assetIndex", ai) Then
                assetIndexId = Str(ai, "id")
                Dim au = Str(ai, "url")
                Dim idxPath = Path.Combine(mcDir, "assets", "indexes", assetIndexId & ".json")
                If Not File.Exists(idxPath) Then
                    DownloadWithMirror(Rewrite(au), au, idxPath)
                End If
                Try
                    Using idxDoc = JsonDocument.Parse(File.ReadAllBytes(idxPath))
                        Dim objs As JsonElement
                        If idxDoc.RootElement.TryGetProperty("objects", objs) Then
                            For Each o In objs.EnumerateObject()
                                Dim hash = Str(o.Value, "hash")
                                If hash.Length < 2 Then Continue For
                                Dim sub1 = hash.Substring(0, 2)
                                Dim target = Path.Combine(mcDir, "assets", "objects", sub1, hash)
                                If File.Exists(target) AndAlso New FileInfo(target).Length > 0 Then Continue For
                                Dim u = "https://resources.download.minecraft.net/" & sub1 & "/" & hash
                                indexTasks.Add(Tuple.Create(Rewrite(u), target))
                            Next
                        End If
                    End Using
                Catch ex As Exception
                    ModLog.Err(ex, "解析资源索引")
                End Try
            End If

            ' --- 并行下载（异步，IO 不占线程，并发才真正生效） ---
            Dim all As New List(Of Tuple(Of String, String))
            all.AddRange(libTasks)
            all.AddRange(indexTasks)
            Dim todo = all.Where(Function(t) Not File.Exists(t.Item2) OrElse New FileInfo(t.Item2).Length = 0).ToList()
            Dim total = Math.Max(todo.Count, 1)
            Dim done = 0
            Dim stageName = If(indexTasks.Count > libTasks.Count, "下载资源文件", "下载依赖库")
            report(0.14, stageName & "（共 " & todo.Count & " 个文件）")

            Dim failCount = 0
            Using sem As New SemaphoreSlim(16)
                Dim jobs As New List(Of Task)
                For Each t In todo
                    Dim item = t
                    Await sem.WaitAsync()
                    jobs.Add(Task.Run(Async Function()
                                          Try
                                              If Not cancel() Then
                                                  Await DownloadFileAsync(item.Item1, item.Item2, 1)
                                              End If
                                          Catch ex As Exception
                                              Interlocked.Increment(failCount)
                                              ModLog.Err(ex, "下载依赖失败：" & item.Item1)
                                          Finally
                                              sem.Release()
                                              Dim n = Interlocked.Increment(done)
                                              If n Mod 8 = 0 OrElse n = total Then
                                                  report(0.14 + 0.84 * n / total, stageName & $"（{n}/{total}）")
                                              End If
                                          End Try
                                      End Function))
                Next
                Await Task.WhenAll(jobs)
            End Using

            ' 少量文件失败可能不影响启动（有些库只在其它平台需要），大面积失败则必须报错
            If failCount > 0 Then
                If failCount > Math.Max(3, total * 0.2) Then
                    Throw New Exception($"有 {failCount} 个文件下载失败（共 {total} 个），请检查网络后重新安装。")
                End If
                ModLog.Info($"有 {failCount} 个文件下载失败，已跳过")
            End If
        End Using

        ' legacy 资源需要复制到 assets/virtual/legacy
        Try
            CopyLegacyAssetsIfNeeded()
        Catch ex As Exception
            ModLog.Err(ex, "整理旧版资源")
        End Try

        report(1.0, "安装完成")
        ModVersion.Invalidate()
    End Function

    ''' <summary>异步下载单个文件（先写 .tmp 再改名，避免中断留下坏文件）。</summary>
    Public Async Function DownloadFileAsync(url As String, dest As String, Optional retry As Integer = 2) As Task
        Dim dir = Path.GetDirectoryName(dest)
        If Not String.IsNullOrEmpty(dir) AndAlso Not Directory.Exists(dir) Then Directory.CreateDirectory(dir)
        Dim tmp = dest & ".tmp"
        Dim lastEx As Exception = Nothing
        For i = 0 To retry
            Try
                Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(40))
                    Using req As New HttpRequestMessage(HttpMethod.Get, url)
                        req.Headers.UserAgent.ParseAdd("TYWLauncher/1.0 (Linux)")
                        Using resp = Await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                            resp.EnsureSuccessStatusCode()
                            Using src = Await resp.Content.ReadAsStreamAsync(cts.Token)
                                Using dst As New FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, True)
                                    Await CopyCounted(src, dst, 81920, cts.Token)
                                End Using
                            End Using
                        End Using
                    End Using
                End Using
                If File.Exists(dest) Then File.Delete(dest)
                File.Move(tmp, dest)
                Return
            Catch ex As Exception
                lastEx = ex
                Try
                    If File.Exists(tmp) Then File.Delete(tmp)
                Catch
                End Try
                If i < retry Then Thread.Sleep(400 * (i + 1))
            End Try
        Next
        Throw New Exception("下载失败：" & url & " —— " & lastEx?.Message, lastEx)
    End Function

#Region "下载速度（对应 Windows 版 NetManager.Speed）"

    Private ReadOnly SpeedLock As New Object
    Private TotalBytes As Long = 0
    Private ReadOnly SpeedSamples As New List(Of Tuple(Of Date, Long))

    ''' <summary>复制流的同时累计字节数。</summary>
    Private Async Function CopyCounted(src As Stream, dst As Stream, bufSize As Integer, token As CancellationToken) As Task
        Dim buf(bufSize - 1) As Byte
        Dim n = Await src.ReadAsync(buf, 0, bufSize, token)
        Do While n > 0
            Interlocked.Add(TotalBytes, n)
            Await dst.WriteAsync(buf, 0, n, token)
            n = Await src.ReadAsync(buf, 0, bufSize, token)
        Loop
    End Function

    ''' <summary>最近约 1 秒的下载速度（字节/秒）。在 UI 刷新时调用即可。</summary>
    Public Function Speed() As Long
        SyncLock SpeedLock
            Dim now = Date.UtcNow
            Dim total = Interlocked.Read(TotalBytes)
            SpeedSamples.Add(Tuple.Create(now, total))
            SpeedSamples.RemoveAll(Function(s) (now - s.Item1).TotalSeconds > 1.3)
            If SpeedSamples.Count < 2 Then Return 0
            Dim delta = SpeedSamples(SpeedSamples.Count - 1).Item2 - SpeedSamples(0).Item2
            Dim secs = (SpeedSamples(SpeedSamples.Count - 1).Item1 - SpeedSamples(0).Item1).TotalSeconds
            If secs <= 0.05 Then Return 0
            Return CLng(delta / secs)
        End SyncLock
    End Function

    ''' <summary>字节数转人类可读（对应 StringUtils.FormatByteSize，Windows 版显示如 5.2 M/s）。</summary>
    Public Function FormatSpeed(bytesPerSec As Long) As String
        Dim b = Math.Max(0, bytesPerSec)
        If b >= 1024 * 1024 * 1024 Then Return (b / 1024 / 1024 / 1024).ToString("0.0") & " G/s"
        If b >= 1024 * 1024 Then Return (b / 1024 / 1024).ToString("0.0") & " M/s"
        If b >= 1024 Then Return (b / 1024).ToString("0.0") & " K/s"
        Return b & " B/s"
    End Function

#End Region

    ''' <summary>老版本（1.6 及以前）需要把 objects 复制到 virtual/legacy。</summary>
    Private Sub CopyLegacyAssetsIfNeeded()
        Dim idx = Path.Combine(ModPaths.McFolder, "assets", "indexes", "legacy.json")
        If Not File.Exists(idx) Then Return
        Dim virt = Path.Combine(ModPaths.McFolder, "assets", "virtual", "legacy")
        If Directory.Exists(virt) AndAlso Directory.GetFiles(virt, "*", SearchOption.AllDirectories).Length > 10 Then Return
        Using doc = JsonDocument.Parse(File.ReadAllBytes(idx))
            Dim objs As JsonElement
            If Not doc.RootElement.TryGetProperty("objects", objs) Then Return
            For Each o In objs.EnumerateObject()
                Dim hash = Str(o.Value, "hash")
                If hash.Length < 2 Then Continue For
                Dim src = Path.Combine(ModPaths.McFolder, "assets", "objects", hash.Substring(0, 2), hash)
                Dim dst = Path.Combine(virt, o.Name.Replace("/"c, Path.DirectorySeparatorChar))
                If Not File.Exists(src) Then Continue For
                Directory.CreateDirectory(Path.GetDirectoryName(dst))
                File.Copy(src, dst, True)
            Next
        End Using
    End Sub

    ''' <summary>规则判断：当前平台（Linux / macOS）与当前架构是否适用。</summary>
    Public Function LibAllowed(libEl As JsonElement) As Boolean
        Dim rules As JsonElement
        If Not libEl.TryGetProperty("rules", rules) Then Return True
        Dim allowed = False
        For Each rule In rules.EnumerateArray()
            Dim action = Str(rule, "action")
            Dim osEl As JsonElement
            Dim matchAs = True
            If rule.TryGetProperty("os", osEl) Then
                Dim name = Str(osEl, "name")
                Dim expectName = If(ModPlatform.IsMac, "osx", "linux")
                If name <> "" AndAlso name <> expectName Then matchAs = False
                Dim arch = Str(osEl, "arch")
                Select Case arch
                    Case "x86" : If ModPlatform.ArchName <> "x86" Then matchAs = False
                    Case "x86_64" : If ModPlatform.ArchName <> "x64" Then matchAs = False
                    Case "arm64", "aarch64" : If Not ModPlatform.IsArm64 Then matchAs = False
                End Select
            End If
            If matchAs Then
                allowed = (action = "allow")
            End If
        Next
        Return allowed
    End Function

#End Region

End Module
