Imports System.IO

''' <summary>
''' 服务端地址解析。
'''
''' 开源仓库里不带任何服务器地址：地址为空时，公告、启动器更新、内测码校验、封禁查询、
''' 匿名上报全部自动关闭，启动器纯本地运行。
'''
''' 想接到自己的服务端，任选一种（优先级从高到低）：
'''   1) 设置里的 ServerUrl（设置 → 启动器 → 服务端地址）
'''   2) 环境变量 TYW_SERVER
'''   3) 程序目录 / 数据目录下的 server.txt，内容就一行地址
'''   4) 编译期常量（打包脚本用 TYW_SERVER_URL 注入）
''' </summary>
Public Module ModServerUrl

    ''' <summary>编译期注入的默认地址。开源仓库里恒为空，由打包脚本在构建前替换这一行。</summary>
    Public Const BuiltIn As String = ""
    Public Const BuiltInStoreApiKey As String = ""

    Private _cached As String = Nothing

    ''' <summary>当前生效的服务端地址（保证以 / 结尾）。空字符串表示未配置。</summary>
    Public ReadOnly Property Current As String
        Get
            If _cached IsNot Nothing Then Return _cached
            _cached = Resolve()
            Return _cached
        End Get
    End Property

    ''' <summary>设置里改了地址后调用，清掉缓存。</summary>
    Public Sub Invalidate()
        _cached = Nothing
    End Sub

    Private Function Resolve() As String
        Try
            Dim custom As String = ""
            Try
                custom = ModSettings.GetString("ServerUrl", "")
            Catch
            End Try
            If String.IsNullOrWhiteSpace(custom) Then
                custom = Environment.GetEnvironmentVariable("TYW_SERVER")
            End If
            If String.IsNullOrWhiteSpace(custom) Then
                For Each candidate In {
                        Path.Combine(AppContext.BaseDirectory, "server.txt"),
                        Path.Combine(ModPaths.Root, "server.txt")}
                    Try
                        If File.Exists(candidate) Then
                            custom = File.ReadAllText(candidate).Trim()
                            If custom <> "" Then Exit For
                        End If
                    Catch
                    End Try
                Next
            End If
            If String.IsNullOrWhiteSpace(custom) Then custom = BuiltIn
            custom = If(custom, "").Trim()
            If custom = "" Then Return ""
            If Not custom.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then custom = "http://" & custom
            If Not custom.EndsWith("/"c) Then custom &= "/"
            Return custom
        Catch ex As Exception
            ModLog.Err(ex, "解析服务端地址")
            Return ""
        End Try
    End Function

End Module
