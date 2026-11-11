Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports Newtonsoft.Json.Linq

''' <summary>
''' 拓展商店：从下载站（downloads.tywmc.cn）拉取「分支项目树」并在启动器设置里展示，
''' 支持一键把拓展包下载并导入启动器（复用 ModExt.Install）。
'''
''' 安全约定（与 OAuth / CurseForge 密钥一致）：
'''   应用凭证密钥（X-Api-Key）恒不进源码，由 scripts/inject_secrets.py 在编译期注入
'''   ModSecret.BuiltInStoreApiKey；运行时只在内存里使用，界面只显示掩码，绝不打印明文。
'''   该密钥用于让启动器成为「可信内部调用方」，从而绕过下载站的来源域名 / IP 白名单，
'''   在任意客户端上都能读取与下载；密钥无法反推、源码里永远看不到真实值。
''' </summary>
Friend Module ModStore

#Region "配置（非机密）"

    ''' <summary>下载站基础地址（公开，非机密）。</summary>
    Public Const StoreBaseUrl As String = "https://downloads.tywmc.cn/"

    ''' <summary>下载站中的应用标识（分支项目树的根）。</summary>
    Public Const StoreAppSlug As String = "tyw-launcher"

#End Region

#Region "平台检测（挑下载文件用）"

    ''' <summary>当前运行平台（与下载站 os 字段一致：windows / linux / macos / android）。</summary>
    Friend ReadOnly Property StoreOs As String
        Get
            Try
                Dim p = Environment.OSVersion.Platform
                Select Case p
                    Case PlatformID.Win32NT, PlatformID.Win32Windows, PlatformID.WinCE
                        Return "windows"
                    Case PlatformID.MacOSX
                        Return "macos"
                    Case PlatformID.Unix
                        Return "linux"
                    Case Else
                        Return "windows"
                End Select
            Catch
                Return "windows"
            End Try
        End Get
    End Property

    ''' <summary>当前 CPU 架构（与下载站 arch 字段一致：x64 / x86 / arm64）。</summary>
    Friend ReadOnly Property StoreArch As String
        Get
            Try
                If Not Environment.Is64BitOperatingSystem Then Return "x86"
                If (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") & "").Equals("ARM64", StringComparison.OrdinalIgnoreCase) Then Return "arm64"
                Return "x64"
            Catch
                Return "x64"
            End Try
        End Get
    End Property

#End Region

#Region "密钥与请求头"

    ''' <summary>密钥掩码：用于界面展示，避免泄露明文。</summary>
    Friend Function StoreKeyMasked() As String
        Dim key = ModSecret.StoreApiKey
        If String.IsNullOrWhiteSpace(key) Then Return "（未配置）"
        If key.Length <= 6 Then Return "****"
        Return key.Substring(0, 4) & "****" & key.Substring(key.Length - 2)
    End Function

    ''' <summary>带应用凭证的请求头；未配置密钥时返回 Nothing（此时若下载站开了来源白名单会读不到）。</summary>
    Private Function ApiHeaders() As String(,)
        Dim key = ModSecret.StoreApiKey
        If String.IsNullOrWhiteSpace(key) Then Return Nothing
        Dim h(0, 1) As String
        h(0, 0) = "X-Api-Key"
        h(0, 1) = key
        Return h
    End Function

#End Region

#Region "数据模型"

    ''' <summary>分支项目（下载站 depth=2 节点）。</summary>
    Public Class StoreProject
        Public Id As Integer = 0
        Public Name As String = ""
        Public Slug As String = ""
        Public Tagline As String = ""
        Public Description As String = ""
        Public Icon As String = ""
        Public Downloads As Integer = 0
        Public Files As Integer = 0
        Public SizeHuman As String = ""
        Public Latest As String = ""

        Public Shared Function FromJson(j As JObject) As StoreProject
            Dim p As New StoreProject
            If j Is Nothing Then Return p
            p.Id = JsonInt(j, "id")
            p.Name = JsonStr(j, "name")
            p.Slug = JsonStr(j, "slug")
            p.Tagline = JsonStr(j, "tagline")
            p.Description = JsonStr(j, "description")
            p.Icon = JsonStr(j, "icon")
            Dim s = TryCast(j("stats"), JObject)
            If s IsNot Nothing Then
                p.Downloads = JsonInt(s, "downloads")
                p.Files = JsonInt(s, "files")
                p.SizeHuman = JsonStr(s, "size_human")
                p.Latest = JsonStr(s, "latest")
            End If
            Return p
        End Function
    End Class

    ''' <summary>分类（下载站 depth=1 节点），其下挂若干项目。</summary>
    Public Class StoreCategory
        Public Id As Integer = 0
        Public Name As String = ""
        Public Tagline As String = ""
        Public Description As String = ""
        Public Icon As String = ""
        Public Projects As New List(Of StoreProject)
    End Class

    Private Function JsonStr(j As JObject, key As String) As String
        Try
            Dim t = j(key)
            If t Is Nothing OrElse t.Type = JTokenType.Null Then Return ""
            Return t.ToString
        Catch
            Return ""
        End Try
    End Function

    Private Function JsonInt(j As JObject, key As String) As Integer
        Try
            Dim t = j(key)
            If t Is Nothing OrElse t.Type = JTokenType.Null Then Return 0
            Dim v As Integer
            If Integer.TryParse(t.ToString, v) Then Return v
        Catch
        End Try
        Return 0
    End Function

#End Region

#Region "拉取数据"

    ''' <summary>
    ''' 拉取分类树：返回所有可见的 depth=1 分类，以及各自 depth=2 的项目。
    ''' 分类与项目都跟随下载站维护，可无限新增；客户端只负责渲染。
    ''' </summary>
    Friend Function FetchTree() As List(Of StoreCategory)
        Dim cats As New List(Of StoreCategory)
        Try
            Dim url = StoreBaseUrl.TrimEnd("/"c) & "/api/nodes.php?app=" & Uri.EscapeDataString(StoreAppSlug) & "&depth=2&with=stats"
            Dim raw = NetRequestByClient(url, Headers:=ApiHeaders(), RequireJson:=True, Timeout:=15000)
            Dim json = JObject.Parse(raw)
            Dim nodes = TryCast(json("nodes"), JArray)
            If nodes Is Nothing Then Return cats

            Dim byDepth1 As New List(Of JObject)
            For Each n In nodes
                Dim jo = TryCast(n, JObject)
                If jo Is Nothing Then Continue For
                If JsonInt(jo, "depth") = 1 Then byDepth1.Add(jo)
            Next

            For Each c In byDepth1
                Dim cat As New StoreCategory With {
                    .Id = JsonInt(c, "id"),
                    .Name = JsonStr(c, "name"),
                    .Tagline = JsonStr(c, "tagline"),
                    .Description = JsonStr(c, "description"),
                    .Icon = JsonStr(c, "icon")}
                Dim pid = cat.Id
                For Each n In nodes
                    Dim jo = TryCast(n, JObject)
                    If jo Is Nothing Then Continue For
                    If JsonInt(jo, "depth") = 2 AndAlso JsonInt(jo, "parent_id") = pid Then
                        cat.Projects.Add(StoreProject.FromJson(jo))
                    End If
                Next
                cats.Add(cat)
            Next
        Catch ex As Exception
            Logger.Warn(ex, "拉取拓展商店分类树失败")
            Throw
        End Try
        Return cats
    End Function

    ''' <summary>拉取某项目的下载文件清单（带 get_url，可直接拼成下载链接）。</summary>
    Friend Function FetchFiles(nodeId As Integer) As JArray
        Dim url = StoreBaseUrl.TrimEnd("/"c) & "/api/files.php?node=" & nodeId &
                  "&recursive=1&latest=1&get_url=1&limit=200"
        Dim raw = NetRequestByClient(url, Headers:=ApiHeaders(), RequireJson:=True, Timeout:=15000)
        Dim json = JObject.Parse(raw)
        Dim files = TryCast(json("files"), JArray)
        If files Is Nothing Then Return New JArray()
        Return files
    End Function

    ''' <summary>按当前平台挑一个最匹配的下载文件；只认拓展包（.tmlext / .jar）。返回 Nothing 表示没有适配版本。</summary>
    Friend Function PickFile(files As JArray) As JObject
        If files Is Nothing OrElse files.Count = 0 Then Return Nothing
        Dim best As JObject = Nothing
        Dim bestScore As Integer = -1
        For Each f In files
            Dim jo = TryCast(f, JObject)
            If jo Is Nothing Then Continue For
            Dim fname = JsonStr(jo, "file").ToLowerInvariant()
            If Not fname.EndsWith(".tmlext") AndAlso Not fname.EndsWith(".jar") Then Continue For
            Dim os = JsonStr(jo, "os")
            Dim arch = JsonStr(jo, "arch")
            Dim score As Integer = 0
            If os = StoreOs Then score += 2
            If arch = StoreArch Then score += 1
            If score > bestScore Then
                bestScore = score
                best = jo
            End If
        Next
        Return best
    End Function

#End Region

#Region "下载并导入"

    ''' <summary>
    ''' 带 X-Api-Key 头下载文件到本地（NetDownloadByLoader 不支持自定义头，故自写 HttpClient）。
    ''' 跟随 302 到 download.php（一次性令牌，无需密钥）。
    ''' </summary>
    Private Function DownloadFileWithKey(url As String, localPath As String) As Long
        Dim dir = Path.GetDirectoryName(localPath)
        If Not String.IsNullOrEmpty(dir) AndAlso Not Directory.Exists(dir) Then Directory.CreateDirectory(dir)
        Dim handler As New HttpClientHandler With {
            .AllowAutoRedirect = True,
            .AutomaticDecompression = DecompressionMethods.Deflate Or DecompressionMethods.GZip}
        Using client As New HttpClient(handler)
            client.Timeout = TimeSpan.FromSeconds(120)
            Using req As New HttpRequestMessage(HttpMethod.Get, url)
                If Not String.IsNullOrWhiteSpace(ModSecret.StoreApiKey) Then req.Headers.Add("X-Api-Key", ModSecret.StoreApiKey)
                req.Headers.Add("User-Agent", "TYWLauncher/" & VersionBaseName)
                Using resp = client.SendAsync(req).GetAwaiter().GetResult()
                    resp.EnsureSuccessStatusCode()
                    Using stream = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                        Using fs = File.Create(localPath)
                            stream.CopyTo(fs)
                        End Using
                    End Using
                End Using
            End Using
        End Using
        Return New FileInfo(localPath).Length
    End Function

    ''' <summary>
    ''' 一键导入：挑当前平台的最新构建 → 下载到临时目录 → 交给 ModExt.Install 安装。
    ''' onStatus 用于回报进度（在后台线程调用，调用方负责切回 UI 线程）。
    ''' </summary>
    Friend Sub DownloadAndInstall(project As StoreProject, Optional onStatus As Action(Of String) = Nothing)
        If project Is Nothing Then Throw New ArgumentNullException("project")
        If onStatus IsNot Nothing Then onStatus("正在获取「" & If(project.Name, "拓展") & "」的下载清单……")
        Dim files = FetchFiles(project.Id)
        Dim pick = PickFile(files)
        If pick Is Nothing Then
            Throw New Exception("没有适配当前平台（" & StoreOs & " " & StoreArch & "）的下载，请在下载站查看其他平台版本。")
        End If
        Dim getUrl = JsonStr(pick, "get_url")
        If getUrl = "" Then Throw New Exception("下载链接缺失。")
        Dim fullUrl = StoreBaseUrl.TrimEnd("/"c) & getUrl
        Dim fileName = JsonStr(pick, "file")
        If fileName = "" Then fileName = "store-" & project.Id & ".tmlext"
        If Not fileName.EndsWith(".tmlext", StringComparison.OrdinalIgnoreCase) AndAlso
           Not fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) Then
            fileName &= ".tmlext"
        End If
        Dim tmp = PathTemp & "TYW Launcher\Store\" & Guid.NewGuid().ToString("N") & "_" & Path.GetFileName(fileName)
        Try
            If onStatus IsNot Nothing Then onStatus("正在下载「" & If(project.Name, "拓展") & "」……")
            DownloadFileWithKey(fullUrl, tmp)
            If onStatus IsNot Nothing Then onStatus("正在导入拓展……")
            ModExt.Init()
            ModExt.Install(tmp)
        Finally
            Try : FileUtils.Delete(tmp) : Catch : End Try
        End Try
    End Sub

#End Region

End Module
