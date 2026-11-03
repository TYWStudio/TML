Imports System.IO
Imports System.IO.Compression
Imports System.Formats.Tar

''' <summary>
''' 自更新。两个平台的安装形态不一样，替换方式也不同：
'''   Linux：安装单元是整个目录（tar.gz 解压出来的），下载 tar.gz → 解压 → 覆盖目录 → 重启
'''   macOS：安装单元是 TYW Launcher.app（内含签名），下载 zip → 解压 → 整体换掉 .app →
'''          去隔离属性 → ad-hoc 重签名 → open 重启
''' 两者的共同点：都写一个脚本，等本进程退出后再动文件，否则正在运行的文件被占用。
''' 对应 Windows 版的 SwitchChannel / UpdateDownloadAndRestart。
''' </summary>
Public Module ModUpdate

    ''' <summary>安装目录（发布目录）。</summary>
    Public ReadOnly Property InstallDir As String
        Get
            Return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
        End Get
    End Property

    ''' <summary>当前可执行文件路径。</summary>
    Public ReadOnly Property ExePath As String
        Get
            Return If(Environment.ProcessPath, Path.Combine(InstallDir, "TYWLauncher"))
        End Get
    End Property

    ''' <summary>
    ''' macOS 上当前正在运行的 .app 路径；其他平台或不是从 .app 里启动时返回空串。
    ''' ExePath 形如 /Applications/TYW Launcher.app/Contents/MacOS/TYWLauncher，往上找到 .app 那一层。
    ''' </summary>
    Public ReadOnly Property MacBundlePath As String
        Get
            If Not ModPlatform.IsMac Then Return ""
            Dim cur = Path.GetDirectoryName(ExePath)
            For i = 1 To 4
                If String.IsNullOrEmpty(cur) Then Exit For
                If cur.EndsWith(".app", StringComparison.OrdinalIgnoreCase) Then Return cur
                cur = Path.GetDirectoryName(cur)
            Next
            Return ""
        End Get
    End Property

    ''' <summary>
    ''' 下载并应用更新。返回 True 表示已排定替换重启（调用方应尽快退出进程）。
    ''' onStage：阶段文本；progress：0~1。
    ''' </summary>
    Public Function ApplyUpdate(packageUrl As String, progress As Action(Of Double, String)) As Boolean
        If Not ModPlatform.SupportsSelfUpdate Then
            Throw New Exception("当前平台不支持自动更新，请到官网下载新版")
        End If
        Dim workDir = Path.Combine(ModPaths.TempDir, "Update-" & Guid.NewGuid().ToString("N").Substring(0, 8))
        Directory.CreateDirectory(workDir)
        If ModPlatform.IsMac Then Return ApplyUpdateMac(packageUrl, workDir, progress)
        Return ApplyUpdateLinux(packageUrl, workDir, progress)
    End Function

#Region "Linux：目录覆盖"

    Private Function ApplyUpdateLinux(packageUrl As String, workDir As String,
                                      progress As Action(Of Double, String)) As Boolean
        Dim tarPath = Path.Combine(workDir, "update.tar.gz")

        progress(0.05, "正在下载更新包……")
        If Not ModNet.DownloadFile(packageUrl, tarPath,
            Sub(p) progress(0.05 + 0.65 * p, "正在下载更新包… " & CInt(p * 100) & "%")) Then
            Throw New Exception("下载更新包失败")
        End If
        If Not File.Exists(tarPath) OrElse New FileInfo(tarPath).Length < 1024 * 1024 Then
            Throw New Exception("下载的更新包不完整")
        End If

        progress(0.75, "正在解压更新包……")
        Dim extractDir = Path.Combine(workDir, "files")
        Directory.CreateDirectory(extractDir)
        Using fs = File.OpenRead(tarPath)
            Using gz As New GZipStream(fs, CompressionMode.Decompress)
                TarFile.ExtractToDirectory(gz, extractDir, True)
            End Using
        End Using
        '包里如果套了一层目录，自动下钻
        Dim srcDir = extractDir
        For i = 1 To 2
            Dim subs = Directory.GetDirectories(srcDir)
            Dim files = Directory.GetFiles(srcDir)
            If files.Length = 0 AndAlso subs.Length = 1 Then srcDir = subs(0) Else Exit For
        Next
        If Not File.Exists(Path.Combine(srcDir, "TYWLauncher")) AndAlso
           Not File.Exists(Path.Combine(srcDir, "TYWLauncher.dll")) Then
            Throw New Exception("更新包里没有找到启动器文件")
        End If

        progress(0.9, "正在准备替换文件……")
        Dim script = Path.Combine(workDir, "apply.sh")
        Dim sb As New Text.StringBuilder()
        sb.AppendLine("#!/bin/sh")
        sb.AppendLine("PID=" & Environment.ProcessId.ToString())
        sb.AppendLine("SRC=" & Quote(srcDir))
        sb.AppendLine("DEST=" & Quote(InstallDir))
        sb.AppendLine("EXE=" & Quote(ExePath))
        sb.AppendLine("while kill -0 ""$PID"" 2>/dev/null; do sleep 0.2; done")
        sb.AppendLine("sleep 0.4")
        sb.AppendLine("cp -rf ""$SRC""/. ""$DEST""/ 2>/dev/null")
        sb.AppendLine("chmod +x ""$EXE"" 2>/dev/null")
        sb.AppendLine("cd ""$DEST"" && ""$EXE"" >/dev/null 2>&1 &")
        sb.AppendLine("sleep 1")
        sb.AppendLine("rm -rf " & Quote(workDir))
        File.WriteAllText(script, sb.ToString())
        Chmod(script)

        progress(1, "正在重启启动器……")
        'setsid：让替换脚本脱离当前会话，启动器退出后仍能继续执行
        Dim psi As New System.Diagnostics.ProcessStartInfo With {
            .FileName = "setsid",
            .Arguments = "/bin/sh " & Quote(script),
            .UseShellExecute = False,
            .CreateNoWindow = True
        }
        System.Diagnostics.Process.Start(psi)
        Return True
    End Function

#End Region

#Region "macOS：整个 .app 替换"

    Private Function ApplyUpdateMac(packageUrl As String, workDir As String,
                                    progress As Action(Of Double, String)) As Boolean
        Dim dest = MacBundlePath
        If dest = "" Then
            Throw New Exception("当前不是从 .app 包启动的，无法自动更新。请到官网下载新版")
        End If

        Dim zipPath = Path.Combine(workDir, "update.zip")
        progress(0.05, "正在下载更新包……")
        If Not ModNet.DownloadFile(packageUrl, zipPath,
            Sub(p) progress(0.05 + 0.65 * p, "正在下载更新包… " & CInt(p * 100) & "%")) Then
            Throw New Exception("下载更新包失败")
        End If
        If Not File.Exists(zipPath) OrElse New FileInfo(zipPath).Length < 1024 * 1024 Then
            Throw New Exception("下载的更新包不完整")
        End If

        progress(0.75, "正在解压更新包……")
        Dim extractDir = Path.Combine(workDir, "files")
        Directory.CreateDirectory(extractDir)
        Try
            ZipFile.ExtractToDirectory(zipPath, extractDir, True)
        Catch ex As Exception
            Throw New Exception("解压更新包失败：" & ex.Message)
        End Try

        Dim srcApp = FindBundle(extractDir)
        If srcApp = "" Then Throw New Exception("更新包里没有找到 .app")
        If Not File.Exists(Path.Combine(srcApp, "Contents", "MacOS", "TYWLauncher")) Then
            Throw New Exception("更新包里的 .app 不完整（缺少 Contents/MacOS/TYWLauncher）")
        End If

        progress(0.9, "正在准备替换文件……")
        Dim script = Path.Combine(workDir, "apply.sh")
        Dim sb As New Text.StringBuilder()
        sb.AppendLine("#!/bin/sh")
        sb.AppendLine("PID=" & Environment.ProcessId.ToString())
        sb.AppendLine("SRC=" & Quote(srcApp))
        sb.AppendLine("DEST=" & Quote(dest))
        sb.AppendLine("while kill -0 ""$PID"" 2>/dev/null; do sleep 0.2; done")
        sb.AppendLine("sleep 0.5")
        sb.AppendLine("NEW=""$DEST.new.$$""")
        sb.AppendLine("OLD=""$DEST.old.$$""")
        ' ditto 会保留资源分支、unix 权限与扩展属性，比 cp -R 更可靠
        sb.AppendLine("rm -rf ""$NEW""")
        sb.AppendLine("if ! ditto ""$SRC"" ""$NEW""; then rm -rf ""$NEW""; exit 1; fi")
        ' 先换到旁边再挪进来：避免在运行目录上原地覆盖产生半新半旧
        sb.AppendLine("rm -rf ""$OLD""")
        sb.AppendLine("mv ""$DEST"" ""$OLD"" 2>/dev/null")
        sb.AppendLine("mv ""$NEW"" ""$DEST"" || { mv ""$OLD"" ""$DEST"" 2>/dev/null; exit 1; }")
        sb.AppendLine("rm -rf ""$OLD""")
        ' 从网上下载的包会带 com.apple.quarantine，不去掉会被 Gatekeeper 拦；
        ' 换过二进制后原签名失效，必须重新做一次 ad-hoc 签名，否则 Apple Silicon 上直接拒绝启动
        sb.AppendLine("xattr -cr ""$DEST"" 2>/dev/null")
        sb.AppendLine("codesign --force --deep --sign - ""$DEST"" >/dev/null 2>&1")
        sb.AppendLine("open -n ""$DEST""")
        sb.AppendLine("sleep 1")
        sb.AppendLine("rm -rf " & Quote(workDir))
        File.WriteAllText(script, sb.ToString())
        Chmod(script)

        progress(1, "正在重启启动器……")
        ' macOS 没有 setsid，/bin/sh 起的子进程本来就是独立进程，父进程退出不会带走它
        Dim psi As New System.Diagnostics.ProcessStartInfo With {
            .FileName = "/bin/sh",
            .Arguments = Quote(script),
            .UseShellExecute = False,
            .CreateNoWindow = True
        }
        System.Diagnostics.Process.Start(psi)
        Return True
    End Function

    ''' <summary>在解压目录里找第一层或第二层的 .app。</summary>
    Private Function FindBundle(root As String, Optional depth As Integer = 0) As String
        Try
            For Each d In Directory.GetDirectories(root)
                If d.EndsWith(".app", StringComparison.OrdinalIgnoreCase) Then Return d
            Next
            If depth >= 2 Then Return ""
            For Each d In Directory.GetDirectories(root)
                Dim found = FindBundle(d, depth + 1)
                If found <> "" Then Return found
            Next
        Catch
        End Try
        Return ""
    End Function

#End Region

    Private Function Quote(s As String) As String
        Return """" & s.Replace("""", "\""") & """"
    End Function

    Private Sub Chmod(path As String)
        Try
            Dim psi As New System.Diagnostics.ProcessStartInfo With {
                .FileName = "chmod", .Arguments = "+x " & Quote(path), .UseShellExecute = False
            }
            Using p = System.Diagnostics.Process.Start(psi)
                p?.WaitForExit(3000)
            End Using
        Catch
        End Try
    End Sub

    ''' <summary>切换到指定通道：下载该通道的安装包并替换重启。返回 False 表示未切换。</summary>
    Public Function SwitchChannel(targetChannel As String, betaCode As String,
                                  progress As Action(Of Double, String)) As Boolean
        If Not ModPlatform.SupportsChannelSwitch Then
            Throw New Exception("当前平台暂不支持在启动器内切换通道，请到官网下载对应通道的安装包")
        End If
        Dim info = ModServer.GetChannelInfo(targetChannel)
        If String.IsNullOrWhiteSpace(info.File) Then
            Throw New Exception("服务端没有配置「" & ChannelDisplayOf(targetChannel) & "」的" & ModPlatform.DisplayName & "安装包")
        End If
        Dim url = info.File
        If Not url.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
            url = ModNet.ServerAddress.TrimEnd("/"c) & "/" & url.TrimStart("/"c)
        End If
        If targetChannel = "Beta" Then
            url &= If(url.Contains("?"), "&", "?") & "ident=" & Uri.EscapeDataString(ModSecret.DeviceCode) &
                   "&key=" & Uri.EscapeDataString(If(betaCode, ""))
        End If
        Return ApplyUpdate(url, progress)
    End Function

    Public Function ChannelDisplayOf(channel As String) As String
        Select Case channel
            Case "Snapshot" : Return "快照版"
            Case "Beta" : Return "内测版"
            Case Else : Return "正式版"
        End Select
    End Function

End Module
