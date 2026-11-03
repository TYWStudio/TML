Imports System.IO
Imports System.Text.Json

''' <summary>可安装的加载器种类（对应 Windows 版 ModLoaders 中的客户端加载器）。</summary>
Public Enum LoaderKind
    Fabric
    Quilt
    Forge
    NeoForge
End Enum

''' <summary>一个加载器版本条目。</summary>
Public Class LoaderEntry
    Public Property Version As String = ""
    Public Property Desc As String = ""
End Class

''' <summary>
''' 加载器安装（对应 Windows 版 PageDownloadInstall 的 Fabric/Forge/NeoForge 卡片 + Fabric API 自动下载）。
''' Fabric / Quilt 用官方 meta 的 profile JSON（自带 inheritsFrom 与库列表）；
''' Forge / NeoForge 下载官方安装器并 --installClient。
''' </summary>
Public Module ModLoader

#Region "加载器版本列表"

    ''' <summary>Fabric 加载器版本（meta.fabricmc.net，稳定版在前）。</summary>
    Public Function ListFabric(gameVer As String) As List(Of LoaderEntry)
        Dim raw = ModDownload.GetBytes("https://meta.fabricmc.net/v2/versions/loader/" & gameVer, 1)
        Using doc = JsonDocument.Parse(raw)
            Dim result As New List(Of LoaderEntry)
            For Each el In doc.RootElement.EnumerateArray()
                Dim ldr As JsonElement
                If Not el.TryGetProperty("loader", ldr) Then Continue For
                Dim ver = Str(ldr, "version")
                If ver = "" Then Continue For
                Dim stable As JsonElement
                Dim isStable = If(ldr.TryGetProperty("stable", stable), stable.GetBoolean(), True)
                result.Add(New LoaderEntry With {.Version = ver, .Desc = If(isStable, "稳定版", "测试版")})
            Next
            ' meta 已按新到旧排序；把稳定版整体排在测试版前
            Dim stableList = result.Where(Function(e) e.Desc = "稳定版").ToList()
            stableList.AddRange(result.Where(Function(e) e.Desc <> "稳定版"))
            Return stableList
        End Using
    End Function

    ''' <summary>Quilt 加载器版本（meta.quiltmc.org）。</summary>
    Public Function ListQuilt(gameVer As String) As List(Of LoaderEntry)
        Dim raw = ModDownload.GetBytes("https://meta.quiltmc.org/v3/versions/loader/" & gameVer, 1)
        Using doc = JsonDocument.Parse(raw)
            Dim result As New List(Of LoaderEntry)
            For Each el In doc.RootElement.EnumerateArray()
                Dim ldr As JsonElement
                If Not el.TryGetProperty("loader", ldr) Then Continue For
                Dim ver = Str(ldr, "version")
                If ver = "" Then Continue For
                result.Add(New LoaderEntry With {.Version = ver, .Desc = "官方构建"})
            Next
            Return result
        End Using
    End Function

    ''' <summary>Forge 版本：BMCLAPI 镜像列表，失败回落官方 promotions。</summary>
    Public Function ListForge(gameVer As String) As List(Of LoaderEntry)
        Try
            Dim raw = ModDownload.GetBytes(ModDownload.Mirror & "/forge/minecraft/" & gameVer, 1)
            Using doc = JsonDocument.Parse(raw)
                Dim result As New List(Of LoaderEntry)
                For Each el In doc.RootElement.EnumerateArray()
                    Dim ver = Str(el, "version")
                    If ver = "" Then Continue For
                    Dim branch = Str(el, "branch")
                    result.Add(New LoaderEntry With {.Version = ver, .Desc = If(branch = "", "官方构建", branch)})
                Next
                If result.Count > 0 Then Return result
            End Using
        Catch ex As Exception
            ModLog.Err(ex, "Forge 镜像列表 " & gameVer)
        End Try
        ' 回落：官方 promotions（只有推荐/最新）
        Dim raw2 = ModDownload.GetBytes("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", 1)
        Using doc = JsonDocument.Parse(raw2)
            Dim result As New List(Of LoaderEntry)
            Dim promos As JsonElement
            If doc.RootElement.TryGetProperty("promos", promos) Then
                Dim rec As JsonElement, lat As JsonElement
                If promos.TryGetProperty(gameVer & "-recommended", rec) Then
                    result.Add(New LoaderEntry With {.Version = rec.GetString(), .Desc = "官方推荐"})
                End If
                If promos.TryGetProperty(gameVer & "-latest", lat) Then
                    Dim v = lat.GetString()
                    If result.All(Function(e) e.Version <> v) Then
                        result.Add(New LoaderEntry With {.Version = v, .Desc = "最新构建"})
                    End If
                End If
            End If
            If result.Count = 0 Then Throw New Exception("暂无 Forge " & gameVer & " 的可用版本")
            Return result
        End Using
    End Function

    ''' <summary>NeoForge 版本（maven.neoforged.net，versions API 失败回落 maven-metadata.xml）。</summary>
    Public Function ListNeoForge(gameVer As String) As List(Of LoaderEntry)
        ' NeoForge 版本号去掉 MC 的 "1." 前缀：1.21.1 → 21.1.x
        Dim prefix = gameVer
        If prefix.StartsWith("1.") Then prefix = prefix.Substring(2)
        If Not prefix.Contains("."c) Then prefix &= ".0"

        Dim all As New List(Of String)
        Try
            Dim raw = ModDownload.GetBytes("https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", 1)
            Using doc = JsonDocument.Parse(raw)
                Dim versions As JsonElement
                If doc.RootElement.TryGetProperty("versions", versions) Then
                    For Each el In versions.EnumerateArray()
                        Dim v = el.GetString()
                        If v IsNot Nothing AndAlso v.StartsWith(prefix & ".") Then all.Add(v)
                    Next
                End If
            End Using
        Catch ex As Exception
            ModLog.Err(ex, "NeoForge versions API，改用 maven-metadata")
        End Try

        If all.Count = 0 Then
            Dim xml = ModDownload.GetBytes("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", 1)
            Dim xmlText = Text.Encoding.UTF8.GetString(xml)
            For Each m In Text.RegularExpressions.Regex.Matches(xmlText, "<version>(" & Text.RegularExpressions.Regex.Escape(prefix) & "\.[^<]+)</version>")
                all.Add(CType(m, Text.RegularExpressions.Match).Groups(1).Value)
            Next
        End If

        If all.Count = 0 Then Throw New Exception("NeoForge 暂不支持 Minecraft " & gameVer)
        ' 排除 beta / rc；一个都没有就保留全部
        Dim rel = all.Where(Function(v) Not v.Contains("-beta") AndAlso Not v.Contains("-rc")).ToList()
        If rel.Count = 0 Then rel = all
        rel.Reverse() ' 新到旧
        Dim result As New List(Of LoaderEntry)
        For Each v In rel
            result.Add(New LoaderEntry With {.Version = v, .Desc = If(v.Contains("-"), "预览版", "官方构建")})
        Next
        Return result
    End Function

#End Region

#Region "安装"

    ''' <summary>
    ''' 安装 Fabric / Quilt：确保原版就位 → 拉 profile JSON（inheritsFrom 指向原版）→ 下载加载器库文件。
    ''' </summary>
    Public Sub InstallMetaLoader(kind As LoaderKind, gameVer As String, loaderVer As String,
                                 id As String, report As Action(Of Double, String), cancel As Func(Of Boolean))
        Dim name = If(kind = LoaderKind.Fabric, "Fabric", "Quilt")
        EnsureVanilla(gameVer, Sub(p, s) report(0.02 + 0.3 * p, s), cancel)

        report(0.35, "获取 " & name & " 配置（loader " & loaderVer & "）")
        Dim url = If(kind = LoaderKind.Fabric,
                     "https://meta.fabricmc.net/v2/versions/loader/" & gameVer & "/" & loaderVer & "/profile/json",
                     "https://meta.quiltmc.org/v3/versions/loader/" & gameVer & "/" & loaderVer & "/profile/json")
        Dim raw = ModDownload.GetBytes(url, 2)

        ' 改写 id 为用户自定义实例名后落盘
        Dim node = Json.Nodes.JsonNode.Parse(raw)
        node("id") = Json.Nodes.JsonValue.Create(id)
        Dim verDir = Path.Combine(ModPaths.McFolder, "versions", id)
        Directory.CreateDirectory(verDir)
        File.WriteAllText(Path.Combine(verDir, id & ".json"), node.ToJsonString())

        ' 下载加载器库文件（profile 里 libraries[].name + url 基址）
        Dim libs As JsonElement
        Using doc = JsonDocument.Parse(node.ToJsonString())
            If doc.RootElement.TryGetProperty("libraries", libs) Then
                Dim targets As New List(Of Tuple(Of String, String))
                For Each libEl In libs.EnumerateArray()
                    Dim libName = Str(libEl, "name")
                    Dim baseU = Str(libEl, "url")
                    If libName = "" Then Continue For
                    If baseU = "" Then baseU = "https://maven.fabricmc.net/"
                    Dim rel = ModDownload.MavenToPath(libName)
                    If rel = "" Then Continue For
                    Dim full = (baseU.TrimEnd("/"c) & "/" & rel)
                    targets.Add(Tuple.Create(full, Path.Combine(ModPaths.McFolder, "libraries", rel.Replace("/"c, Path.DirectorySeparatorChar))))
                Next
                Dim total = Math.Max(targets.Count, 1)
                For i = 0 To targets.Count - 1
                    If cancel() Then Throw New OperationCanceledException
                    report(0.4 + 0.55 * i / total, "下载 " & name & " 运行库 " & (i + 1) & "/" & total)
                    Dim t = targets(i)
                    If Not File.Exists(t.Item2) OrElse New FileInfo(t.Item2).Length = 0 Then
                        Directory.CreateDirectory(Path.GetDirectoryName(t.Item2))
                        ModDownload.DownloadFile(t.Item1, t.Item2)
                    End If
                Next
            End If
        End Using

        report(1, name & " " & loaderVer & " 安装完成")
    End Sub

    ''' <summary>
    ''' 安装 Forge / NeoForge：确保原版就位 → 下载官方安装器 → java --installClient → 改名到自定义实例名。
    ''' </summary>
    Public Sub InstallForge(kind As LoaderKind, gameVer As String, loaderVer As String,
                            id As String, report As Action(Of Double, String), cancel As Func(Of Boolean))
        Dim isForge = kind = LoaderKind.Forge
        Dim name = If(isForge, "Forge", "NeoForge")
        EnsureVanilla(gameVer, Sub(p, s) report(0.02 + 0.2 * p, s), cancel)

        ' --- 下载安装器 ---
        Dim gamePair = If(isForge, gameVer & "-" & loaderVer, loaderVer)
        Dim jarName As String
        If isForge Then
            jarName = "forge-" & gamePair & "-installer.jar"
        Else
            jarName = "neoforge-" & gamePair & "-installer.jar"
        End If
        Dim mirrorUrl = If(isForge,
                           ModDownload.Mirror & "/maven/net/minecraftforge/forge/" & gamePair & "/" & jarName,
                           ModDownload.Mirror & "/maven/net/neoforged/neoforge/" & gamePair & "/" & jarName)
        Dim officialUrl = If(isForge,
                             "https://maven.minecraftforge.net/net/minecraftforge/forge/" & gamePair & "/" & jarName,
                             "https://maven.neoforged.net/releases/net/neoforged/neoforge/" & gamePair & "/" & jarName)
        ModPaths.EnsureAll()
        Directory.CreateDirectory(ModPaths.TempDir)
        Dim instPath = Path.Combine(ModPaths.TempDir, jarName)
        report(0.25, "下载 " & name & " 安装器")
        Try
            If File.Exists(instPath) Then File.Delete(instPath)
            ModDownload.DownloadFile(ModDownload.Rewrite(mirrorUrl), instPath)
        Catch ex As Exception
            ModLog.Err(ex, name & " 安装器镜像下载失败，改走官方源")
            ModDownload.DownloadFile(officialUrl, instPath)
        End Try
        If cancel() Then Throw New OperationCanceledException

        ' --- 选 Java（安装器 8 即可跑；优先 8，其次任意可用） ---
        Dim java = ModJava.Best(8)
        If java Is Nothing Then java = ModJava.Best(0)
        If java Is Nothing Then Throw New Exception("未找到可用的 Java 运行时，无法运行 " & name & " 安装器")

        ' --- 运行安装器 ---
        report(0.45, "运行 " & name & " 安装器（需要联网下载依赖，请耐心等待）")
        Dim psi As New ProcessStartInfo(java.Path) With {
            .Arguments = "-jar """ & instPath & """ --installClient """ & ModPaths.McFolder & """",
            .UseShellExecute = False,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .CreateNoWindow = True
        }
        Using proc = Process.Start(psi)
            If proc Is Nothing Then Throw New Exception("无法启动 Java 进程")
            AddHandler proc.OutputDataReceived, Sub(s, e) If e.Data IsNot Nothing Then ModLog.Info("[installer] " & e.Data)
            AddHandler proc.ErrorDataReceived, Sub(s, e) If e.Data IsNot Nothing Then ModLog.Info("[installer] " & e.Data)
            proc.BeginOutputReadLine()
            proc.BeginErrorReadLine()
            While Not proc.HasExited
                If cancel() Then
                    Try
                        proc.Kill(True)
                    Catch
                    End Try
                    Throw New OperationCanceledException
                End If
                System.Threading.Thread.Sleep(300)
            End While
            If proc.ExitCode <> 0 Then Throw New Exception(name & " 安装器退出码 " & proc.ExitCode & "，详见日志")
        End Using

        ' --- 改名到自定义实例名 ---
        report(0.92, "整理版本文件")
        Dim defId As String = If(isForge, gameVer & "-forge", gameVer & "-neoforge")
        Dim srcDir = Path.Combine(ModPaths.McFolder, "versions", defId)
        If Not Directory.Exists(srcDir) Then
            ' 兜底：搜索 {game}-forge* / {game}-neoforge* 目录
            Dim guess = Path.Combine(ModPaths.McFolder, "versions")
            For Each d In Directory.GetDirectories(guess, If(isForge, gameVer & "-forge*", gameVer & "-neoforge*"))
                srcDir = d
                Exit For
            Next
        End If
        If Not Directory.Exists(srcDir) Then Throw New Exception(name & " 安装器没有生成版本目录，安装失败")
        If id <> defId Then
            Dim oldJson = Path.Combine(srcDir, Path.GetFileName(srcDir) & ".json")
            If File.Exists(oldJson) Then
                Dim n = Json.Nodes.JsonNode.Parse(File.ReadAllBytes(oldJson))
                n("id") = Json.Nodes.JsonValue.Create(id)
                File.WriteAllText(Path.Combine(srcDir, id & ".json"), n.ToJsonString())
            End If
            Dim dstDir = Path.Combine(ModPaths.McFolder, "versions", id)
            If Directory.Exists(dstDir) Then Throw New Exception("版本目录 " & id & " 已存在")
            Directory.Move(srcDir, dstDir)
        End If

        Try
            If File.Exists(instPath) Then File.Delete(instPath)
        Catch
        End Try
        report(1, name & " " & loaderVer & " 安装完成")
    End Sub

    ''' <summary>下载 Fabric API 的最新兼容版到实例 mods 目录（对应 Windows 版 CardFabricApi）。</summary>
    Public Sub DownloadFabricApi(gameVer As String, destDir As String,
                                 report As Action(Of Double, String), cancel As Func(Of Boolean))
        report(0.05, "查询 Fabric API 版本")
        Dim files = ModResource.FilesOf("fabric-api", ResPlatform.Modrinth, ResType.Mod)
        Dim pick = files.FirstOrDefault(Function(f) f.GameVersions.Contains(gameVer) AndAlso
                                            f.ModLoaders.Contains("fabric") AndAlso f.ReleaseType = 1)
        If pick Is Nothing Then
            pick = files.FirstOrDefault(Function(f) f.GameVersions.Contains(gameVer) AndAlso f.ReleaseType = 1)
        End If
        If pick Is Nothing Then pick = files.FirstOrDefault()
        If pick Is Nothing Then Throw New Exception("没有找到兼容 Minecraft " & gameVer & " 的 Fabric API")
        Directory.CreateDirectory(destDir)
        ModResource.DownloadResFile(pick, destDir, Sub(p, s) report(p, s), cancel, "Fabric API ")
        report(1, "Fabric API 已下载到 mods 目录")
    End Sub

    ''' <summary>确保原版版本（JSON）已安装；没有则先装。</summary>
    Private Sub EnsureVanilla(gameVer As String, report As Action(Of Double, String), cancel As Func(Of Boolean))
        If File.Exists(Path.Combine(ModPaths.McFolder, "versions", gameVer, gameVer & ".json")) Then Return
        Dim m = ModDownload.Manifest().FirstOrDefault(Function(v) v.Id = gameVer)
        If m Is Nothing Then Throw New Exception("找不到原版版本 " & gameVer)
        ModDownload.Install(gameVer, m.Url, report, cancel)
    End Sub

    Private Function Str(el As JsonElement, name As String) As String
        Dim v As JsonElement
        Return If(el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String, v.GetString(), "")
    End Function

#End Region

End Module
