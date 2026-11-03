Imports System.IO

''' <summary>
''' 路径约定（对应 Windows 版 ModBase / Path）。平台差异从 ModPlatform 取：
''' Linux 走 XDG（~/.local/share、~/.minecraft），
''' macOS 走 Apple 惯例（~/Library/Application Support/TYWLauncher、.../minecraft）。
''' </summary>
Public Module ModPaths

    Private ReadOnly Home As String = ModPlatform.Home

    ''' <summary>启动器数据目录。</summary>
    Public ReadOnly Root As String =
        If(Environment.GetEnvironmentVariable("TYW_DATA_DIR"),
           If(ModPlatform.IsMac,
              Path.Combine(Home, "Library", "Application Support", "TYWLauncher"),
              Path.Combine(Home, ".local", "share", "TYWLauncher")))

    ''' <summary>游戏目录：默认与原版启动器一致，可用 TYW_MC_DIR 覆盖。</summary>
    Public ReadOnly McFolder As String =
        If(Environment.GetEnvironmentVariable("TYW_MC_DIR"),
           If(ModPlatform.IsMac,
              Path.Combine(Home, "Library", "Application Support", "minecraft"),
              Path.Combine(Home, ".minecraft")))

    Public ReadOnly LogDir As String = Path.Combine(Root, "Log")
    Public ReadOnly CacheDir As String = Path.Combine(Root, "Cache")
    Public ReadOnly TempDir As String = Path.Combine(Root, "Temp")

    Public ReadOnly LogFile As String = Path.Combine(LogDir, "TYWLauncher.log")
    Public ReadOnly SettingFile As String = Path.Combine(Root, "setup.ini")

    Public Sub EnsureAll()
        For Each d In {Root, LogDir, CacheDir, TempDir}
            Try
                If Not Directory.Exists(d) Then Directory.CreateDirectory(d)
            Catch
            End Try
        Next
    End Sub

End Module
