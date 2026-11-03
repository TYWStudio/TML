Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions

''' <summary>一个可用的 Java 运行时。</summary>
Public Class JavaItem
    Public Property Path As String = ""
    Public Property Major As Integer = 0
    Public Property FullVersion As String = ""
    Public Property Arch As String = ""
    ''' <summary>架构名：arm64 / x64 / x86（macOS 上要挑与启动器同架构的 Java，否则 Rosetta 下会崩）。</summary>
    Public Property ArchName As String = ""
    Public Property Vendor As String = ""

    Public ReadOnly Property Display As String
        Get
            Dim bits = If(Arch = "64", "x64", "x86")
            Dim ver = If(FullVersion = Major.ToString(), "", " " & FullVersion)
            Return $"Java {Major}{ver}  ({bits})"
        End Get
    End Property
End Class

''' <summary>
''' Java 检测（Linux / macOS 共用）。对应 Windows 版 ModJava。
''' 搜索顺序：启动器自带 JRE → 设置中手工指定 → PATH → 系统 JVM 目录。
''' </summary>
Public Module ModJava

    Private _cache As List(Of JavaItem) = Nothing

    ''' <summary>启动器自己下载的 JRE 存放位置。</summary>
    Public ReadOnly RuntimeDir As String = Path.Combine(ModPaths.Root, "Java")

    Public Function ListJava(Optional force As Boolean = False) As List(Of JavaItem)
        If _cache IsNot Nothing AndAlso Not force Then Return _cache
        Dim result As New List(Of JavaItem)
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)

        ' 1. 启动器自带
        For Each p In CandidatesFromRuntimeDir()
            Probe(p, result, seen)
        Next
        ' 2. 手工指定
        Dim manual = ModSettings.GetString("LaunchJavaExe")
        If Not String.IsNullOrWhiteSpace(manual) Then Probe(manual, result, seen)
        ' 3. PATH
        Dim pathEnv = Environment.GetEnvironmentVariable("PATH")
        If Not String.IsNullOrEmpty(pathEnv) Then
            For Each d In pathEnv.Split(Path.PathSeparator)
                If d.Length = 0 Then Continue For
                Dim j = Path.Combine(d, "java")
                If File.Exists(j) AndAlso Not ModPlatform.IsJavaStub(j) Then Probe(j, result, seen)
            Next
        End If
        ' 4. 系统 JVM 目录（Linux: /usr/lib/jvm 等；macOS: /Library/Java/JavaVirtualMachines）
        For Each jvmRoot In ModPlatform.JvmRoots()
            Try
                If Not Directory.Exists(jvmRoot) Then Continue For
                For Each subDir In Directory.GetDirectories(jvmRoot)
                    For Each rel In ModPlatform.JavaRelPaths()
                        Dim j = Path.Combine(subDir, rel.Replace("/"c, Path.DirectorySeparatorChar))
                        If File.Exists(j) AndAlso Not ModPlatform.IsJavaStub(j) Then Probe(j, result, seen)
                    Next
                Next
            Catch
            End Try
        Next

        result.Sort(Function(a, b) a.Major.CompareTo(b.Major))
        _cache = result
        Return result
    End Function

    Private Iterator Function CandidatesFromRuntimeDir() As IEnumerable(Of String)
        If Not Directory.Exists(RuntimeDir) Then Return
        For Each subDir In Directory.GetDirectories(RuntimeDir)
            Dim j = Path.Combine(subDir, "bin", "java")
            If File.Exists(j) Then Yield j
        Next
    End Function

    Private Sub Probe(exe As String, result As List(Of JavaItem), seen As HashSet(Of String))
        Try
            If String.IsNullOrWhiteSpace(exe) OrElse Not File.Exists(exe) Then Return
            ' 解析符号链接（/usr/bin/java → alternatives → jvm/bin/java 往往有好几层）
            Dim real = ResolveReal(exe)
            If Not seen.Add(real) Then Return

            Dim item As JavaItem = ReadVersion(exe)
            If item Is Nothing Then Return
            item.Path = exe
            result.Add(item)
        Catch ex As Exception
            ModLog.Err(ex, "探测 Java：" & exe)
        End Try
    End Sub

    Private Function ResolveReal(startPath As String) As String
        ' Linux 上直接用 libc 的 realpath：能把路径里每一级目录的符号链接都解开
        '（/usr/bin/java → /etc/alternatives/java → /usr/lib/jvm/java-21-openjdk-amd64/bin/java，
        ' 而 java-21-openjdk-amd64 本身又是指向 java-1.21.0-openjdk-amd64 的链接）
        ' macOS 的 libSystem 没有同名导出，走下面的托管实现（FileInfo.LinkTarget）。
        If ModPlatform.IsLinux Then
            Try
                Dim p = RealPathNative(startPath, IntPtr.Zero)
                If p <> IntPtr.Zero Then
                    Dim s = Marshal.PtrToStringAnsi(p)
                    FreeNative(p)
                    If Not String.IsNullOrEmpty(s) Then Return s
                End If
            Catch
            End Try
        End If

        Dim cur = startPath
        For i = 1 To 8
            Try
                Dim fi As New FileInfo(cur)
                If fi.LinkTarget Is Nothing Then Return cur
                Dim nextPath = fi.LinkTarget
                If Not Path.IsPathRooted(nextPath) Then nextPath = Path.Combine(fi.DirectoryName, nextPath)
                cur = Path.GetFullPath(nextPath)
            Catch
                Return cur
            End Try
        Next
        Return cur
    End Function

    <DllImport("libc", EntryPoint:="realpath", CharSet:=CharSet.Ansi)>
    Private Function RealPathNative(path As String, resolved As IntPtr) As IntPtr
    End Function

    <DllImport("libc", EntryPoint:="free")>
    Private Sub FreeNative(ptr As IntPtr)
    End Sub

    Private Function ReadVersion(exe As String) As JavaItem
        Try
            Dim psi As New ProcessStartInfo(exe) With {
                .RedirectStandardError = True,
                .RedirectStandardOutput = True,
                .UseShellExecute = False,
                .CreateNoWindow = True
            }
            psi.ArgumentList.Add("-XshowSettings:properties")
            psi.ArgumentList.Add("-version")
            Using p = Process.Start(psi)
                Dim out = p.StandardError.ReadToEnd()
                p.StandardOutput.ReadToEnd()
                If Not p.WaitForExit(8000) Then
                    Try : p.Kill() : Catch : End Try
                    Return Nothing
                End If
                Dim item As New JavaItem
                Dim spec = Regex.Match(out, "java\.specification\.version\s*=\s*([\d\.]+)")
                Dim full = Regex.Match(out, "java\.version\s*=\s*([\d\._]+)")
                If spec.Success Then
                    item.FullVersion = If(full.Success, full.Groups(1).Value, spec.Groups(1).Value)
                    item.Major = ParseMajor(spec.Groups(1).Value)
                ElseIf full.Success Then
                    item.FullVersion = full.Groups(1).Value
                    item.Major = ParseMajor(item.FullVersion)
                Else
                    Return Nothing
                End If
                Dim am = Regex.Match(out, "os\.arch\s*=\s*(\S+)")
                If am.Success Then
                    Dim a = am.Groups(1).Value.ToLowerInvariant()
                    item.Arch = If(a.Contains("64"), "64", "32")
                    item.ArchName = ModPlatform.NormalizeArch(a)
                End If
                Dim vm = Regex.Match(out, "java\.vm\.vendor\s*=\s*(.+)")
                If vm.Success Then item.Vendor = vm.Groups(1).Value.Trim()
                If item.Major <= 0 Then Return Nothing
                Return item
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>把 1.8.0_422 / 17.0.9 / 21 这类版本号折算成主版本号。</summary>
    Public Function ParseMajor(v As String) As Integer
        If String.IsNullOrEmpty(v) Then Return 0
        Dim parts = v.Split("."c, "_"c, "-"c, "+"c)
        Dim first As Integer
        If Not Integer.TryParse(parts(0), first) Then Return 0
        If first = 1 AndAlso parts.Length > 1 Then
            Dim second As Integer
            If Integer.TryParse(parts(1), second) Then Return second
            Return 1
        End If
        Return first
    End Function

    ''' <summary>
    ''' 按版本要求挑一个最合适的 Java：同大版本优先，其次更高的。
    ''' 同版本里优先与启动器同架构的（Apple Silicon 上混用 x64 Java 会走 Rosetta，性能与 native 库都会出问题）。
    ''' </summary>
    Public Function Best(Optional required As Integer = 0) As JavaItem
        Dim list = ListJava()
        If list.Count = 0 Then Return Nothing

        ' 先挑与启动器架构一致的；一个都没有（老 JDK 不报 os.arch）再用全部
        Dim sameArch = list.Where(Function(j) String.IsNullOrEmpty(j.ArchName) OrElse j.ArchName = ModPlatform.ArchName).ToList()
        If sameArch.Count > 0 Then list = sameArch

        If required <= 0 Then Return list(list.Count - 1)
        Dim exact = list.LastOrDefault(Function(j) j.Major = required)
        If exact IsNot Nothing Then Return exact
        Dim higher = list.Where(Function(j) j.Major > required).OrderBy(Function(j) j.Major).FirstOrDefault()
        If higher IsNot Nothing Then Return higher
        Return list(list.Count - 1)
    End Function

    ''' <summary>版本 JSON 要求的 Java 大版本，读不到就按 MC 版本号猜。</summary>
    Public Function RequiredFor(versionJsonMajor As Integer, mcId As String) As Integer
        If versionJsonMajor > 0 Then Return versionJsonMajor
        Dim m = Regex.Match(mcId, "^(\d+)\.(\d+)")
        If m.Success Then
            Dim major = Integer.Parse(m.Groups(1).Value)
            Dim minor = Integer.Parse(m.Groups(2).Value)
            If major = 1 Then
                If minor >= 20 Then Return 17
                If minor >= 18 Then Return 17
                If minor >= 17 Then Return 16
                Return 8
            End If
        End If
        Return 8
    End Function

    Public Sub Invalidate()
        _cache = Nothing
    End Sub

End Module
