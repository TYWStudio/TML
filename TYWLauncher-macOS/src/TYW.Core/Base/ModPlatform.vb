Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions

''' <summary>
''' 平台差异集中在这里。Linux 与 macOS 共用同一套 Avalonia 界面与业务代码，
''' 凡是跟系统相关的（数据目录、native 分类名、JVM 搜索路径、启动参数、安装包名）
''' 一律从这里取，避免在几十个文件里散落 If OperatingSystem.IsMacOS()。
''' </summary>
Public Module ModPlatform

    Public ReadOnly Property IsLinux As Boolean = OperatingSystem.IsLinux()
    Public ReadOnly Property IsMac As Boolean = OperatingSystem.IsMacOS()

    ''' <summary>平台显示名，界面文案用。</summary>
    Public ReadOnly Property DisplayName As String = If(IsMac, "macOS", "Linux")

    ''' <summary>进程架构：arm64 / x64。</summary>
    Public ReadOnly Property ArchName As String =
        If(RuntimeInformation.ProcessArchitecture = Architecture.Arm64, "arm64", "x64")

    Public ReadOnly Property IsArm64 As Boolean = (ArchName = "arm64")

    ''' <summary>用户主目录。</summary>
    Public ReadOnly Property Home As String =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)

    ''' <summary>
    ''' 自动更新：Linux 覆盖目录，macOS 整体替换 .app 后 ad-hoc 重签名，
    ''' 两边都支持（实现在 ModUpdate.ApplyUpdate 里按平台分支）。
    ''' </summary>
    Public ReadOnly Property SupportsSelfUpdate As Boolean = True

    ''' <summary>通道切换（下载别的通道的包替换安装）两个平台都支持。</summary>
    Public ReadOnly Property SupportsChannelSwitch As Boolean = True

    ''' <summary>
    ''' 联机（EasyTier）需要创建 TUN 虚拟网卡：Linux 上普通进程即可（权限由打包/用户决定），
    ''' macOS 必须 root，首版暂不开启，界面给出说明。
    ''' </summary>
    Public ReadOnly Property SupportsLink As Boolean = Not IsMac

    ''' <summary>natives 解压目录名。</summary>
    Public ReadOnly Property NativesDirName As String = If(IsMac, "natives-macos", "natives-linux")

    ''' <summary>服务端 config.json 里本平台对应的段名（Linux / MacOS）。</summary>
    Public ReadOnly Property ServerSection As String = If(IsMac, "MacOS", "Linux")

    ''' <summary>服务端联机下载列表的键名。</summary>
    Public ReadOnly Property LinkDownloadsKey As String = If(IsMac, "DownloadsMacOS", "DownloadsLinux")

#Region "安装包命名"

    ''' <summary>当前通道在服务端 www 里的安装包文件名。</summary>
    Public Function PackageFileName(channel As LauncherChannel) As String
        Dim suffix As String
        Select Case channel
            Case LauncherChannel.Snapshot : suffix = "-snapshot"
            Case LauncherChannel.Beta : suffix = "-beta"
            Case Else : suffix = ""
        End Select
        If IsMac Then Return "TYWLauncher-macos-" & ArchName & suffix & ".zip"
        ' Linux 的历史包名不含架构（一直是 x64 名），保持不动，否则老客户端找不到更新
        Return "TYWLauncher-linux-x64" & suffix & ".tar.gz"
    End Function

#End Region

#Region "Minecraft 原生库分类"

    ''' <summary>
    ''' 给版本 JSON 里的 natives classifier 打分：0 = 不是本平台的，分数越高越优先。
    ''' macOS 的 JSON 里是 natives-macos（x64）和 natives-macos-arm64；
    ''' Linux 是 natives-linux 与 natives-linux-arm64；老版本没有架构后缀，作为兜底。
    ''' </summary>
    Public Function ClassifierScore(key As String) As Integer
        If String.IsNullOrEmpty(key) Then Return 0
        Dim k = key.ToLowerInvariant()
        Dim isArmKey = k.Contains("arm64") OrElse k.Contains("aarch64")
        Dim hasArchKey = isArmKey OrElse k.Contains("x86_64") OrElse k.Contains("amd64") OrElse k.Contains("x64")

        If k.Contains("windows") OrElse k.Contains("win32") OrElse k.Contains("win64") Then Return 0

        Dim wantArm = IsArm64
        If k.Contains("macos") OrElse k.Contains("osx") OrElse k.Contains("darwin") Then
            If Not IsMac Then Return 0
        ElseIf k.Contains("linux") Then
            If Not IsLinux Then Return 0
        Else
            Return 0
        End If

        If hasArchKey AndAlso (wantArm <> isArmKey) Then Return 0
        If isArmKey Then Return 92
        If hasArchKey Then Return 90
        Return 50   ' 没有架构后缀的老式分类（只有 x64 原生库）
    End Function

    ''' <summary>从一组 classifier 名里挑本平台最合适的那个；没有返回空串。</summary>
    Public Function BestClassifier(keys As IEnumerable(Of String)) As String
        Dim best As String = ""
        Dim bestScore = 0
        For Each k In keys
            Dim s = ClassifierScore(k)
            If s > bestScore Then
                bestScore = s
                best = k
            End If
        Next
        Return best
    End Function

#End Region

#Region "Java"

    ''' <summary>系统级 JVM 安装根目录。</summary>
    Public Function JvmRoots() As String()
        If IsMac Then
            Return {"/Library/Java/JavaVirtualMachines",
                    Path.Combine(Home, "Library", "Java", "JavaVirtualMachines")}
        End If
        Return {"/usr/lib/jvm", "/usr/java", "/opt/java", "/opt/jdk", "/usr/local/java"}
    End Function

    ''' <summary>某个 JVM 根目录条目下 java 的相对路径（macOS 的 .jdk 包要多走 Contents/Home）。</summary>
    Public Function JavaRelPaths() As String()
        If IsMac Then Return {"Contents/Home/bin/java"}
        Return {"bin/java", "jre/bin/java"}
    End Function

    ''' <summary>
    ''' 是否需要排除这个 java：macOS 的 /usr/bin/java 只是占位壳，
    ''' 没装 JDK 时执行它会弹「需要安装 Java 运行时」的系统对话框。
    ''' </summary>
    Public Function IsJavaStub(exe As String) As Boolean
        If Not IsMac Then Return False
        Return String.Equals(Path.GetFullPath(exe), "/usr/bin/java", StringComparison.Ordinal)
    End Function

    ''' <summary>把 os.arch 的原始值折算成 arm64 / x64 之类。</summary>
    Public Function NormalizeArch(raw As String) As String
        If String.IsNullOrEmpty(raw) Then Return ""
        Dim a = raw.ToLowerInvariant()
        If a.Contains("aarch64") OrElse a.Contains("arm64") Then Return "arm64"
        If a.Contains("x86_64") OrElse a.Contains("amd64") OrElse a = "x64" Then Return "x64"
        If a.Contains("x86") OrElse a.Contains("i386") OrElse a.Contains("i686") Then Return "x86"
        Return a
    End Function

#End Region

#Region "启动参数"

    ''' <summary>
    ''' macOS 上 Minecraft 1.19+（LWJGL 3.3.1）必须在主线程初始化 GLFW，
    ''' 否则启动直接崩（"GLFW may only be used on the main thread"）。
    ''' 版本号猜不到时也加上：多加不影响，少加会崩。
    ''' </summary>
    Public Function NeedsStartOnFirstThread(mcId As String) As Boolean
        If Not IsMac Then Return False
        Dim m = Regex.Match(If(mcId, ""), "^(\d+)\.(\d+)")
        If Not m.Success Then Return True
        Dim major = Integer.Parse(m.Groups(1).Value)
        Dim minor = Integer.Parse(m.Groups(2).Value)
        Return major > 1 OrElse (major = 1 AndAlso minor >= 19)
    End Function

#End Region

End Module
