Imports System.Text.Json

''' <summary>
''' 内测通道（Beta）：内测码校验、单次更新码、启动前联网校验。
''' 对应 Windows 版 Modules/ModBeta.vb。
''' </summary>
Public Module ModBeta

    Public Structure BetaResult
        Public Ok As Boolean
        Public Message As String
        Public VersionCode As Integer
    End Structure

    ''' <summary>把设备码 + 内测码送到服务端校验（GET /beta）。顺带拿到服务端内测版版本码。</summary>
    Public Function VerifyBetaCode(code As String) As BetaResult
        Return CallBeta("beta?ident=" & Uri.EscapeDataString(ModSecret.DeviceCode) &
                        "&code=" & Uri.EscapeDataString(If(code, "").Trim))
    End Function

    ''' <summary>
    ''' 把设备码 + 单次更新码送到服务端校验（GET /betaupdate）。
    ''' 校验通过服务端立即把更新码标记为已用；客户端更新完成后再上报 done=1 删除记录。
    ''' 已是最新版本时服务端不消耗更新码。
    ''' </summary>
    Public Function VerifyBetaUpdateCode(code As String) As BetaResult
        Return CallBeta("betaupdate?ident=" & Uri.EscapeDataString(ModSecret.DeviceCode) &
                        "&code=" & Uri.EscapeDataString(If(code, "").Trim) &
                        "&ver=" & ModApp.VersionCode)
    End Function

    ''' <summary>更新完成后上报（GET /betaupdate?...&amp;done=1）：删除已用的更新码记录。失败只写日志。</summary>
    Public Sub ReportBetaUpdateDone(code As String)
        Try
            ModNet.GetServer("betaupdate?ident=" & Uri.EscapeDataString(ModSecret.DeviceCode) &
                             "&code=" & Uri.EscapeDataString(If(code, "").Trim) & "&done=1", 8)
            ModLog.Info("内测版更新完成已上报服务端，更新码记录已删除")
        Catch ex As Exception
            ModLog.Info("内测版更新完成上报失败（不影响更新）：" & ex.Message)
        End Try
    End Sub

    Private Function CallBeta(path As String) As BetaResult
        Dim raw = ModNet.GetServerStrict(path, 12)
        Using doc = JsonDocument.Parse(raw)
            Dim root = doc.RootElement
            Dim ok As Boolean = False
            Dim msg As String = ""
            Dim ver As Integer = 0
            Dim el As JsonElement
            If root.TryGetProperty("ok", el) Then ok = el.ValueKind = JsonValueKind.True
            If root.TryGetProperty("msg", el) Then msg = el.GetString()
            If root.TryGetProperty("version_code", el) Then ver = el.GetInt32()
            Return New BetaResult With {.Ok = ok, .Message = msg, .VersionCode = ver}
        End Using
    End Function

    ''' <summary>
    ''' 内测版启动前的联网校验。返回 (是否可用, 失败原因)。
    ''' 连不上服务端同样判为不可用（内测版必须联网，与 Windows 版一致）。
    ''' </summary>
    Public Function CheckUsable() As (Ok As Boolean, Message As String)
        Try
            Dim result = VerifyBetaCode(ModSettings.GetString("BetaCode", ""))
            If result.Ok Then
                ModLog.Info("内测版校验通过（服务端内测版版本码 " & result.VersionCode & "）")
                Return (True, "")
            End If
            Return (False, "内测资格已失效：" & If(String.IsNullOrWhiteSpace(result.Message), "内测码无效或已被停用", result.Message))
        Catch ex As Exception
            Return (False, "内测版必须联网并连接服务端才能使用。" & Environment.NewLine &
                           "当前无法连接服务端：" & ModNet.ServerAddress & Environment.NewLine &
                           "请检查网络后重新启动；也可以先切回正式版。")
        End Try
    End Function

End Module
