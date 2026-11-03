Imports System.IO
Imports System.IO.Compression
Imports System.Formats.Tar

''' <summary>
''' Linux 版自更新：下载 tar.gz 包 → 解压到临时目录 → 用独立脚本等本进程退出后覆盖安装目录并重启。
''' 对应 Windows 版的 SwitchChannel / UpdateDownloadAndRestart（Windows 是 exe 替换，Linux 是目录覆盖）。
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
    ''' 下载并应用更新。返回 True 表示已排定替换重启（调用方应尽快退出进程）。
    ''' onStage：阶段文本；progress：0~1。
    ''' </summary>
    Public Function ApplyUpdate(packageUrl As String, progress As Action(Of Double, String)) As Boolean
        If Not ModPlatform.SupportsSelfUpdate Then
            Throw New Exception("macOS 版是签名的 .app 包，不支持就地替换更新，请到官网下载新版")
        End If
        If Not OperatingSystem.IsLinux() Then Throw New Exception("当前平台不支持自更新")
        Dim workDir = Path.Combine(ModPaths.TempDir, "Update-" & Guid.NewGuid().ToString("N").Substring(0, 8))
        Directory.CreateDirectory(workDir)
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
            Throw New Exception("macOS 版暂不支持在启动器内切换通道，请到官网下载对应通道的安装包")
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
