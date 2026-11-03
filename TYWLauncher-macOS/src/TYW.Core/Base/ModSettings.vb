Imports System.IO
Imports System.Text.Json

''' <summary>
''' 设置读写。存成每行 key:value 的纯文本，键名与 Windows 版保持一致，方便迁移。
''' </summary>
Public Module ModSettings

    Private ReadOnly Lock As New Object()
    Private ReadOnly Data As New Dictionary(Of String, String)
    Private _loaded As Boolean

    ''' <summary>默认值表，key 与 Windows 版 Setup 一致。</summary>
    Public ReadOnly Defaults As New Dictionary(Of String, String) From {
        {"UiLogoLeft", "True"},
        {"UiLogoType", "0"},
        {"UiTheme", "0"},
        {"UiDarkMode", "False"},
        {"UiMusic", "False"},
        {"LoginType", "0"},
        {"LaunchMode", "0"},
        {"VersionSelect", ""},
        {"Language", "zh-CN"},
        {"UiHide", ""}
    }

    Public Sub Load()
        SyncLock Lock
            If _loaded Then Return
            _loaded = True
            For Each kv In Defaults
                Data(kv.Key) = kv.Value
            Next
            Try
                If File.Exists(ModPaths.SettingFile) Then
                    For Each line In File.ReadAllLines(ModPaths.SettingFile)
                        Dim i = line.IndexOf(":"c)
                        If i > 0 Then Data(line.Substring(0, i)) = line.Substring(i + 1)
                    Next
                End If
            Catch ex As Exception
                ModLog.Err(ex, "读取设置")
            End Try
        End SyncLock
    End Sub

    Public Function GetString(key As String, Optional def As String = "") As String
        Load()
        SyncLock Lock
            Dim v As String = Nothing
            If Data.TryGetValue(key, v) AndAlso v IsNot Nothing Then Return v
        End SyncLock
        Return def
    End Function

    Public Function GetBool(key As String, Optional def As Boolean = False) As Boolean
        Dim v = GetString(key, If(def, "True", "False"))
        Return v.Equals("True", StringComparison.OrdinalIgnoreCase) OrElse v = "1"
    End Function

    Public Function GetInt(key As String, Optional def As Integer = 0) As Integer
        Dim n As Integer
        If Integer.TryParse(GetString(key, def.ToString()), n) Then Return n
        Return def
    End Function

    Public Sub SetValue(key As String, value As String)
        Load()
        SyncLock Lock
            Data(key) = If(value, "")
        End SyncLock
    End Sub

    Public Sub SetValue(key As String, value As Boolean)
        SetValue(key, If(value, "True", "False"))
    End Sub

    Public Sub Save()
        SyncLock Lock
            Try
                ModPaths.EnsureAll()
                Dim sb As New Text.StringBuilder
                For Each kv In Data
                    sb.AppendLine(kv.Key & ":" & kv.Value)
                Next
                File.WriteAllText(ModPaths.SettingFile, sb.ToString(), Text.Encoding.UTF8)
            Catch ex As Exception
                ModLog.Err(ex, "保存设置")
            End Try
        End SyncLock
    End Sub

End Module
