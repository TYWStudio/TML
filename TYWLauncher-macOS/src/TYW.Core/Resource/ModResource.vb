Imports System.Collections.Concurrent
Imports System.IO
Imports System.IO.Compression
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>社区资源类别（对应 Windows 版 ResourceTypes）。</summary>
Public Enum ResType
    [Mod] = 1
    ModPack = 2
    DataPack = 3
    Shader = 4
    ResourcePack = 5
    Plugin = 6
End Enum

''' <summary>资源来源平台。</summary>
Public Enum ResPlatform
    CurseForge = 1
    Modrinth = 2
End Enum

''' <summary>
''' 社区资源的项目信息（对应 Windows 版 ResourceProject）。
''' 解析 CurseForge / Modrinth 搜索与工程 API 返回的 JSON。
''' </summary>
Public Class ResProject
    Public Property Platform As ResPlatform
    Public Property Types As ResType
    Public Property Slug As String = ""
    Public Property Id As String = ""
    Public Property RawName As String = ""
    Public Property Description As String = ""
    Public Property Website As String = ""
    Public Property LastUpdate As Date
    Public Property DownloadCount As Long
    Public Property ModLoaders As New List(Of String)
    Public Property Tags As New List(Of String)
    Public Property LogoUrl As String = ""
    ''' <summary>支持的 MC 版本（仅 Modrinth 搜索结果直接给出；CF 由 latestFilesIndexes 聚合）。</summary>
    Public Property GameVersions As New List(Of String)

    ''' <summary>从搜索/工程 JSON 解析。</summary>
    Public Shared Function Parse(el As JsonElement) As ResProject
        Dim p As New ResProject
        If el.TryGetProperty("summary", Nothing) Then 'CurseForge：有 summary 字段
            p.Platform = ResPlatform.CurseForge
            p.Id = Str(el, "id")
            p.Slug = Str(el, "slug")
            p.RawName = Str(el, "name")
            p.Description = Str(el, "summary")
            Dim links As JsonElement
            If el.TryGetProperty("links", links) Then p.Website = Str(links, "websiteUrl").TrimEnd("/"c)
            Dim dr As String = Str(el, "dateReleased")
            If dr <> "" Then Date.TryParse(dr, p.LastUpdate)
            p.DownloadCount = NumL(el, "downloadCount")
            Dim logo As JsonElement
            If el.TryGetProperty("logo", logo) AndAlso logo.ValueKind = JsonValueKind.Object Then
                Dim thumb = Str(logo, "thumbnailUrl")
                p.LogoUrl = If(thumb <> "", thumb, Str(logo, "url"))
            End If
            'Type 由 website 推断
            If p.Website.Contains("/mc-mods/") OrElse p.Website.Contains("/mod/") Then
                p.Types = ResType.Mod
            ElseIf p.Website.Contains("/modpacks/") Then
                p.Types = ResType.ModPack
            ElseIf p.Website.Contains("/resourcepacks/") OrElse p.Website.Contains("/texture-packs/") Then
                p.Types = ResType.ResourcePack
            ElseIf p.Website.Contains("/shaders/") Then
                p.Types = ResType.Shader
            Else
                p.Types = ResType.DataPack
            End If
            '聚合支持的游戏版本
            Dim seen As New HashSet(Of String)
            If el.TryGetProperty("latestFilesIndexes", Nothing) Then
                For Each f In el.GetProperty("latestFilesIndexes").EnumerateArray()
                    Dim gv = Str(f, "gameVersion")
                    If gv <> "" AndAlso seen.Add(gv) Then p.GameVersions.Add(gv)
                Next
            End If
            If el.TryGetProperty("latestFiles", Nothing) Then
                For Each f In el.GetProperty("latestFiles").EnumerateArray()
                    If f.TryGetProperty("gameVersions", Nothing) Then
                        For Each gv In f.GetProperty("gameVersions").EnumerateArray()
                            Dim s = gv.GetString()
                            If s IsNot Nothing AndAlso seen.Add(s) Then p.GameVersions.Add(s)
                        Next
                    End If
                Next
            End If
            '分类标签（CurseForge 分类 ID → 中文）
            If el.TryGetProperty("categories", Nothing) Then
                For Each c In el.GetProperty("categories").EnumerateArray()
                    Dim t = CfTagName(Num(c, "id"))
                    If t <> "" Then p.Tags.Add(t)
                Next
            End If
        Else 'Modrinth
            p.Platform = ResPlatform.Modrinth
            Dim idEl As JsonElement
            p.Id = If(el.TryGetProperty("project_id", idEl), Str(el, "project_id"), Str(el, "id"))
            p.Slug = Str(el, "slug")
            p.RawName = Str(el, "title")
            p.Description = Str(el, "description")
            Dim dm = If(el.TryGetProperty("date_modified", idEl), Str(el, "date_modified"), Str(el, "updated"))
            If dm <> "" Then Date.TryParse(dm, p.LastUpdate)
            p.DownloadCount = NumL(el, "downloads")
            p.LogoUrl = Str(el, "icon_url")
            Dim pt = Str(el, "project_type")
            Select Case pt
                Case "modpack" : p.Types = ResType.ModPack
                Case "resourcepack" : p.Types = ResType.ResourcePack
                Case "shader" : p.Types = ResType.Shader
                Case Else : p.Types = ResType.Mod
            End Select
            '游戏版本（搜索结果叫 versions，工程接口叫 game_versions）
            Dim gvKey As String = If(el.TryGetProperty("game_versions", Nothing), "game_versions",
                                     If(el.TryGetProperty("versions", Nothing), "versions", ""))
            If gvKey <> "" Then
                For Each v In el.GetProperty(gvKey).EnumerateArray()
                    Dim s = v.GetString()
                    If s IsNot Nothing Then p.GameVersions.Add(s)
                Next
            End If
            Dim webs As New HashSet(Of String)
            If el.TryGetProperty("loaders", Nothing) Then
                For Each l In el.GetProperty("loaders").EnumerateArray()
                    Dim s = l.GetString()
                    If s IsNot Nothing AndAlso loadersAll.Contains(s) Then
                        webs.Add(s)
                        p.ModLoaders.Add(s)
                    End If
                Next
            End If
            If el.TryGetProperty("categories", Nothing) Then
                For Each c In el.GetProperty("categories").EnumerateArray()
                    Dim s = c.GetString()
                    If s Is Nothing Then Continue For
                    If loadersAll.Contains(s) AndAlso Not webs.Contains(s) Then
                        p.ModLoaders.Add(s)
                        Continue For
                    End If
                    If s = "datapack" Then
                        p.Types = ResType.DataPack
                        Continue For
                    End If
                    Dim t = MrTagName(s)
                    If t <> "" Then p.Tags.Add(t)
                Next
            End If
            p.Website = "https://modrinth.com/" & pt & "/" & p.Slug
        End If
        If p.Tags.Count = 0 Then p.Tags.Add("其他")
        p.Tags.Sort()
        Return p
    End Function

    ''' <summary>列表上的版本描述：1.20.1 / 1.16.5 ~ 1.20.1 / 全版本 / 仅快照版本。</summary>
    Public ReadOnly Property GameVersionDesc As String
        Get
            Dim std = GameVersions.Where(Function(v) IsStandardVersion(v)).Distinct.ToList
            If std.Count = 0 Then Return "仅快照版本"
            std.Sort(Function(a, b) CompareVersion(b, a))
            Dim maxV = std(0), minV = std(std.Count - 1)
            If std.Count = 1 Then Return maxV
            If CompareVersion(maxV, "1.0") >= 0 AndAlso CompareVersion(minV, "1.0") >= 0 AndAlso
               maxV.Split("."c)(1) = minV.Split("."c)(1) AndAlso std.Count > 2 Then
                Return "全版本"
            End If
            Return minV & " ~ " & maxV
        End Get
    End Property

    ''' <summary>跨平台去重键：名称中的字母与数字。</summary>
    Public ReadOnly Property LikeKey As String
        Get
            Dim sb As New StringBuilder
            For Each ch In (Slug & RawName)
                If Char.IsLetterOrDigit(ch) Then sb.Append(Char.ToLower(ch))
            Next
            Return sb.ToString
        End Get
    End Property
End Class

''' <summary>
''' 社区资源的单个可下载版本（对应 Windows 版 ResourceVersion）。
''' </summary>
Public Class ResFile
    Public Property Id As String = ""
    Public Property ResType As ResType
    Public Property Display As String = ""
    Public Property Version As String = ""
    Public Property ReleaseDate As Date
    Public Property ReleaseType As Integer = 1 '1=Release 2=Beta 3=Alpha
    Public Property DownloadCount As Long
    Public Property FileName As String = ""
    Public Property Urls As New List(Of String)
    Public Property Hash As String = ""
    Public Property Size As Long = -1
    Public Property ModLoaders As New List(Of String)
    Public Property GameVersions As New List(Of String)
    Public Property Dependencies As New List(Of String)

    Public Shared Function Parse(el As JsonElement, resType As ResType) As ResFile
        Dim f As New ResFile With {.ResType = resType}
        If el.TryGetProperty("gameId", Nothing) Then 'CurseForge
            f.Id = Str(el, "id")
            f.Display = Str(el, "displayName").Replace(vbTab, "").Trim(" "c)
            Dim fd As String = Str(el, "fileDate")
            If fd <> "" Then Date.TryParse(fd, f.ReleaseDate)
            f.ReleaseType = Num(el, "releaseType")
            f.DownloadCount = NumL(el, "downloadCount")
            f.FileName = Str(el, "fileName")
            f.Size = NumL(el, "fileLength")
            If f.Size = 0 Then f.Size = -1
            If el.TryGetProperty("hashes", Nothing) Then
                For Each h In el.GetProperty("hashes").EnumerateArray()
                    Dim algo = Num(h, "algo")
                    If (algo = 1 OrElse algo = 2) AndAlso f.Hash = "" Then f.Hash = Str(h, "value").ToLower
                Next
            End If
            Dim url = Str(el, "downloadUrl")
            If url = "" Then
                Dim idStr = f.Id
                If idStr.Length > 4 Then url = "https://edge.forgecdn.net/files/" & idStr.Substring(0, 4) & "/" & idStr.Substring(4) & "/" & f.FileName
            End If
            If url <> "" Then
                f.Urls = ParseCfDownloadUrls(url)
                f.Urls.Add(MirrorMod(url)) '镜像源要求未编码文件名
                f.Urls = f.Urls.Distinct.ToList
            End If
            If el.TryGetProperty("dependencies", Nothing) Then
                For Each d In el.GetProperty("dependencies").EnumerateArray()
                    If Num(d, "relationType") = 3 Then
                        Dim mid = Num(d, "modId")
                        If mid <> 306612 AndAlso mid <> 634179 Then f.Dependencies.Add(mid.ToString) '排除 Fabric/Quilt API
                    End If
                Next
            End If
            Dim seen As New HashSet(Of String)
            If el.TryGetProperty("gameVersions", Nothing) Then
                For Each v In el.GetProperty("gameVersions").EnumerateArray()
                    Dim s = If(v.GetString(), "").Trim.ToLower
                    If loadersAll.Contains(s) Then
                        If Not f.ModLoaders.Contains(s) Then f.ModLoaders.Add(s)
                    ElseIf IsStandardVersion(s) AndAlso seen.Add(s) Then
                        f.GameVersions.Add(s)
                    End If
                Next
            End If
        Else 'Modrinth
            f.Id = Str(el, "id")
            f.Display = Str(el, "name").Replace(vbTab, "").Trim(" "c)
            f.Version = Str(el, "version_number")
            Dim dp As String = Str(el, "date_published")
            If dp <> "" Then Date.TryParse(dp, f.ReleaseDate)
            Dim vt = Str(el, "version_type")
            f.ReleaseType = If(vt = "release", 1, If(vt = "beta", 2, 3))
            f.DownloadCount = NumL(el, "downloads")
            If el.TryGetProperty("files", Nothing) Then
                For Each fe In el.GetProperty("files").EnumerateArray()
                    If fe.TryGetProperty("primary", Nothing) AndAlso Not fe.GetProperty("primary").GetBoolean() Then Continue For
                    f.FileName = Str(fe, "filename")
                    Dim u = Str(fe, "url")
                    If u <> "" Then
                        f.Urls.Add(u)
                        f.Urls.Add(MirrorMod(u))
                    End If
                    f.Size = NumL(fe, "size")
                    Dim hashes As JsonElement
                    If fe.TryGetProperty("hashes", hashes) Then f.Hash = Str(hashes, "sha1").ToLower
                    Exit For
                Next
            End If
            If el.TryGetProperty("dependencies", Nothing) Then
                For Each d In el.GetProperty("dependencies").EnumerateArray()
                    If Str(d, "dependency_type") = "required" Then
                        Dim pid = Str(d, "project_id")
                        If pid <> "" AndAlso pid <> "P7dR8mSH" AndAlso pid <> "qvIfYCYJ" Then f.Dependencies.Add(pid)
                    End If
                Next
            End If
            Dim rawLoaders As New List(Of String)
            If el.TryGetProperty("loaders", Nothing) Then
                For Each l In el.GetProperty("loaders").EnumerateArray()
                    Dim s = l.GetString()
                    If s IsNot Nothing Then rawLoaders.Add(s)
                Next
            End If
            For Each l In rawLoaders
                If loadersAll.Contains(l) AndAlso Not f.ModLoaders.Contains(l) Then f.ModLoaders.Add(l)
            Next
            If (resType = ResType.Mod OrElse resType = ResType.DataPack) Then
                If rawLoaders.Any(Function(l) {"bukkit", "folia", "paper", "purpur", "spigot"}.Contains(l)) Then f.ResType = ResType.Plugin
                If rawLoaders.Contains("datapack") Then f.ResType = ResType.DataPack
                If f.ModLoaders.Count > 0 Then f.ResType = ResType.Mod
            End If
            If el.TryGetProperty("game_versions", Nothing) Then
                For Each v In el.GetProperty("game_versions").EnumerateArray()
                    Dim s = If(v.GetString(), "").Trim.ToLower
                    If s.Contains(".") AndAlso IsStandardVersion(s) AndAlso Not f.GameVersions.Contains(s) Then f.GameVersions.Add(s)
                Next
            End If
        End If
        Return f
    End Function

    ''' <summary>版本行的描述：加载器、游戏版本、下载量、更新时间。</summary>
    Public ReadOnly Property InfoLine As String
        Get
            Dim parts As New List(Of String)
            If ModLoaders.Count > 0 Then parts.Add(JoinLoaders(ModLoaders))
            If GameVersions.Count > 0 Then parts.Add("游戏版本 " & GameVersions(0) & If(GameVersions.Count > 1, "+", ""))
            If DownloadCount > 0 Then
                parts.Add("下载 " & If(DownloadCount > 100000, (DownloadCount \ 10000) & " 万", DownloadCount.ToString) & " 次")
            End If
            Dim days = (Date.Now - ReleaseDate).TotalDays
            parts.Add("更新于 " & If(days > 365, (days \ 365) & " 年前", If(days > 30, (days \ 30) & " 个月前", If(days > 1, (days \ 1) & " 天前", "今天"))))
            If ReleaseType <> 1 Then parts.Add(If(ReleaseType = 2, "测试版", "早期测试版"))
            Return String.Join("，", parts)
        End Get
    End Property
End Class

''' <summary>
''' 社区资源搜索与下载（对应 Windows 版 ModDownloadLib / ResourceSearcher / ResourceVersion）。
''' 全部走 MCIM 镜像（国内直连 CurseForge/Modrinth 不通），Modrinth 镜像失败时回退官方。
''' </summary>
Public Module ModResource

    Public Const McimBase As String = "https://mod.mcimirror.top"

    Friend ReadOnly loadersAll As String() = {"forge", "neoforge", "fabric", "quilt", "liteloader"}

    ''' <summary>把 CurseForge/Modrinth API 地址换到 MCIM 镜像。</summary>
    Public Function MirrorMod(url As String) As String
        Return url.
            Replace("api.modrinth.com", "mod.mcimirror.top/modrinth").
            Replace("cdn.modrinth.com", "mod.mcimirror.top").
            Replace("api.curseforge.com", "mod.mcimirror.top/curseforge").
            Replace("edge.forgecdn.net", "mod.mcimirror.top").
            Replace("mediafilez.forgecdn.net", "mod.mcimirror.top").
            Replace("media.forgecdn.net", "mod.mcimirror.top")
    End Function

    ''' <summary>整理 CurseForge 下载地址的多种形态。</summary>
    Public Function ParseCfDownloadUrls(url As String) As List(Of String)
        Return {
            url.Replace("-service.overwolf.wtf", ".forgecdn.net").Replace("://edge.", "://mediafilez.").Replace("://media.", "://mediafilez."),
            url.Replace("://edge.", "://mediafilez.").Replace("://media.", "://mediafilez."),
            url.Replace("-service.overwolf.wtf", ".forgecdn.net"),
            url.Replace("://media.", "://edge."),
            url
        }.Distinct.ToList
    End Function

    Private _resClient As HttpClient = Nothing
    Private Function ResClient() As HttpClient
        If _resClient Is Nothing Then
            Dim handler As New SocketsHttpHandler With {
                .AutomaticDecompression = System.Net.DecompressionMethods.All
            }
            _resClient = New HttpClient(handler) With {.Timeout = TimeSpan.FromSeconds(30)}
            _resClient.DefaultRequestHeaders.UserAgent.ParseAdd("TYWLauncher/1.0 (Linux)")
        End If
        Return _resClient
    End Function

    ''' <summary>资源 API 请求：镜像优先，Modrinth 官方兜底。返回根 JsonElement。</summary>
    Public Function ResRequest(url As String) As JsonElement
        Dim errors As New List(Of Exception)
        Dim urls As New List(Of String) From {MirrorMod(url)}
        If url.Contains("modrinth") Then urls.Add(url) 'Modrinth 官方国内可直连，作兜底
        urls.Add(MirrorMod(url))
        For Each u In urls
            Try
                Using resp = ResClient().GetAsync(u).GetAwaiter().GetResult()
                    resp.EnsureSuccessStatusCode()
                    Dim doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult())
                    Return doc.RootElement.Clone()
                End Using
            Catch ex As Exception
                errors.Add(ex)
            End Try
        Next
        Throw New Exception("资源请求失败：" & url, If(errors.Count > 0, errors(0), Nothing))
    End Function

    '===================== 搜索 =====================

    Public Class SearchResult
        Public Property Projects As New List(Of ResProject)
        Public Property ErrorMessage As String = ""
        Public Property CurseForgeOffset As Integer = 0
        Public Property CurseForgeTotal As Integer = -1
        Public Property ModrinthOffset As Integer = 0
        Public Property ModrinthTotal As Integer = -1
    End Class

    Public Class SearchQuery
        Public Property Type As ResType
        Public Property SearchText As String = ""
        Public Property GameVersion As String = ""
        Public Property Loader As String = "" 'forge/neoforge/fabric/quilt，空不筛选
        Public Property PageSize As Integer = 40
    End Class

    ''' <summary>从两个平台各取一页搜索结果并合并去重排序（对应 ResourceSearcher.Run）。</summary>
    Public Function Search(query As SearchQuery, storage As SearchResult) As SearchResult
        Dim results As New Concurrent.ConcurrentBag(Of ResProject)
        Dim errCf As Exception = Nothing, errMr As Exception = Nothing

        'CurseForge
        Dim cfOk = storage.CurseForgeTotal < 0 OrElse storage.CurseForgeOffset < storage.CurseForgeTotal
        If cfOk Then
            Try
                Dim addr = "https://api.curseforge.com/v1/mods/search?gameId=432&sortField=2&sortOrder=desc&pageSize=" & query.PageSize
                Select Case query.Type
                    Case ResType.Mod : addr &= "&classId=6"
                    Case ResType.ModPack : addr &= "&classId=4471"
                    Case ResType.DataPack : addr &= "&classId=6945"
                    Case ResType.Shader : addr &= "&classId=6552"
                    Case ResType.ResourcePack : addr &= "&classId=12"
                End Select
                If query.Loader <> "" Then addr &= "&modLoaderType=" & CfLoaderId(query.Loader)
                If query.GameVersion <> "" Then addr &= "&gameVersion=" & Uri.EscapeDataString(query.GameVersion)
                If query.SearchText <> "" Then addr &= "&searchFilter=" & Uri.EscapeDataString(query.SearchText)
                If storage.CurseForgeOffset > 0 Then addr &= "&index=" & storage.CurseForgeOffset
                Dim json = ResRequest(addr)
                Dim data = json.GetProperty("data")
                Dim cnt = 0
                For Each entry In data.EnumerateArray()
                    Dim p = ResProject.Parse(entry)
                    If query.Type = ResType.ResourcePack AndAlso p.Tags.Contains("数据包") Then Continue For
                    results.Add(p)
                    cnt += 1
                Next
                storage.CurseForgeOffset += cnt
                storage.CurseForgeTotal = Num(json.GetProperty("pagination"), "totalCount")
            Catch ex As Exception
                ModLog.Err(ex, "CurseForge 搜索")
                storage.CurseForgeTotal = -1
                errCf = ex
            End Try
        End If

        'Modrinth
        Dim mrOk = storage.ModrinthTotal < 0 OrElse storage.ModrinthOffset < storage.ModrinthTotal
        If mrOk Then
            Try
                Dim addr = "https://api.modrinth.com/v2/search?limit=" & query.PageSize & "&index=relevance"
                If query.SearchText <> "" Then addr &= "&query=" & Uri.EscapeDataString(query.SearchText)
                If storage.ModrinthOffset > 0 Then addr &= "&offset=" & storage.ModrinthOffset
                Dim facets As New List(Of String)
                Dim mrType As String = query.Type.ToString.ToLower
                If query.Type = ResType.DataPack Then mrType = "mod" 'Modrinth 把数据包归为 mod 分类
                facets.Add("[`project_type:" & mrType & "`]")
                If query.Loader <> "" Then facets.Add("[`categories:" & query.Loader & "`]")
                If query.GameVersion <> "" Then facets.Add("[`versions:'" & query.GameVersion & "'`]")
                addr &= "&facets=" & Uri.EscapeDataString("[" & String.Join(",", facets) & "]")
                Dim json = ResRequest(addr)
                Dim hits = json.GetProperty("hits")
                Dim cnt = 0
                For Each h In hits.EnumerateArray()
                    results.Add(ResProject.Parse(h))
                    cnt += 1
                Next
                storage.ModrinthOffset += cnt
                storage.ModrinthTotal = Num(json, "total_hits")
            Catch ex As Exception
                ModLog.Err(ex, "Modrinth 搜索")
                storage.ModrinthTotal = -1
                errMr = ex
            End Try
        End If

        If results.Count = 0 Then
            If errCf IsNot Nothing Then Throw New Exception("无法连接到 CurseForge：" & errCf.Message, errCf)
            If errMr IsNot Nothing Then Throw New Exception("无法连接到 Modrinth：" & errMr.Message, errMr)
            storage.ErrorMessage = "没有搜索结果"
            Return storage
        End If
        If errCf IsNot Nothing Then storage.ErrorMessage = "无法连接到 CurseForge，仅显示来自 Modrinth 的内容。"
        If errMr IsNot Nothing Then storage.ErrorMessage = "无法连接到 Modrinth，仅显示来自 CurseForge 的内容。"

        '去重（跨平台同名工程）
        Dim merged As New List(Of ResProject)
        Dim keys As New HashSet(Of String)
        'CF 排前（与 Windows 版一致），再 Modrinth
        For Each p In results.OrderBy(Function(x) If(x.Platform = ResPlatform.CurseForge, 0, 1))
            If keys.Add(p.LikeKey) Then merged.Add(p)
        Next
        '排序：无搜索词按下载量（带平台权重），有搜索词保持 API 相关度顺序
        If query.SearchText = "" Then
            Dim mult = Function(p As ResProject) As Double
                           Select Case query.Type
                               Case ResType.Mod, ResType.ModPack
                                   Return If(p.Platform = ResPlatform.CurseForge, 1, 5)
                               Case ResType.DataPack
                                   Return If(p.Platform = ResPlatform.CurseForge, 10, 1)
                               Case Else
                                   Return If(p.Platform = ResPlatform.CurseForge, 1, 4)
                           End Select
                       End Function
            merged.Sort(Function(a, b) (b.DownloadCount * mult(b)).CompareTo(a.DownloadCount * mult(a)))
        End If
        storage.Projects.AddRange(merged)
        Return storage
    End Function

    '===================== 工程与版本 =====================

    Private _projectCache As New Dictionary(Of String, ResProject)

    ''' <summary>按工程 ID 获取工程信息。</summary>
    Public Function ProjectById(projectId As String, platform As ResPlatform) As ResProject
        If _projectCache.ContainsKey(projectId) Then Return _projectCache(projectId)
        Dim json As JsonElement
        If platform = ResPlatform.CurseForge Then
            json = ResRequest("https://api.curseforge.com/v1/mods/" & projectId).GetProperty("data")
        Else
            json = ResRequest("https://api.modrinth.com/v2/project/" & projectId)
        End If
        Dim p = ResProject.Parse(json)
        _projectCache(projectId) = p
        Return p
    End Function

    Private _filesCache As New Dictionary(Of String, List(Of ResFile))

    ''' <summary>按工程 ID 获取全部版本文件列表。</summary>
    Public Function FilesOf(projectId As String, platform As ResPlatform, resType As ResType) As List(Of ResFile)
        If _filesCache.ContainsKey(projectId) Then Return _filesCache(projectId)
        Dim arr As JsonElement
        If platform = ResPlatform.CurseForge Then
            Dim lastEx As Exception = Nothing
            For retry = 10000 To 10002
                Try
                    Dim json = ResRequest("https://api.curseforge.com/v1/mods/" & projectId & "/files?pageSize=" & retry)
                    If Num(json.GetProperty("pagination"), "resultCount") = Num(json.GetProperty("pagination"), "totalCount") Then
                        arr = json.GetProperty("data")
                        lastEx = Nothing
                        Exit For
                    End If
                Catch ex As Exception
                    lastEx = ex
                End Try
            Next
            If lastEx IsNot Nothing AndAlso arr.ValueKind = JsonValueKind.Undefined Then Throw New Exception("CurseForge 版本列表获取失败", lastEx)
        Else
            arr = ResRequest("https://api.modrinth.com/v2/project/" & projectId & "/version?include_changelog=false")
        End If
        Dim list As New List(Of ResFile)
        Dim seen As New HashSet(Of String)
        For Each el In arr.EnumerateArray()
            Dim f = ResFile.Parse(el, resType)
            If f.Urls.Count = 0 OrElse f.FileName = "" Then Continue For
            If seen.Add(f.Id) Then list.Add(f)
        Next
        '按时间降序
        list.Sort(Function(a, b) b.ReleaseDate.CompareTo(a.ReleaseDate))
        _filesCache(projectId) = list
        Return list
    End Function

    ''' <summary>给一批 ResFile 挂上前置工程的名称（对应依赖加载）。</summary>
    Public Sub LoadDependencyNames(files As List(Of ResFile), platform As ResPlatform)
        Dim ids = files.SelectMany(Function(f) f.Dependencies).Distinct.ToList
        For Each depId In ids
            Try
                Dim p = ProjectById(depId, platform)
                For Each f In files
                    If f.Dependencies.Contains(depId) Then f.Dependencies(f.Dependencies.IndexOf(depId)) = p.RawName
                Next
            Catch
            End Try
        Next
    End Sub

    '===================== 下载 =====================

    ''' <summary>把资源文件下载到目标目录（依次尝试 URL 列表，校验 SHA1 与大小）。</summary>
    Public Sub DownloadResFile(file As ResFile, destDir As String,
                               Optional progress As Action(Of Double, String) = Nothing,
                               Optional cancel As Func(Of Boolean) = Nothing,
                               Optional statusPrefix As String = "")
        Dim dest = Path.Combine(destDir, file.FileName)
        Directory.CreateDirectory(destDir)
        Dim tmp = dest & ".tmp"
        Dim lastEx As Exception = Nothing
        For Each url In file.Urls
            Try
                Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(120))
                    Using resp = ResClient().GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult()
                        resp.EnsureSuccessStatusCode()
                        Dim total As Long = If(file.Size > 0, file.Size, resp.Content.Headers.ContentLength.GetValueOrDefault(-1))
                        Using src = resp.Content.ReadAsStreamAsync(cts.Token).GetAwaiter().GetResult()
                            Using dst = New FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920)
                                Dim buf(81919) As Byte
                                Dim done As Long = 0
                                Do
                                    If cancel IsNot Nothing AndAlso cancel() Then Throw New OperationCanceledException()
                                    Dim n = src.ReadAsync(buf, 0, buf.Length, cts.Token).GetAwaiter().GetResult()
                                    If n <= 0 Then Exit Do
                                    dst.Write(buf, 0, n)
                                    done += n
                                    If progress IsNot Nothing AndAlso total > 0 Then
                                        progress(Math.Min(1, done / total), statusPrefix & "下载 " & file.FileName & " " & Math.Floor(100.0 * done / total) & "%")
                                    End If
                                Loop
                            End Using
                        End Using
                    End Using
                End Using
                '校验
                If file.Hash <> "" Then
                    Dim sha = ComputeSha1(tmp)
                    If sha <> file.Hash.ToLower Then Throw New Exception("文件校验失败（SHA1 不匹配）")
                End If
                If System.IO.File.Exists(dest) Then System.IO.File.Delete(dest)
                System.IO.File.Move(tmp, dest)
                If progress IsNot Nothing Then progress(1, "已完成 " & file.FileName)
                Return
            Catch ex As OperationCanceledException
                Try
                    If System.IO.File.Exists(tmp) Then System.IO.File.Delete(tmp)
                Catch
                End Try
                Throw
            Catch ex As Exception
                lastEx = ex
                Try
                    If System.IO.File.Exists(tmp) Then System.IO.File.Delete(tmp)
                Catch
                End Try
            End Try
        Next
        Throw New Exception("下载失败：" & file.FileName & " —— " & If(lastEx?.Message, "全部来源不可用"), lastEx)
    End Sub

    Public Function ComputeSha1(path As String) As String
        Using sha = SHA1.Create()
            Using fs = File.OpenRead(path)
                Return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLower
            End Using
        End Using
    End Function

    '===================== 资源安装目标 =====================

    ''' <summary>资源安装的目标目录（Mod → mods，资源包 → resourcepacks，光影 → shaderpacks）。</summary>
    Public Function InstallDirFor(resType As ResType) As String
        Select Case resType
            Case ResType.Mod, ResType.Plugin
                Return Path.Combine(ModPaths.McFolder, "mods")
            Case ResType.ResourcePack
                Return Path.Combine(ModPaths.McFolder, "resourcepacks")
            Case ResType.Shader
                Return Path.Combine(ModPaths.McFolder, "shaderpacks")
            Case Else
                Throw New Exception("该类别需要指定具体安装位置")
        End Select
    End Function

    ''' <summary>数据包安装目标：选中的世界目录。</summary>
    Public Function DataPackDir(world As String) As String
        Return Path.Combine(ModPaths.McFolder, "saves", world, "datapacks")
    End Function

    ''' <summary>列出可装数据包的世界。</summary>
    Public Function ListWorlds() As List(Of String)
        Dim result As New List(Of String)
        Dim dir = Path.Combine(ModPaths.McFolder, "saves")
        If Directory.Exists(dir) Then
            For Each d In Directory.GetDirectories(dir)
                If File.Exists(Path.Combine(d, "level.dat")) Then result.Add(Path.GetFileName(d))
            Next
        End If
        Return result
    End Function

    '===================== 整合包安装 =====================

    ''' <summary>
    ''' 安装整合包（对应 Windows 版 ModModpack）。
    ''' 支持 .mrpack（Modrinth）与 CurseForge zip；overrides 解压到实例目录，Mod 文件自动下载，
    ''' Fabric/Quilt 自动创建版本 JSON，Forge/NeoForge 下载官方安装器静默安装。
    ''' 返回新实例名。
    ''' </summary>
    Public Function InstallModPack(packPath As String, instanceName As String,
                                   Optional progress As Action(Of Double, String) = Nothing,
                                   Optional cancel As Func(Of Boolean) = Nothing) As String
        Dim ext = Path.GetExtension(packPath).ToLower
        Dim baseDir = Path.Combine(ModPaths.McFolder, "versions", instanceName)
        If Directory.Exists(baseDir) Then Throw New Exception("版本 " & instanceName & " 已存在")
        Directory.CreateDirectory(baseDir)
        Dim extractDir = Path.Combine(ModPaths.TempDir, "pack_" & instanceName)
        If Directory.Exists(extractDir) Then Directory.Delete(extractDir, True)
        Try
            If progress IsNot Nothing Then progress(0.02, "解压整合包…")
            ZipFile.ExtractToDirectory(packPath, extractDir, True)

            Dim mcVersion As String = "", loaderName As String = "", loaderVersion As String = ""
            Dim downloadTasks As New List(Of (Urls As List(Of String), Hash As String, Size As Long, RelPath As String))
            Dim overridesDir As String = ""

            If ext = ".mrpack" Then
                Dim indexDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(extractDir, "modrinth.index.json")))
                Dim root = indexDoc.RootElement
                mcVersion = Str(root.GetProperty("dependencies"), "minecraft")
                loaderName = If(Str(root.GetProperty("dependencies"), "fabric-loader") <> "", "fabric",
                            If(Str(root.GetProperty("dependencies"), "quilt-loader") <> "", "quilt",
                            If(Str(root.GetProperty("dependencies"), "forge") <> "", "forge",
                            If(Str(root.GetProperty("dependencies"), "neoforge") <> "", "neoforge", ""))))
                loaderVersion = If(loaderName = "fabric", Str(root.GetProperty("dependencies"), "fabric-loader"),
                               If(loaderName = "quilt", Str(root.GetProperty("dependencies"), "quilt-loader"),
                               If(loaderName = "forge", Str(root.GetProperty("dependencies"), "forge"),
                               Str(root.GetProperty("dependencies"), "neoforge"))))
                If root.TryGetProperty("files", Nothing) Then
                    For Each f In root.GetProperty("files").EnumerateArray()
                        Dim envClient As String = "required"
                        Dim env As JsonElement
                        If f.TryGetProperty("env", env) Then envClient = If(Str(env, "client"), "required")
                        If envClient = "unsupported" Then Continue For
                        Dim urls As New List(Of String)
                        For Each u In f.GetProperty("downloads").EnumerateArray()
                            urls.Add(u.GetString())
                            urls.Add(MirrorMod(u.GetString()))
                        Next
                        Dim hashes As JsonElement
                        Dim sha = ""
                        If f.TryGetProperty("hashes", hashes) Then sha = Str(hashes, "sha1")
                        downloadTasks.Add((urls.Distinct.ToList, sha, NumL(f, "fileSize"), Str(f, "path")))
                    Next
                End If
                overridesDir = Path.Combine(extractDir, "overrides")
            ElseIf ext = ".zip" Then
                Dim manifestDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(extractDir, "manifest.json")))
                Dim root = manifestDoc.RootElement
                Dim mc As JsonElement
                If root.TryGetProperty("minecraft", mc) Then mcVersion = Str(mc, "version")
                If root.TryGetProperty("modloaders", Nothing) Then
                    For Each ml In root.GetProperty("modloaders").EnumerateArray()
                        Dim id = Str(ml, "id") '形如 forge-47.1.0 / fabric-0.14.21
                        Dim dash = id.IndexOf("-"c)
                        If dash > 0 Then
                            loaderName = id.Substring(0, dash).ToLower
                            loaderVersion = id.Substring(dash + 1)
                        End If
                    Next
                End If
                If root.TryGetProperty("files", Nothing) Then
                    Dim ids As New List(Of Long)
                    For Each f In root.GetProperty("files").EnumerateArray()
                        Dim req As Boolean = True
                        If f.TryGetProperty("required", Nothing) Then req = f.GetProperty("required").GetBoolean()
                        If req Then ids.Add(NumL(f, "fileID"))
                    Next
                    If ids.Count > 0 Then
                        If progress IsNot Nothing Then progress(0.1, "获取整合包 Mod 信息（" & ids.Count & " 个文件）…")
                        Dim body = "{""modIds"": [" & String.Join(",", ids) & "]}"
                        Dim json = ResRequestPost("https://api.curseforge.com/v1/mods/files", body)
                        For Each fe In json.GetProperty("data").EnumerateArray()
                            Dim f = ResFile.Parse(fe, ResType.Mod)
                            If f.Urls.Count = 0 Then Continue For
                            downloadTasks.Add((f.Urls, f.Hash, f.Size, "mods/" & f.FileName))
                        Next
                    End If
                End If
                overridesDir = Path.Combine(extractDir, "overrides")
            Else
                Throw New Exception("不支持的整合包格式：" & ext)
            End If

            '解压 overrides 到实例目录（版本隔离：该目录即游戏目录）
            If Directory.Exists(overridesDir) Then
                If progress IsNot Nothing Then progress(0.15, "应用整合包配置…")
                CopyDir(overridesDir, baseDir)
            End If

            '下载 Mod 等文件
            If downloadTasks.Count > 0 Then
                Dim done As Integer = 0
                For Each t In downloadTasks
                    If cancel IsNot Nothing AndAlso cancel() Then Throw New OperationCanceledException()
                    done += 1
                    If progress IsNot Nothing Then progress(0.2 + 0.6 * done / downloadTasks.Count, "下载整合包文件 " & done & "/" & downloadTasks.Count)
                    Dim f As New ResFile With {.FileName = Path.GetFileName(t.RelPath.Replace("\"c, "/"c).Split("/"c).Last), .Hash = t.Hash, .Size = t.Size, .Urls = t.Urls}
                    DownloadResFile(f, Path.GetDirectoryName(Path.Combine(baseDir, t.RelPath.Replace("\"c, "/"c))), progress, cancel, "")
                Next
            End If

            '写版本 JSON（Loader）
            If progress IsNot Nothing Then progress(0.85, "创建版本 " & instanceName & "…")
            WritePackVersionJson(instanceName, mcVersion, loaderName, loaderVersion, progress, cancel)

            ModVersion.Invalidate()
            If progress IsNot Nothing Then progress(1, "整合包安装完成")
            Return instanceName
        Finally
            Try
                If Directory.Exists(extractDir) Then Directory.Delete(extractDir, True)
            Catch
            End Try
        End Try
    End Function

    ''' <summary>为整合包创建版本 JSON：Fabric/Quilt 直接用官方 profile；Forge/NeoForge 跑官方安装器。</summary>
    Private Sub WritePackVersionJson(name As String, mcVersion As String, loaderName As String, loaderVersion As String,
                                     progress As Action(Of Double, String), cancel As Func(Of Boolean))
        Dim verDir = Path.Combine(ModPaths.McFolder, "versions", name)
        If loaderName = "fabric" OrElse loaderName = "quilt" Then
            'meta 的 profile 是完整版本 JSON（含 inheritsFrom），官方源国内可直连
            Dim meta As String
            If loaderName = "fabric" Then
                meta = "https://meta.fabricmc.net/v2/versions/loader/" & mcVersion & "/" & loaderVersion & "/profile/json"
            Else
                meta = "https://meta.quiltmc.org/v3/versions/loader/" & mcVersion & "/" & loaderVersion & "/profile/json"
            End If
            Dim jsonText As String
            Try
                jsonText = New WebClient2().DownloadString(meta)
            Catch ex As Exception
                'Fabric meta 官方不可达时走镜像
                jsonText = New WebClient2().DownloadString("https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/" & mcVersion & "/" & loaderVersion & "/profile/json")
            End Try
            Dim oldId = jsonIdOf(jsonText)
            If oldId <> "" Then jsonText = jsonText.Replace("""id"": """ & oldId & """", """id"": """ & name & """")
            File.WriteAllText(Path.Combine(verDir, name & ".json"), jsonText, New Text.UTF8Encoding(False))
            Return
        End If
        If loaderName = "forge" OrElse loaderName = "neoforge" Then
            '下载官方安装器后静默安装到 .minecraft，再把生成版本的 JSON 复制成本实例（inheritsFrom 指向原版）
            Dim installerUrl As String, verId As String
            If loaderName = "forge" Then
                verId = mcVersion & "-forge-" & loaderVersion
                installerUrl = "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/" & mcVersion & "-" & loaderVersion & "/forge-" & mcVersion & "-" & loaderVersion & "-installer.jar"
            Else
                verId = mcVersion & "-neoforge-" & loaderVersion
                installerUrl = "https://bmclapi2.bangbang93.com/maven/net/neoforged/forge/" & mcVersion & "-" & loaderVersion & "/forge-" & mcVersion & "-" & loaderVersion & "-installer.jar"
            End If
            Dim installerPath = Path.Combine(ModPaths.TempDir, "forge-installer-" & loaderVersion & ".jar")
            Dim f As New ResFile With {.FileName = Path.GetFileName(installerPath), .Urls = New List(Of String) From {installerUrl, MirrorMod(installerUrl)}}
            DownloadResFile(f, ModPaths.TempDir, progress, cancel, "下载 Forge 安装器 ")
            '找 Java 跑安装器
            Dim java = ModJava.Best(8)
            If java Is Nothing Then Throw New Exception("未找到可用的 Java，无法安装 Forge；请先在设置中配置 Java")
            If progress IsNot Nothing Then progress(0.92, "运行 Forge 安装器（可能需要几分钟）…")
            Dim psi As New ProcessStartInfo With {
                .FileName = java.Path,
                .Arguments = "-jar """ & installerPath & """ --installClient """ & ModPaths.McFolder & """",
                .UseShellExecute = False,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .CreateNoWindow = True
            }
            Using p = Process.Start(psi)
                p.WaitForExit()
                If p.ExitCode <> 0 Then Throw New Exception("Forge 安装器退出码 " & p.ExitCode)
            End Using
            'Forge 装出的版本 JSON 的 inheritsFrom 是原版，把它链到我们的实例名下
            Dim forgeJson = Path.Combine(ModPaths.McFolder, "versions", verId, verId & ".json")
            If Not File.Exists(forgeJson) Then Throw New Exception("Forge 安装器未生成版本 JSON：" & verId)
            Dim text = File.ReadAllText(forgeJson)
            text = text.Replace("""id"": """ & verId & """", """id"": """ & name & """")
            File.WriteAllText(Path.Combine(verDir, name & ".json"), text, New Text.UTF8Encoding(False))
            Return
        End If
        '没有 loader：纯原版整合包；原版没装时先装
        EnsureVanillaInstalled(mcVersion, progress, cancel)
        Dim vanillaJson = Path.Combine(ModPaths.McFolder, "versions", mcVersion, mcVersion & ".json")
        Dim vanillaText = File.ReadAllText(vanillaJson)
        vanillaText = vanillaText.Replace("""id"": """ & mcVersion & """", """id"": """ & name & """")
        File.WriteAllText(Path.Combine(verDir, name & ".json"), vanillaText, New Text.UTF8Encoding(False))
    End Sub

    ''' <summary>确保某个原版版本已安装（整合包前置）。</summary>
    Public Sub EnsureVanillaInstalled(mcVersion As String, progress As Action(Of Double, String), cancel As Func(Of Boolean))
        Dim json = Path.Combine(ModPaths.McFolder, "versions", mcVersion, mcVersion & ".json")
        If File.Exists(json) Then Return
        Dim target = ModDownload.Manifest().FirstOrDefault(Function(v) v.Id = mcVersion)
        If target Is Nothing Then Throw New Exception("整合包需要的 Minecraft " & mcVersion & " 不在版本列表中")
        ModDownload.Install(mcVersion, target.Url,
                            Sub(p, s) If progress IsNot Nothing Then progress(0.2 + 0.6 * p, "先安装原版 " & mcVersion & "：" & s), cancel)
    End Sub

    Private Function jsonIdOf(jsonText As String) As String
        Try
            Dim doc = JsonDocument.Parse(jsonText)
            Return Str(doc.RootElement, "id")
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>CurseForge POST 请求（批量取文件信息）。</summary>
    Public Function ResRequestPost(url As String, body As String) As JsonElement
        Dim errors As New List(Of Exception)
        For Each u In {MirrorMod(url), url}
            Try
                Dim content = New StringContent(body, Encoding.UTF8, "application/json")
                Using resp = ResClient().PostAsync(u, content).GetAwaiter().GetResult()
                    resp.EnsureSuccessStatusCode()
                    Dim doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult())
                    Return doc.RootElement.Clone()
                End Using
            Catch ex As Exception
                errors.Add(ex)
            End Try
        Next
        Throw New Exception("请求失败：" & url, If(errors.Count > 0, errors(0), Nothing))
    End Function

    Private Sub CopyDir(src As String, dst As String)
        Directory.CreateDirectory(dst)
        For Each f In Directory.GetFiles(src, "*", IO.SearchOption.AllDirectories)
            Dim rel = f.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            Dim target = Path.Combine(dst, rel)
            Directory.CreateDirectory(Path.GetDirectoryName(target))
            File.Copy(f, target, True)
        Next
    End Sub

    '===================== 杂项 =====================

    Public Function IsStandardVersion(v As String) As Boolean
        If String.IsNullOrEmpty(v) Then Return False
        '1.x / 26.x / b1.x 不算标准
        Return System.Text.RegularExpressions.Regex.IsMatch(v, "^\d+\.\d+(\.\d+)?$")
    End Function

    ''' <summary>版本号比较（对应 McUtils.CompareVersion）。</summary>
    Public Function CompareVersion(a As String, b As String) As Integer
        Dim pa = a.Split("."c), pb = b.Split("."c)
        For i = 0 To Math.Max(pa.Length, pb.Length) - 1
            Dim va = If(i < pa.Length, Val(pa(i)), 0)
            Dim vb = If(i < pb.Length, Val(pb(i)), 0)
            If va <> vb Then Return va.CompareTo(vb)
        Next
        Return 0
    End Function

    ''' <summary>CurseForge modLoaderType。</summary>
    Public Function CfLoaderId(loader As String) As Integer
        Select Case loader
            Case "forge" : Return 1
            Case "fabric" : Return 4
            Case "quilt" : Return 5
            Case "neoforge" : Return 6
            Case Else : Return 0
        End Select
    End Function

    Public Function JoinLoaders(loaders As List(Of String)) As String
        Return String.Join(" / ", loaders.Select(Function(l) l.Substring(0, 1).ToUpper & l.Substring(1)))
    End Function

    Friend Function CfTagName(id As Long) As String
        Select Case id
            Case 406 : Return "世界元素"
            Case 407 : Return "生物群系"
            Case 410 : Return "维度"
            Case 408 : Return "矿物/资源"
            Case 409 : Return "天然结构"
            Case 412 : Return "科技"
            Case 415 : Return "管道/物流"
            Case 4843 : Return "自动化"
            Case 417 : Return "能源"
            Case 4558 : Return "红石"
            Case 436 : Return "食物/烹饪"
            Case 416 : Return "农业"
            Case 414 : Return "运输"
            Case 420 : Return "仓储"
            Case 419 : Return "魔法"
            Case 422 : Return "冒险"
            Case 424 : Return "装饰"
            Case 411 : Return "生物"
            Case 434 : Return "装备"
            Case 6814 : Return "性能优化"
            Case 9026 : Return "创造模式"
            Case 423 : Return "信息显示"
            Case 435 : Return "服务器"
            Case 5191 : Return "改良"
            Case 421 : Return "支持库"
            Case 4484 : Return "多人"
            Case 4479 : Return "硬核"
            Case 4483 : Return "战斗"
            Case 4478 : Return "任务"
            Case 4472 : Return "科技"
            Case 4473 : Return "魔法"
            Case 4475 : Return "冒险"
            Case 4476 : Return "探索"
            Case 4477 : Return "小游戏"
            Case 4474 : Return "科幻"
            Case 4736 : Return "空岛"
            Case 5128 : Return "原版改良"
            Case 4487 : Return "FTB"
            Case 4480 : Return "基于地图"
            Case 4481 : Return "轻量"
            Case 4482 : Return "大型"
            Case 403 : Return "原版风"
            Case 400 : Return "写实风"
            Case 401 : Return "现代风"
            Case 402 : Return "中世纪"
            Case 399 : Return "蒸汽朋克"
            Case 5244 : Return "含字体"
            Case 404 : Return "动态效果"
            Case 4465 : Return "兼容 Mod"
            Case 393 : Return "16x"
            Case 394 : Return "32x"
            Case 395 : Return "64x"
            Case 396 : Return "128x"
            Case 397 : Return "256x"
            Case 398 : Return "超高清"
            Case 5193 : Return "数据包"
            Case 6553 : Return "写实风"
            Case 6554 : Return "幻想风"
            Case 6555 : Return "原版风"
            Case 6948 : Return "冒险"
            Case 6949 : Return "幻想"
            Case 6950 : Return "支持库"
            Case 6952 : Return "魔法"
            Case 6946 : Return "Mod 相关"
            Case 6951 : Return "科技"
            Case 6953 : Return "实用"
        End Select
        Return ""
    End Function

    Friend Function MrTagName(tag As String) As String
        Select Case tag
            Case "technology" : Return "科技"
            Case "magic" : Return "魔法"
            Case "adventure" : Return "冒险"
            Case "utility" : Return "实用"
            Case "optimization" : Return "性能优化"
            Case "vanilla-like" : Return "原版风"
            Case "realistic" : Return "写实风"
            Case "worldgen" : Return "世界元素"
            Case "food" : Return "食物/烹饪"
            Case "game-mechanics" : Return "游戏机制"
            Case "transportation" : Return "运输"
            Case "storage" : Return "仓储"
            Case "decoration" : Return "装饰"
            Case "mobs" : Return "生物"
            Case "equipment" : Return "装备"
            Case "social" : Return "服务器"
            Case "library" : Return "支持库"
            Case "multiplayer" : Return "多人"
            Case "challenging" : Return "硬核"
            Case "combat" : Return "战斗"
            Case "quests" : Return "任务"
            Case "kitchen-sink" : Return "水槽包"
            Case "lightweight" : Return "轻量"
            Case "simplistic" : Return "简洁"
            Case "tweaks" : Return "改良"
            Case "8x-" : Return "极简"
            Case "16x" : Return "16x"
            Case "32x" : Return "32x"
            Case "48x" : Return "48x"
            Case "64x" : Return "64x"
            Case "128x" : Return "128x"
            Case "256x" : Return "256x"
            Case "512x+" : Return "超高清"
            Case "audio" : Return "含声音"
            Case "fonts" : Return "含字体"
            Case "models" : Return "含模型"
            Case "gui" : Return "含 UI"
            Case "locale" : Return "含语言"
            Case "core-shaders" : Return "核心着色器"
            Case "modded" : Return "兼容 Mod"
            Case "fantasy" : Return "幻想风"
            Case "semi-realistic" : Return "半写实风"
            Case "cartoon" : Return "卡通风"
            Case "colored-lighting" : Return "彩色光照"
            Case "path-tracing" : Return "路径追踪"
            Case "pbr" : Return "PBR"
            Case "reflections" : Return "反射"
            Case "iris" : Return "Iris"
            Case "optifine" : Return "OptiFine"
            Case "vanilla" : Return "原版可用"
        End Select
        Return ""
    End Function

    Friend Function Str(el As JsonElement, key As String) As String
        Try
            Dim v As JsonElement
            If el.TryGetProperty(key, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        Catch
        End Try
        Return ""
    End Function

    Friend Function Num(el As JsonElement, key As String) As Long
        Try
            Dim v As JsonElement
            If el.TryGetProperty(key, v) AndAlso (v.ValueKind = JsonValueKind.Number) Then Return v.GetInt64()
        Catch
        End Try
        Return 0
    End Function

    Friend Function NumL(el As JsonElement, key As String) As Long
        Return Num(el, key)
    End Function

End Module

''' <summary>简单的同步字符串下载（Fabric meta 等）。</summary>
Public Class WebClient2
    Public Function DownloadString(url As String) As String
        Using resp = New HttpClient().GetAsync(url).GetAwaiter().GetResult()
            resp.EnsureSuccessStatusCode()
            Return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        End Using
    End Function
End Class
