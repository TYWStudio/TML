Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports TYW.Core.Ext

''' <summary>
''' 拓展商店：从下载站（downloads.tywmc.cn）拉取「分支项目树」并在设置里展示，
''' 支持一键把拓展包下载并导入启动器（复用 ExtensionManager）。
'''
''' 安全约定（与 OAuth / CurseForge 密钥一致）：
'''   应用凭证密钥（X-Api-Key）恒不进源码，由 scripts/inject_server_url.py 在编译期注入
'''   ModServerUrl.BuiltInStoreApiKey；运行时只在内存里使用，界面不显示，绝不打印明文。
'''   该密钥用于让启动器成为「可信内部调用方」，从而绕过下载站的来源域名 / IP 白名单，
'''   在任意客户端上都能读取与下载；密钥无法反推、源码里永远看不到真实值。
''' </summary>
Public Module ModStore

#Region "配置（非机密）"

    ''' <summary>下载站基础地址（公开，非机密）。</summary>
    Public Const StoreBaseUrl As String = "https://downloads.tywmc.cn/"

    ''' <summary>下载站中的应用标识（分支项目树的根）。</summary>
    Public Const StoreAppSlug As String = "tyw-launcher"

    ''' <summary>只读取这个一级分类下的拓展项目（分类改名时仍按 slug / kind 识别）。</summary>
    Public Const StoreCategorySlug As String = "extensions"
    Public Const StoreCategoryName As String = "拓展商店"

    Private Function IsStoreCategory(slug As String, name As String, kind As String) As Boolean
        Return slug.Equals(StoreCategorySlug, StringComparison.OrdinalIgnoreCase) OrElse name.Equals(StoreCategoryName, StringComparison.OrdinalIgnoreCase) OrElse kind.Equals(StoreCategorySlug, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>下载站返回的 icon 是站内路径，补成完整 URL。</summary>
    Public Function IconUrl(icon As String) As String
        If String.IsNullOrWhiteSpace(icon) Then Return ""
        If icon.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse icon.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then Return icon
        Return StoreBaseUrl.TrimEnd("/"c) & "/" & icon.TrimStart("/"c)
    End Function

#End Region

#Region "平台检测（挑下载文件用）"

    ''' <summary>当前运行平台（与下载站 os 字段一致：linux / macos）。</summary>
    Public ReadOnly Property StoreOs As String
        Get
            If ModPlatform.IsMac Then Return "macos"
            Return "linux"
        End Get
    End Property

    ''' <summary>当前 CPU 架构（与下载站 arch 字段一致：x64 / arm64）。</summary>
    Public ReadOnly Property StoreArch As String
        Get
            Return If(ModPlatform.IsArm64, "arm64", "x64")
        End Get
    End Property

#End Region

#Region "密钥与请求头"

    ''' <summary>下载项目图标字节（失败返回空数组，不影响列表展示）。</summary>
    Public Function FetchIconBytes(url As String) As Byte()
        Try
            If String.IsNullOrWhiteSpace(url) Then Return Array.Empty(Of Byte)()
            Return Client().GetByteArrayAsync(url).GetAwaiter().GetResult()
        Catch
            Return Array.Empty(Of Byte)()
        End Try
    End Function

    Private Function ApiHeaders() As Dictionary(Of String, String)
        Dim key = ModServerUrl.BuiltInStoreApiKey
        Dim d As New Dictionary(Of String, String)()
        If Not String.IsNullOrWhiteSpace(key) Then d("X-Api-Key") = key
        Return d
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

        Public Shared Function FromJson(j As JsonElement) As StoreProject
            Dim p As New StoreProject
            If j.ValueKind = JsonValueKind.Undefined Then Return p
            p.Id = JsonInt(j, "id")
            p.Name = JsonStr(j, "name")
            p.Slug = JsonStr(j, "slug")
            p.Tagline = JsonStr(j, "tagline")
            p.Description = JsonStr(j, "description")
            p.Icon = IconUrl(JsonStr(j, "icon"))
            Dim stats As JsonElement
            If j.TryGetProperty("stats", stats) Then
                p.Downloads = JsonInt(stats, "downloads")
                p.Files = JsonInt(stats, "files")
                p.SizeHuman = JsonStr(stats, "size_human")
                p.Latest = JsonStr(stats, "latest")
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
        Public Projects As New List(Of StoreProject)()
    End Class

    Private Function JsonStr(j As JsonElement, key As String) As String
        Dim v As JsonElement
        Try
            If Not j.TryGetProperty(key, v) Then Return ""
            If v.ValueKind = JsonValueKind.Null Then Return ""
            Return If(v.GetString(), "")
        Catch
            Return ""
        End Try
    End Function

    Private Function JsonInt(j As JsonElement, key As String) As Integer
        Dim v As JsonElement
        Try
            If Not j.TryGetProperty(key, v) Then Return 0
            Dim n As Integer
            If v.TryGetInt32(n) Then Return n
            Return 0
        Catch
            Return 0
        End Try
    End Function

#End Region

#Region "拉取数据"

    Private _client As HttpClient = Nothing

    Private Function Client() As HttpClient
        If _client Is Nothing Then
            Dim h As New HttpClientHandler With {.AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate}
            _client = New HttpClient(h) With {.Timeout = TimeSpan.FromSeconds(15)}
            Try : _client.DefaultRequestHeaders.UserAgent.ParseAdd("TYWLauncher/" & ModApp.VersionBaseName) : Catch : End Try
        End If
        Return _client
    End Function

    ''' <summary>带应用凭证的 GET；未配置密钥时仍会发请求（下载站开了来源白名单时会 403）。</summary>
    Private Function GetStore(url As String) As String
        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            For Each kv In ApiHeaders()
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value)
            Next
            Using resp = Client().SendAsync(req).GetAwaiter().GetResult()
                If Not resp.IsSuccessStatusCode Then Throw New Exception("下载站返回错误码 " & CInt(resp.StatusCode) & "（" & url & "）")
                Return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            End Using
        End Using
    End Function

    ''' <summary>只拉取「拓展商店」分类及其项目；分页读取，分类/项目数量不限。</summary>
    Public Function FetchTree() As List(Of StoreCategory)
        Dim cats As New List(Of StoreCategory)()
        Try
            ' JsonElement 绑定在 JsonDocument 上，跨页收集时必须 Clone 才能脱离文档生命周期
            Dim all As New List(Of JsonElement)()
            Dim page As Integer = 1
            Do While page <= 20
                Dim url = StoreBaseUrl.TrimEnd("/"c) & "/api/nodes.php?app=" & Uri.EscapeDataString(StoreAppSlug) &
                          "&depth=2&with=stats&limit=100&page=" & page
                Dim raw = GetStore(url)
                Dim count As Integer = 0
                Dim pages As Integer = 1
                Using doc = JsonDocument.Parse(raw)
                    Dim root = doc.RootElement
                    Dim nodesEl As JsonElement
                    If root.TryGetProperty("nodes", nodesEl) Then
                        For Each x In nodesEl.EnumerateArray()
                            all.Add(x.Clone())
                            count += 1
                        Next
                    End If
                    pages = JsonInt(root, "pages")
                    If pages <= 0 Then pages = 1
                End Using
                If count = 0 OrElse page >= pages Then Exit Do
                page += 1
            Loop

            For Each c In all
                If JsonInt(c, "depth") <> 1 Then Continue For
                If Not IsStoreCategory(JsonStr(c, "slug"), JsonStr(c, "name"), JsonStr(c, "kind")) Then Continue For
                Dim cat As New StoreCategory With {
                    .Id = JsonInt(c, "id"),
                    .Name = JsonStr(c, "name"),
                    .Tagline = JsonStr(c, "tagline"),
                    .Description = JsonStr(c, "description"),
                    .Icon = IconUrl(JsonStr(c, "icon"))}
                Dim pid = cat.Id
                For Each x In all
                    If JsonInt(x, "depth") = 2 AndAlso JsonInt(x, "parent_id") = pid Then
                        cat.Projects.Add(StoreProject.FromJson(x))
                    End If
                Next
                cats.Add(cat)
            Next
        Catch ex As Exception
            ModLog.Err(ex, "拉取拓展商店分类树失败")
            Throw
        End Try
        Return cats
    End Function

    ''' <summary>拉取某项目的下载文件清单（带 get_url，可直接拼成下载链接）。</summary>
    Public Function FetchFiles(nodeId As Integer) As List(Of JsonElement)
        Dim url = StoreBaseUrl.TrimEnd("/"c) & "/api/files.php?node=" & nodeId &
                  "&recursive=1&latest=1&get_url=1&limit=200"
        Dim raw = GetStore(url)
        Dim list As New List(Of JsonElement)()
        Using doc = JsonDocument.Parse(raw)
            Dim root = doc.RootElement
            Dim filesEl As JsonElement
            If root.TryGetProperty("files", filesEl) Then
                For Each f In filesEl.EnumerateArray()
                    list.Add(f.Clone())
                Next
            End If
        End Using
        Return list
    End Function

    ''' <summary>
    ''' 自动分辨通用包与平台专用包，只认拓展包（.tmlext / .jar）：
    ''' os / arch 为空或 any/all/universal 视为通用包；明确标了别的平台（含文件名里的
    ''' -windows / -linux / arm64 等）直接跳过；多个候选时优先精确匹配当前平台 + 架构。
    ''' </summary>
    Public Function PickFile(files As List(Of JsonElement)) As JsonElement
        Dim best As JsonElement = Nothing
        Dim bestScore As Integer = -1
        Dim curOs = NormOs(StoreOs)
        Dim curArch = NormArch(StoreArch)
        For Each f In files
            Dim fname = If(JsonStr(f, "file"), "").ToLowerInvariant()
            If Not fname.EndsWith(".tmlext") AndAlso Not fname.EndsWith(".jar") Then Continue For
            Dim os = NormOs(JsonStr(f, "os"))
            Dim arch = NormArch(JsonStr(f, "arch"))
            Dim hintOs = NormOs(OsFromFile(fname))
            Dim hintArch = NormArch(ArchFromFile(fname))
            If Not IsAny(os) AndAlso os <> curOs Then Continue For
            If Not IsAny(arch) AndAlso curArch <> "" AndAlso arch <> curArch Then Continue For
            If IsAny(os) AndAlso hintOs <> "" AndAlso hintOs <> curOs Then Continue For
            If IsAny(arch) AndAlso hintArch <> "" AndAlso curArch <> "" AndAlso hintArch <> curArch Then Continue For
            Dim score As Integer = 0
            If os = curOs OrElse (IsAny(os) AndAlso hintOs = curOs) Then score += 2
            If arch = curArch OrElse (IsAny(arch) AndAlso hintArch = curArch) Then score += 1
            If score > bestScore Then
                bestScore = score
                best = f
            End If
        Next
        Return best
    End Function

    Private Function IsAny(v As String) As Boolean
        Select Case v
            Case "", "any", "all", "universal", "common", "通用", "none", "-"
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Function NormOs(v As String) As String
        Dim s = If(v, "").Trim().ToLowerInvariant()
        Select Case s
            Case "win", "win32", "win64", "windows" : Return "windows"
            Case "mac", "macos", "macosx", "osx", "darwin" : Return "macos"
            Case "linux" : Return "linux"
            Case "android" : Return "android"
            Case "ios", "iphone", "ipad" : Return "ios"
            Case Else : Return s
        End Select
    End Function

    Private Function NormArch(v As String) As String
        Dim s = If(v, "").Trim().ToLowerInvariant()
        Select Case s
            Case "x86_64", "amd64", "x64", "64bit", "64" : Return "x64"
            Case "aarch64", "arm64", "arm64-v8a", "armv8" : Return "arm64"
            Case "x86", "i386", "i686", "32bit", "32" : Return "x86"
            Case "armeabi-v7a", "armeabi", "armv7", "arm" : Return "arm"
            Case Else : Return s
        End Select
    End Function

    Private Function OsFromFile(name As String) As String
        If name.Contains("android") Then Return "android"
        If name.Contains("windows") OrElse name.Contains("win32") OrElse name.Contains("-win") Then Return "windows"
        If name.Contains("macos") OrElse name.Contains("darwin") OrElse name.Contains("-mac") Then Return "macos"
        If name.Contains("linux") Then Return "linux"
        Return ""
    End Function

    Private Function ArchFromFile(name As String) As String
        If name.Contains("aarch64") OrElse name.Contains("arm64") Then Return "arm64"
        If name.Contains("x86_64") OrElse name.Contains("amd64") OrElse name.Contains("-x64") Then Return "x64"
        If name.Contains("armeabi") OrElse name.Contains("armv7") Then Return "arm"
        If name.Contains("-x86") OrElse name.Contains("i686") Then Return "x86"
        Return ""
    End Function

#End Region

#Region "下载并导入"

    ''' <summary>
    ''' 一键导入：挑当前平台的最新构建 → 下载到拓展目录 → 交给 ExtensionManager 安装并启用。
    ''' onStatus 用于回报进度（在后台线程调用，调用方负责切回 UI 线程）。
    ''' </summary>
    Public Sub DownloadAndInstall(project As StoreProject, Optional onStatus As Action(Of String) = Nothing)
        If project Is Nothing Then Throw New ArgumentNullException("project")
        If onStatus IsNot Nothing Then onStatus("正在获取「" & If(project.Name, "拓展") & "」的下载清单……")
        Dim files = FetchFiles(project.Id)
        Dim pick = PickFile(files)
        If pick.ValueKind = JsonValueKind.Undefined Then
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
        If onStatus IsNot Nothing Then onStatus("正在下载「" & If(project.Name, "拓展") & "」……")
        Dim tmp = Path.Combine(ModPaths.TempDir, "TYWStore_" & Guid.NewGuid().ToString("N") & "_" & Path.GetFileName(fileName))
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(tmp))
            If Not ModNet.DownloadFile(fullUrl, tmp) Then Throw New Exception("下载失败，请检查网络后重试。")
            If onStatus IsNot Nothing Then onStatus("正在导入拓展……")
            If Not ExtensionManager.Instance.InstallPackage(tmp, True) Then
                Throw New Exception("导入失败，详情见日志。")
            End If
        Finally
            Try : File.Delete(tmp) : Catch : End Try
        End Try
    End Sub

#End Region

End Module
