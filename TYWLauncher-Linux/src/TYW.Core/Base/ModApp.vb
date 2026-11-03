''' <summary>
''' 启动器通道。对应 Windows 版的 BuildTypes：Release = 正式版、Snapshot = 快照版、Beta = 内测版。
''' 由编译常量决定（Release 配置 = 正式，Snapshot 配置 = 快照，Debug 配置 = 内测），
''' 也可以用环境变量 TYW_CHANNEL=Release/Snapshot/Beta 临时覆盖（调试用）。
''' </summary>
Public Enum LauncherChannel
    Release = 0
    Snapshot = 1
    Beta = 2
End Enum

''' <summary>
''' 应用版本信息。发布新版本时改对应通道的版本码与版本名即可。
''' </summary>
Public Module ModApp

    ''' <summary>当前通道。</summary>
    Public ReadOnly Channel As LauncherChannel = DetectChannel()

    Private Function DetectChannel() As LauncherChannel
        '调试用：不重新编译也能验证其他通道的界面与更新逻辑
        Dim env = Environment.GetEnvironmentVariable("TYW_CHANNEL")
        If Not String.IsNullOrWhiteSpace(env) Then
            Select Case env.Trim().ToLowerInvariant()
                Case "snapshot", "快照" : Return LauncherChannel.Snapshot
                Case "beta", "内测" : Return LauncherChannel.Beta
                Case "release", "正式" : Return LauncherChannel.Release
            End Select
        End If
#If TYW_CHANNEL_BETA Then
        Return LauncherChannel.Beta
#ElseIf TYW_CHANNEL_SNAPSHOT Then
        Return LauncherChannel.Snapshot
#Else
        Return LauncherChannel.Release
#End If
    End Function

    ''' <summary>通道名（用于提示、配置文件路径等）。</summary>
    Public ReadOnly Property ChannelKey As String
        Get
            Select Case Channel
                Case LauncherChannel.Snapshot : Return "Snapshot"
                Case LauncherChannel.Beta : Return "Beta"
                Case Else : Return "Release"
            End Select
        End Get
    End Property

    ''' <summary>通道显示名：正式版 / 快照版 / 内测版。</summary>
    Public ReadOnly Property ChannelDisplay As String
        Get
            Select Case Channel
                Case LauncherChannel.Snapshot : Return "快照版"
                Case LauncherChannel.Beta : Return "内测版"
                Case Else : Return "正式版"
            End Select
        End Get
    End Property

    ''' <summary>展示用版本名。</summary>
    Public ReadOnly Property VersionBaseName As String
        Get
            Select Case Channel
                Case LauncherChannel.Snapshot : Return "1.1.36-snapshot"
                Case LauncherChannel.Beta : Return "1.1.36-beta"
                Case Else : Return "1.1.36"
            End Select
        End Get
    End Property

    ''' <summary>
    ''' 版本码：与服务端 config.json 的 Linux/MacOS.Channels.&lt;通道&gt;.VersionCode 对比。
    ''' 每个通道、每个平台各自从 1 开始递增，互不影响。
    ''' </summary>
    Public ReadOnly Property VersionCode As Integer
        Get
            If ModPlatform.IsMac Then
                ' macOS 首发，独立计数
                Select Case Channel
                    Case LauncherChannel.Snapshot : Return 1
                    Case LauncherChannel.Beta : Return 1
                    Case Else : Return 1
                End Select
            End If
            Select Case Channel
                Case LauncherChannel.Snapshot : Return 4
                Case LauncherChannel.Beta : Return 4
                Case Else : Return 5
            End Select
        End Get
    End Property

    ''' <summary>当前通道在服务端 www 里的安装包文件名（按平台）。</summary>
    Public ReadOnly Property PackageFileName As String
        Get
            Return ModPlatform.PackageFileName(Channel)
        End Get
    End Property

    ''' <summary>当前架构描述（x64 / arm64），用于拼 EasyTier 等下载地址。</summary>
    Public ReadOnly RuntimeArch As String =
        If(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = System.Runtime.InteropServices.Architecture.Arm64, "arm64", "x64")

End Module
