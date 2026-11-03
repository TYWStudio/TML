Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions

''' <summary>一次启动所需的信息。</summary>
Public Class LaunchOptions
    Public Property VersionId As String = ""
    Public Property PlayerName As String = "Player"
    Public Property Uuid As String = ""
    Public Property AccessToken As String = "0"
    Public Property UserType As String = "legacy"
    Public Property JavaPath As String = ""
    Public Property MaxRamMb As Integer = 2048
    Public Property GameDir As String = ""
    Public Property ExtraArgs As New List(Of String)
    ''' <summary>窗口化时的宽高（0 = 用版本 JSON 里的默认值）。</summary>
    Public Property WindowWidth As Integer = 0
    Public Property WindowHeight As Integer = 0
    ''' <summary>全屏启动。</summary>
    Public Property Fullscreen As Boolean = False
    ''' <summary>额外 JVM 参数（空格分隔）。</summary>
    Public Property JvmExtra As String = ""
End Class

''' <summary>合并 inheritsFrom 之后的版本描述。</summary>
Public Class MergedVersion
    Public Property Id As String = ""
    Public Property MainClass As String = ""
    Public Property AssetsId As String = "legacy"
    Public Property VersionType As String = "release"
    Public Property JavaMajor As Integer = 0
    Public Property MinimumLauncherVersion As Integer = 0
    Public Property Libraries As New List(Of JsonElement)
    Public Property GameArgs As New List(Of JsonElement)
    Public Property JvmArgs As New List(Of JsonElement)
    Public Property LegacyArgs As String = Nothing
    Public Property RootId As String = ""
End Class

''' <summary>
''' 启动 Minecraft。对应 Windows 版 ModLaunch 的核心链路：
''' 合并版本 → 选 Java → 解压 natives → 拼 classpath → 拼参数 → 起进程。
''' </summary>
Public Module ModLaunch

    ''' <summary>启动过程的阶段回调（说明文字，进度 0~1）。</summary>
    Public Event StageChanged As Action(Of String, Double)
    ''' <summary>游戏进程已创建。</summary>
    Public Event ProcessStarted As Action(Of Process)

    Private ReadOnly LogDir As String = ModPaths.LogDir

    ''' <summary>同步启动。返回已启动的进程；失败抛异常。</summary>
    Public Function Launch(opt As LaunchOptions, Optional report As Action(Of String, Double) = Nothing) As Process
        If opt Is Nothing Then Throw New Exception("未选择版本")
        ModPaths.EnsureAll()

        Dim verDir = Path.Combine(ModPaths.McFolder, "versions", opt.VersionId)
        Dim jsonPath = Path.Combine(verDir, opt.VersionId & ".json")
        If Not File.Exists(jsonPath) Then Throw New Exception("找不到版本 " & opt.VersionId & " 的 JSON，请先下载该版本")

        report?.Invoke("读取版本信息", 0.02)
        Dim merged = LoadMerged(opt.VersionId)

        Dim gameDir = If(String.IsNullOrEmpty(opt.GameDir), ModPaths.McFolder, opt.GameDir)
        If Not Directory.Exists(gameDir) Then Directory.CreateDirectory(gameDir)

        ' --- Java ---
        report?.Invoke("获取 Java", 0.08)
        Dim javaPath = opt.JavaPath
        If String.IsNullOrEmpty(javaPath) Then
            Dim need = ModJava.RequiredFor(merged.JavaMajor, merged.RootId)
            If merged.JavaMajor > 0 Then need = merged.JavaMajor
            Dim j = ModJava.Best(need)
            If j Is Nothing Then
                Throw New Exception("未找到可用的 Java 运行时。请先安装 OpenJDK（需要 Java " & need & " 或更高版本）。")
            End If
            javaPath = j.Path
        End If
        If Not File.Exists(javaPath) Then Throw New Exception("Java 路径不存在：" & javaPath)

        ' --- natives ---
        report?.Invoke("解压运行库", 0.14)
        Dim nativesDir = Path.Combine(verDir, ModPlatform.NativesDirName)
        ExtractNatives(merged, nativesDir)

        ' --- classpath ---
        report?.Invoke("整理类路径", 0.22)
        Dim cpParts As New List(Of String)
        For Each libEl In merged.Libraries
            If Not RuleMatches(libEl) Then Continue For
            ' 只把 artifact 放进 classpath；带 natives 的库同样需要它的主 jar
            Dim rel = LibRelativePath(libEl)
            If rel = "" Then Continue For
            Dim full = Path.Combine(ModPaths.McFolder, "libraries", rel.Replace("/"c, Path.DirectorySeparatorChar))
            If File.Exists(full) Then cpParts.Add(full)
        Next
        ' 客户端 jar：根版本（最底层父版本）的那个
        Dim rootJar = Path.Combine(ModPaths.McFolder, "versions", merged.RootId, merged.RootId & ".jar")
        If File.Exists(rootJar) Then
            cpParts.Insert(0, rootJar)
        Else
            Dim ownJar = Path.Combine(verDir, opt.VersionId & ".jar")
            If File.Exists(ownJar) Then cpParts.Insert(0, ownJar)
        End If
        If cpParts.Count = 0 Then Throw New Exception("类路径为空，版本文件不完整，请重新下载该版本")
        Dim classpath = String.Join(Path.PathSeparator.ToString(), cpParts)

        Dim assetsRoot = Path.Combine(ModPaths.McFolder, "assets")
        If Not Directory.Exists(assetsRoot) Then Directory.CreateDirectory(assetsRoot)

        ' --- 参数 ---
        report?.Invoke("生成启动参数", 0.30)
        Dim args As New List(Of String)
        args.AddRange(BuildJvmArgs(merged, opt, nativesDir, classpath))
        If merged.MainClass = "" Then Throw New Exception("版本 JSON 缺少 MainClass，文件可能已损坏")
        args.Add(merged.MainClass)
        Dim gameArgs = BuildGameArgs(merged, opt, gameDir, assetsRoot)
        gameArgs = ApplyWindowArgs(gameArgs, opt)
        args.AddRange(gameArgs)
        args.AddRange(opt.ExtraArgs)

        ' --- 起进程 ---
        report?.Invoke("启动游戏", 0.36)
        Dim psi As New ProcessStartInfo(javaPath) With {
            .WorkingDirectory = gameDir,
            .UseShellExecute = False,
            .RedirectStandardOutput = False,
            .RedirectStandardError = False
        }
        For Each a In args
            psi.ArgumentList.Add(a)
        Next

        ' 让游戏在独立的进程组里，启动器退出后不被带走
        psi.Environment("TYW_LAUNCHER") = "1"

        Directory.CreateDirectory(LogDir)
        Dim logFile = Path.Combine(LogDir, "mc-" & opt.VersionId & ".log")
        Try
            File.WriteAllText(logFile,
                "# " & DateTime.Now.ToString("s") & vbCrLf &
                "# Java: " & javaPath & vbCrLf &
                "# 参数: " & String.Join(" ", args) & vbCrLf & vbCrLf)
        Catch
        End Try

        Dim p = Process.Start(psi)
        If p Is Nothing Then Throw New Exception("无法创建游戏进程")

        RaiseEvent StageChanged("已启动", 0.5)
        RaiseEvent ProcessStarted(p)
        Return p
    End Function

#Region "版本合并"

    ''' <summary>读版本 JSON 并沿 inheritsFrom 向上合并。</summary>
    Public Function LoadMerged(id As String) As MergedVersion
        Dim chain As New List(Of String)
        Dim cur = id
        Dim guard = 0
        While Not String.IsNullOrEmpty(cur) AndAlso guard < 10
            chain.Add(cur)
            Dim p = Path.Combine(ModPaths.McFolder, "versions", cur, cur & ".json")
            If Not File.Exists(p) Then Exit While
            Using doc = JsonDocument.Parse(File.ReadAllBytes(p))
                Dim inh As JsonElement
                If doc.RootElement.TryGetProperty("inheritsFrom", inh) AndAlso inh.ValueKind = JsonValueKind.String Then
                    cur = inh.GetString()
                Else
                    cur = Nothing
                End If
            End Using
            guard += 1
        End While
        If chain.Count = 0 Then Throw New Exception("版本不存在：" & id)

        Dim result As New MergedVersion With {.Id = id, .RootId = chain(chain.Count - 1)}
        Dim libSeen As New HashSet(Of String)(StringComparer.Ordinal)

        ' 从最底层父版本开始处理，子版本覆盖父版本
        For i = chain.Count - 1 To 0 Step -1
            Dim vid = chain(i)
            Dim p = Path.Combine(ModPaths.McFolder, "versions", vid, vid & ".json")
            If Not File.Exists(p) Then Continue For
            Using doc = JsonDocument.Parse(File.ReadAllBytes(p))
                Dim root = doc.RootElement

                Dim s As JsonElement
                If root.TryGetProperty("mainClass", s) AndAlso s.ValueKind = JsonValueKind.String Then
                    If Not String.IsNullOrEmpty(s.GetString()) Then result.MainClass = s.GetString()
                End If
                Dim ai As JsonElement
                If root.TryGetProperty("assetIndex", ai) Then
                    Dim aid = GetStr(ai, "id")
                    If Not String.IsNullOrEmpty(aid) Then result.AssetsId = aid
                End If
                If root.TryGetProperty("assets", s) AndAlso s.ValueKind = JsonValueKind.String Then
                    result.AssetsId = s.GetString()
                End If
                If root.TryGetProperty("type", s) AndAlso s.ValueKind = JsonValueKind.String Then
                    result.VersionType = s.GetString()
                End If
                Dim jv As JsonElement
                If root.TryGetProperty("javaVersion", jv) Then
                    Dim mj As JsonElement
                    If jv.TryGetProperty("majorVersion", mj) AndAlso mj.ValueKind = JsonValueKind.Number Then
                        result.JavaMajor = mj.GetInt32()
                    End If
                End If
                Dim mlv As JsonElement
                If root.TryGetProperty("minimumLauncherVersion", mlv) AndAlso mlv.ValueKind = JsonValueKind.Number Then
                    result.MinimumLauncherVersion = mlv.GetInt32()
                End If

                Dim libs As JsonElement
                If root.TryGetProperty("libraries", libs) AndAlso libs.ValueKind = JsonValueKind.Array Then
                    For Each libEl In libs.EnumerateArray()
                        Dim key = GetStr(libEl, "name")
                        Dim nk As JsonElement
                        If libEl.TryGetProperty("natives", nk) Then key &= "#native"
                        If libSeen.Add(key) Then result.Libraries.Add(libEl.Clone())
                    Next
                End If

                Dim margs As JsonElement
                If root.TryGetProperty("minecraftArguments", margs) AndAlso margs.ValueKind = JsonValueKind.String Then
                    result.LegacyArgs = margs.GetString()
                End If

                Dim arguments As JsonElement
                If root.TryGetProperty("arguments", arguments) Then
                    Dim ga As JsonElement
                    If arguments.TryGetProperty("game", ga) AndAlso ga.ValueKind = JsonValueKind.Array Then
                        For Each a In ga.EnumerateArray()
                            result.GameArgs.Add(a.Clone())
                        Next
                    End If
                    Dim ja As JsonElement
                    If arguments.TryGetProperty("jvm", ja) AndAlso ja.ValueKind = JsonValueKind.Array Then
                        For Each a In ja.EnumerateArray()
                            result.JvmArgs.Add(a.Clone())
                        Next
                    End If
                End If
            End Using
        Next
        Return result
    End Function

    Private Function GetStr(el As JsonElement, name As String) As String
        Dim v As JsonElement
        If el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        Return ""
    End Function

#End Region

#Region "natives"

    ''' <summary>这个库带的是当前平台+当前架构的 native（返回 classifier key，否则空串）。</summary>
    Private Function NativeKeyOf(libEl As JsonElement) As String
        Dim dls As JsonElement
        If libEl.TryGetProperty("downloads", dls) Then
            Dim cls As JsonElement
            If dls.TryGetProperty("classifiers", cls) Then
                Dim keys As New List(Of String)
                For Each c In cls.EnumerateObject()
                    keys.Add(c.Name)
                Next
                Dim best = ModPlatform.BestClassifier(keys)
                If best <> "" Then Return best
            End If
        End If
        Dim nk As JsonElement
        If libEl.TryGetProperty("natives", nk) Then
            'natives 段是 {"linux":"natives-linux","osx":"natives-macos"} 这种，按平台取
            Dim platformKey = If(ModPlatform.IsMac, "osx", "linux")
            Dim v As JsonElement
            If nk.TryGetProperty(platformKey, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        End If
        Return ""
    End Function

    ''' <summary>库在 libraries 下的相对路径。</summary>
    Private Function LibRelativePath(libEl As JsonElement) As String
        Dim dls As JsonElement
        If libEl.TryGetProperty("downloads", dls) Then
            Dim art As JsonElement
            If dls.TryGetProperty("artifact", art) Then
                Dim p = GetStr(art, "path")
                If p <> "" Then Return p
            End If
        End If
        Return ModDownload.MavenToPath(GetStr(libEl, "name"))
    End Function

    Private Function NativeJarPath(libEl As JsonElement) As String
        Dim dls As JsonElement
        If libEl.TryGetProperty("downloads", dls) Then
            Dim cls As JsonElement
            If dls.TryGetProperty("classifiers", cls) Then
                Dim keys As New List(Of String)
                For Each c In cls.EnumerateObject()
                    keys.Add(c.Name)
                Next
                Dim best = ModPlatform.BestClassifier(keys)
                If best <> "" Then
                    Dim chosen As JsonElement
                    If cls.TryGetProperty(best, chosen) Then
                        Dim p = GetStr(chosen, "path")
                        If p = "" Then p = ModDownload.MavenToPath(GetStr(libEl, "name") & ":" & best)
                        Return Path.Combine(ModPaths.McFolder, "libraries", p.Replace("/"c, Path.DirectorySeparatorChar))
                    End If
                End If
            End If
        End If
        Return ""
    End Function

    Private Sub ExtractNatives(merged As MergedVersion, nativesDir As String)
        Try
            If Directory.Exists(nativesDir) Then
                For Each f In Directory.GetFiles(nativesDir)
                    Try : File.Delete(f) : Catch : End Try
                Next
            Else
                Directory.CreateDirectory(nativesDir)
            End If
        Catch
            Directory.CreateDirectory(nativesDir)
        End Try

        For Each libEl In merged.Libraries
            If Not RuleMatches(libEl) Then Continue For
            Dim jar = NativeJarPath(libEl)
            If String.IsNullOrEmpty(jar) OrElse Not File.Exists(jar) Then Continue For
            Try
                Using z As ZipArchive = ZipFile.OpenRead(jar)
                    For Each entry In z.Entries
                        If entry.FullName.EndsWith("/"c) Then Continue For
                        If entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) Then Continue For
                        ' 排除其它平台的动态库：Windows 的 .dll 一律不要；
                        ' Linux 只要 .so，macOS 只要 .dylib
                        Dim ext = Path.GetExtension(entry.FullName).ToLowerInvariant()
                        If ext = ".dll" Then Continue For
                        If ModPlatform.IsMac Then
                            If ext <> ".dylib" AndAlso ext <> ".jnilib" Then Continue For
                        Else
                            If ext <> ".so" Then Continue For
                        End If
                        Dim nameOnly = Path.GetFileName(entry.FullName)
                        If nameOnly = "" Then Continue For
                        Dim target = Path.Combine(nativesDir, nameOnly)
                        Try
                            entry.ExtractToFile(target, True)
                        Catch
                        End Try
                    Next
                End Using
            Catch ex As Exception
                ModLog.Err(ex, "解压 natives：" & jar)
            End Try
        Next
    End Sub

#End Region

#Region "参数"

    Private Function BuildJvmArgs(merged As MergedVersion, opt As LaunchOptions, nativesDir As String, classpath As String) As List(Of String)
        Dim list As New List(Of String)
        Dim ram = If(opt.MaxRamMb > 0, opt.MaxRamMb, 2048)
        list.Add("-Xmx" & ram & "M")
        list.Add("-Xms512M")
        ' macOS：1.19+ 的 GLFW 必须在主线程初始化，这一项要排在最前面
        If ModPlatform.NeedsStartOnFirstThread(If(merged.RootId, opt.VersionId)) Then
            list.Add("-XstartOnFirstThread")
        End If
        list.Add("-Dfile.encoding=UTF-8")
        list.Add("-Dstdout.encoding=UTF-8")
        list.Add("-Dstderr.encoding=UTF-8")
        ' Wayland/HiDPI 下 LWJGL 的显示问题在部分机器上会白屏（仅 Linux 需要）
        If ModPlatform.IsLinux Then list.Add("-Dorg.lwjgl.system.allocator=system")

        Dim tokens = TokenTable(merged, opt, nativesDir, classpath, If(opt.GameDir, ModPaths.McFolder))
        Dim hasLibraryPath = False

        If merged.JvmArgs.Count > 0 Then
            Dim expanded = ExpandArgs(merged.JvmArgs, merged)
            Dim i = 0
            While i < expanded.Count
                Dim s = ApplyTokens(expanded(i), tokens)
                If s.StartsWith("-Djava.library.path=") Then hasLibraryPath = True

                If s = "-cp" OrElse s = "-classpath" Then
                    list.Add(s)
                    ' 紧跟其后的通常是 ${classpath} 占位参数，替换后要一起跳掉，
                    ' 否则它会变成"主类名"传给 Java
                    If i + 1 < expanded.Count AndAlso expanded(i + 1).Contains("${classpath}") Then
                        list.Add(classpath)
                        i += 2
                        Continue While
                    End If
                    list.Add(classpath)
                    i += 1
                    Continue While
                End If

                list.Add(s)
                i += 1
            End While
        End If

        If Not hasLibraryPath Then
            list.Add("-Djava.library.path=" & nativesDir)
        End If
        If Not list.Contains("-cp") Then
            list.Add("-cp")
            list.Add(classpath)
        End If

        ' 用户自定义 JVM 参数（设置 → 启动 → 高级）
        If Not String.IsNullOrWhiteSpace(opt.JvmExtra) Then
            For Each a In Regex.Split(opt.JvmExtra.Trim(), "\s+")
                If a <> "" Then list.Add(a)
            Next
        End If
        Return list
    End Function

    ''' <summary>
    ''' 按设置改写窗口参数：全屏优先；否则用自定义宽高覆盖版本 JSON 里的 --width/--height。
    ''' 对应 Windows 版 PageSetupLaunch 的「窗口大小」设置。
    ''' </summary>
    Private Function ApplyWindowArgs(args As List(Of String), opt As LaunchOptions) As List(Of String)
        Dim hasFullscreen = args.Contains("--fullscreen")
        If opt.Fullscreen Then
            If Not hasFullscreen Then args.Add("--fullscreen")
            Return args
        End If
        If hasFullscreen Then args.Remove("--fullscreen")
        If opt.WindowWidth <= 0 OrElse opt.WindowHeight <= 0 Then Return args

        Dim out As New List(Of String)
        Dim i = 0
        While i < args.Count
            Dim a = args(i)
            If (a = "--width" OrElse a = "--height") AndAlso i + 1 < args.Count Then
                i += 2
                Continue While
            End If
            If a.StartsWith("--width=") OrElse a.StartsWith("--height=") Then
                i += 1
                Continue While
            End If
            out.Add(a)
            i += 1
        End While
        out.Add("--width")
        out.Add(opt.WindowWidth.ToString())
        out.Add("--height")
        out.Add(opt.WindowHeight.ToString())
        Return out
    End Function

    Private Function BuildGameArgs(merged As MergedVersion, opt As LaunchOptions, gameDir As String, assetsRoot As String) As List(Of String)
        Dim tokens = TokenTable(merged, opt, "", "", gameDir)
        If merged.GameArgs.Count > 0 Then
            Dim list As New List(Of String)
            For Each a In ExpandArgs(merged.GameArgs, merged)
                list.Add(ApplyTokens(a, tokens))
            Next
            Return list
        End If
        If Not String.IsNullOrEmpty(merged.LegacyArgs) Then
            Dim list As New List(Of String)
            For Each a In Regex.Split(merged.LegacyArgs.Trim(), "\s+")
                If a = "" Then Continue For
                list.Add(ApplyTokens(a, tokens))
            Next
            Return list
        End If
        Return New List(Of String)
    End Function

    ''' <summary>把 arguments 数组里的字符串、以及带 rules 的对象展开成字符串。</summary>
    Private Function ExpandArgs(items As List(Of JsonElement), merged As MergedVersion) As List(Of String)
        Dim list As New List(Of String)
        For Each el In items
            If el.ValueKind = JsonValueKind.String Then
                list.Add(el.GetString())
            ElseIf el.ValueKind = JsonValueKind.Object Then
                ' 这里的 rules 不只看系统，还要看 features：
                ' 例如 --demo 只在 is_demo_user 为真时才有，误加会让游戏变成试玩模式
                If Not RuleMatches(el) Then Continue For
                Dim val As JsonElement
                If el.TryGetProperty("value", val) Then
                    If val.ValueKind = JsonValueKind.String Then
                        list.Add(val.GetString())
                    ElseIf val.ValueKind = JsonValueKind.Array Then
                        For Each v In val.EnumerateArray()
                            If v.ValueKind = JsonValueKind.String Then list.Add(v.GetString())
                        Next
                    End If
                End If
            End If
        Next
        Return list
    End Function

    ''' <summary>判断带 rules 的参数/库在当前平台和当前启动方式下是否生效。</summary>
    Public Function RuleMatches(el As JsonElement) As Boolean
        Dim rules As JsonElement
        If Not el.TryGetProperty("rules", rules) OrElse rules.ValueKind <> JsonValueKind.Array Then Return True
        Dim allowed = False
        For Each rule In rules.EnumerateArray()
            If Not SingleRuleMatches(rule) Then Continue For
            Dim action = GetStr(rule, "action")
            allowed = (action = "allow")
        Next
        Return allowed
    End Function

    Private Function SingleRuleMatches(rule As JsonElement) As Boolean
        Dim osEl As JsonElement
        If rule.TryGetProperty("os", osEl) Then
            ' 版本 JSON 里 macOS 写作 osx；Linux 写作 linux
            Dim name = GetStr(osEl, "name")
            Dim expectName = If(ModPlatform.IsMac, "osx", "linux")
            If name <> "" AndAlso name <> expectName Then Return False

            Dim arch = GetStr(osEl, "arch")
            Select Case arch
                Case "x86" : If ModPlatform.ArchName <> "x86" Then Return False
                Case "x86_64" : If ModPlatform.ArchName <> "x64" Then Return False
                Case "arm64", "aarch64" : If Not ModPlatform.IsArm64 Then Return False
            End Select
        End If

        ' features：我们不是试玩用户、不用自定义分辨率、不用快速进入，
        ' 所以任何要求 feature 为真的规则都不匹配
        Dim feats As JsonElement
        If rule.TryGetProperty("features", feats) AndAlso feats.ValueKind = JsonValueKind.Object Then
            For Each f In feats.EnumerateObject()
                If f.Value.ValueKind = JsonValueKind.True Then Return False
            Next
        End If

        Return True
    End Function

    Private Function TokenTable(merged As MergedVersion, opt As LaunchOptions, nativesDir As String, classpath As String, gameDir As String) As Dictionary(Of String, String)
        Dim d As New Dictionary(Of String, String)(StringComparer.Ordinal)
        d("${auth_player_name}") = opt.PlayerName
        d("${auth_uuid}") = opt.Uuid
        d("${auth_access_token}") = opt.AccessToken
        d("${auth_session}") = "token:" & opt.AccessToken & ":" & opt.Uuid
        d("${user_type}") = opt.UserType
        d("${user_properties}") = "{}"
        d("${version_name}") = merged.Id
        d("${version_type}") = merged.VersionType
        d("${game_directory}") = gameDir
        d("${assets_root}") = Path.Combine(ModPaths.McFolder, "assets")
        d("${game_assets}") = Path.Combine(ModPaths.McFolder, "assets", "virtual", "legacy")
        d("${assets_index_name}") = merged.AssetsId
        d("${natives_directory}") = nativesDir
        d("${classpath}") = classpath
        d("${classpath_separator}") = ":"
        d("${launcher_name}") = "TYWLauncher"
        d("${launcher_version}") = "1.0"
        d("${library_directory}") = Path.Combine(ModPaths.McFolder, "libraries")
        ' 1.20+ 新增的占位符：即使不用也必须替换，否则参数会错位
        d("${clientid}") = ""
        d("${auth_xuid}") = ""
        d("${resolution_width}") = "854"
        d("${resolution_height}") = "480"
        d("${quickPlayPath}") = ""
        d("${quickPlaySingleplayer}") = ""
        d("${quickPlayMultiplayer}") = ""
        d("${quickPlayRealms}") = ""
        Return d
    End Function

    Private Function ApplyTokens(s As String, tokens As Dictionary(Of String, String)) As String
        If String.IsNullOrEmpty(s) OrElse Not s.Contains("${") Then Return s
        For Each kv In tokens
            If s.Contains(kv.Key) Then s = s.Replace(kv.Key, kv.Value)
        Next
        Return s
    End Function

#End Region

End Module
