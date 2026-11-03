Imports System.IO
Imports System.Text

Public Enum LogLevel
    Debug = 0
    Info = 1
    Warn = 2
    [Error] = 3
End Enum

''' <summary>
''' 运行日志。对应 Windows 版 ModBase.Log。
''' </summary>
Public Module ModLog

    Private ReadOnly Lock As New Object()
    Private ReadOnly RecentLines As New List(Of String)

    Public Sub Write(level As LogLevel, msg As String)
        Dim line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {msg}"
        SyncLock Lock
            If RecentLines.Count > 400 Then RecentLines.RemoveRange(0, 200)
            RecentLines.Add(line)
        End SyncLock
        Try
            ModPaths.EnsureAll()
            File.AppendAllText(ModPaths.LogFile, line & Environment.NewLine, Encoding.UTF8)
        Catch
        End Try
    End Sub

    Public Sub Info(msg As String)
        Write(LogLevel.Info, msg)
    End Sub

    Public Sub Warn(msg As String)
        Write(LogLevel.Warn, msg)
    End Sub

    Public Sub Err(msg As String)
        Write(LogLevel.Error, msg)
    End Sub

    Public Sub Err(ex As Exception, Optional where As String = "")
        Write(LogLevel.Error, $"{where} {ex.GetType().Name}: {ex.Message}")
    End Sub

    ''' <summary>给「启动日志」卡片用的最近日志文本。</summary>
    Public Function Recent() As String
        SyncLock Lock
            Return String.Join(Environment.NewLine, RecentLines)
        End SyncLock
    End Function

End Module
