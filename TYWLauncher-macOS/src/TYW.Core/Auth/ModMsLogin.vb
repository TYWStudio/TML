Imports System.IO
Imports System.Net.Http
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>设备码流程返回给界面的信息。</summary>
Public Class MsDeviceCode
    Public Property DeviceCode As String = ""
    Public Property UserCode As String = ""
    Public Property VerificationUri As String = ""
    Public Property Message As String = ""
    Public Property Interval As Integer = 5
    Public Property ExpiresIn As Integer = 900
End Class

''' <summary>登录完成后的账号信息。</summary>
Public Class MsAccount
    Public Property Name As String = ""
    Public Property Uuid As String = ""
    Public Property AccessToken As String = ""
    Public Property RefreshToken As String = ""
    ''' <summary>Minecraft 令牌过期时间（Unix 秒）。</summary>
    Public Property ExpiresAt As Long = 0
End Class

''' <summary>
''' 微软正版登录。对应 Windows 版 ModLaunch 的 MsLoginStep1~6，只是第一步换成设备码流程：
''' Linux 上没有内嵌浏览器，用「打开网页 + 输入配对码」的方式拿 OAuth 令牌。
''' </summary>
Public Module ModMsLogin

    ''' <summary>与 Windows 版同一个应用注册（公开客户端）。</summary>
    Public ReadOnly ClientId As String =
        If(Environment.GetEnvironmentVariable("TYW_MS_CLIENT_ID"),
           "852d43f4-7b54-4544-9a0e-fc04c3c35c82")

    Private Const Authority As String = "https://login.microsoftonline.com/consumers/oauth2/v2.0"
    Private Const Scope As String = "XboxLive.signin offline_access"

    ''' <summary>第 1 步：申请设备码。</summary>
    Public Function RequestDeviceCode() As MsDeviceCode
        Dim body = $"client_id={ClientId}&scope={Uri.EscapeDataString(Scope)}"
        Dim json = PostForm(Authority & "/devicecode", body)
        Using doc = JsonDocument.Parse(json)
            Dim r = doc.RootElement
            Dim dc As New MsDeviceCode With {
                .DeviceCode = GetStr(r, "device_code"),
                .UserCode = GetStr(r, "user_code"),
                .VerificationUri = GetStr(r, "verification_uri"),
                .Message = GetStr(r, "message")
            }
            Dim iv As JsonElement
            If r.TryGetProperty("interval", iv) AndAlso iv.ValueKind = JsonValueKind.Number Then dc.Interval = iv.GetInt32()
            Dim ex As JsonElement
            If r.TryGetProperty("expires_in", ex) AndAlso ex.ValueKind = JsonValueKind.Number Then dc.ExpiresIn = ex.GetInt32()
            If String.IsNullOrEmpty(dc.DeviceCode) Then
                Throw New Exception("申请登录码失败：" & json)
            End If
            Return dc
        End Using
    End Function

    ''' <summary>
    ''' 第 1 步（续）：轮询换取 OAuth 令牌，阻塞直到成功、超时或被取消。
    ''' 成功回调里拿到的是微软令牌，还需要走 2~6 步换 Minecraft 令牌。
    ''' </summary>
    Public Function WaitForToken(dc As MsDeviceCode, cancel As Func(Of Boolean), status As Action(Of String)) As MsAccount
        Dim deadline = DateTime.UtcNow.AddSeconds(dc.ExpiresIn)
        Dim interval = Math.Max(2, dc.Interval)
        While DateTime.UtcNow < deadline
            If cancel IsNot Nothing AndAlso cancel() Then Throw New OperationCanceledException()
            Thread.Sleep(interval * 1000)
            If cancel IsNot Nothing AndAlso cancel() Then Throw New OperationCanceledException()

            Dim body = $"grant_type=urn:ietf:params:oauth:grant-type:device_code&client_id={ClientId}&device_code={Uri.EscapeDataString(dc.DeviceCode)}"
            Dim resp As String
            Try
                resp = PostForm(Authority & "/token", body)
            Catch ex As Exception
                ' 400 里带 error 字段，需要区分「还在等」和「真的失败」
                Dim msg = ex.Message
                If msg.Contains("authorization_pending") Then
                    status?.Invoke("等待你在网页中完成登录…")
                    Continue While
                ElseIf msg.Contains("slow_down") Then
                    interval += 5
                    Continue While
                ElseIf msg.Contains("expired_token") Then
                    Throw New Exception("登录码已过期，请重新登录。")
                ElseIf msg.Contains("authorization_declined") Then
                    Throw New OperationCanceledException()
                Else
                    Throw
                End If
            End Try

            Using doc = JsonDocument.Parse(resp)
                Dim acc As New MsAccount With {
                    .AccessToken = GetStr(doc.RootElement, "access_token"),
                    .RefreshToken = GetStr(doc.RootElement, "refresh_token")
                }
                If String.IsNullOrEmpty(acc.AccessToken) Then Throw New Exception("登录失败：没有拿到令牌")
                status?.Invoke("正在验证 Xbox 账号…")
                Return ToMinecraft(acc.AccessToken, acc.RefreshToken, status)
            End Using
        End While
        Throw New Exception("登录超时，请重新登录。")
    End Function

    ''' <summary>用 refresh_token 静默续期（上次登录过就直接进游戏）。</summary>
    Public Function Refresh(refreshToken As String, Optional status As Action(Of String) = Nothing) As MsAccount
        Dim body = $"client_id={ClientId}&refresh_token={Uri.EscapeDataString(refreshToken)}&grant_type=refresh_token&scope={Uri.EscapeDataString(Scope)}"
        Dim resp = PostForm(Authority & "/token", body)
        Using doc = JsonDocument.Parse(resp)
            Dim at = GetStr(doc.RootElement, "access_token")
            Dim rt = GetStr(doc.RootElement, "refresh_token")
            If String.IsNullOrEmpty(at) Then Throw New Exception("刷新登录失败")
            status?.Invoke("正在验证 Xbox 账号…")
            Return ToMinecraft(at, If(rt = "", refreshToken, rt), status)
        End Using
    End Function

    ''' <summary>一键登录：申请设备码 → 界面显示 → 等待 → 换 MC 令牌。</summary>
    Public Function Login(showCode As Action(Of MsDeviceCode), cancel As Func(Of Boolean), status As Action(Of String)) As MsAccount
        status?.Invoke("正在申请登录码…")
        Dim dc = RequestDeviceCode()
        showCode?.Invoke(dc)
        Return WaitForToken(dc, cancel, status)
    End Function

    ''' <summary>
    ''' 首选方案：打开系统浏览器做标准授权码登录，回调走本机回环端口，
    ''' 用户登录完自动完成，不需要手输配对码。
    ''' 返回 Nothing 表示浏览器方案不可用（端口占用、用户超时、应用不接受回环地址），
    ''' 调用方应回退到 <see cref="Login"/> 的设备码流程。
    ''' </summary>
    Public Function LoginWithBrowser(openUrl As Action(Of String), cancel As Func(Of Boolean),
                                     status As Action(Of String),
                                     Optional timeoutSec As Integer = 240) As MsAccount
        Dim port = PickFreePort()
        Dim redirect = "http://localhost:" & port & "/"
        Dim listener As New Net.HttpListener()
        listener.Prefixes.Add(redirect)
        Try
            listener.Start()
        Catch ex As Exception
            ModLog.Err(ex, "启动本地登录回调端口失败")
            Return Nothing
        End Try

        Try
            Dim state = Guid.NewGuid().ToString("N")
            Dim url = Authority & "/authorize?client_id=" & ClientId &
                      "&response_type=code&redirect_uri=" & Uri.EscapeDataString(redirect) &
                      "&scope=" & Uri.EscapeDataString(Scope) &
                      "&prompt=select_account&state=" & state

            status?.Invoke("正在打开浏览器，请完成登录…")
            openUrl?.Invoke(url)

            Dim deadline = DateTime.UtcNow.AddSeconds(timeoutSec)
            While DateTime.UtcNow < deadline
                If cancel IsNot Nothing AndAlso cancel() Then Return Nothing

                Dim ctxTask = listener.GetContextAsync()
                Dim waited = 0
                While Not ctxTask.Wait(300)
                    waited += 300
                    If (cancel IsNot Nothing AndAlso cancel()) OrElse DateTime.UtcNow > deadline Then
                        Return Nothing
                    End If
                End While

                Dim ctx = ctxTask.Result
                Dim q = ctx.Request.QueryString
                Dim err = q("error")
                Dim code = q("code")
                Dim gotState = q("state")

                If Not String.IsNullOrEmpty(err) Then
                    RespondHtml(ctx, "登录未完成",
                        "登录被取消或出错（" & WebSafe(err) & "）。可以回到启动器重试。")
                    Return Nothing
                End If
                If String.IsNullOrEmpty(code) OrElse gotState <> state Then
                    RespondHtml(ctx, "无效的请求", "这个页面不能直接打开，请回到启动器重新登录。")
                    Continue While
                End If

                RespondHtml(ctx, "登录成功",
                    "TYW Launcher 已经拿到授权，可以关闭这个页面回到启动器了。")

                status?.Invoke("正在换取令牌…")
                Dim body = "grant_type=authorization_code&client_id=" & ClientId &
                           "&code=" & Uri.EscapeDataString(code) &
                           "&redirect_uri=" & Uri.EscapeDataString(redirect) &
                           "&scope=" & Uri.EscapeDataString(Scope)
                Dim json As String
                Try
                    json = PostForm(Authority & "/token", body)
                Catch ex As Exception
                    ModLog.Err(ex, "授权码换取令牌失败")
                    Return Nothing
                End Try

                Dim at = ""
                Dim rt = ""
                Using doc = JsonDocument.Parse(json)
                    at = GetStr(doc.RootElement, "access_token")
                    rt = GetStr(doc.RootElement, "refresh_token")
                End Using
                If String.IsNullOrEmpty(at) Then Return Nothing

                status?.Invoke("正在验证 Xbox 账号…")
                Return ToMinecraft(at, rt, status)
            End While
            Return Nothing
        Finally
            Try
                listener.Stop()
            Catch
            End Try
            listener.Close()
        End Try
    End Function

    Private Function PickFreePort() As Integer
        Dim l As New Net.Sockets.TcpListener(Net.IPAddress.Loopback, 0)
        l.Start()
        Dim p = CType(l.LocalEndpoint, Net.IPEndPoint).Port
        l.Stop()
        Return p
    End Function

    Private Sub RespondHtml(ctx As Net.HttpListenerContext, title As String, message As String)
        Try
            Dim html = "<!doctype html><html lang=""zh-CN""><head><meta charset=""utf-8"">" &
                       "<title>" & WebSafe(title) & "</title><style>" &
                       "body{margin:0;height:100vh;display:flex;align-items:center;justify-content:center;" &
                       "background:#f5f6f8;font-family:system-ui,sans-serif;color:#333}" &
                       "div{background:#fff;padding:36px 44px;border-radius:10px;box-shadow:0 2px 16px rgba(0,0,0,.08);text-align:center}" &
                       "h1{font-size:19px;margin:0 0 10px}p{font-size:13px;color:#666;margin:0}" &
                       "</style></head><body><div><h1>" & WebSafe(title) & "</h1><p>" & WebSafe(message) & "</p></div></body></html>"
            Dim bytes = Text.Encoding.UTF8.GetBytes(html)
            ctx.Response.ContentType = "text/html; charset=utf-8"
            ctx.Response.ContentLength64 = bytes.Length
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length)
            ctx.Response.OutputStream.Close()
        Catch
        End Try
    End Sub

    Private Function WebSafe(s As String) As String
        If s Is Nothing Then Return ""
        Return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
    End Function

#Region "Xbox → Minecraft"

    ''' <summary>第 2~6 步：微软令牌 → XBL → XSTS → Minecraft 令牌 → 玩家档案。</summary>
    Public Function ToMinecraft(msToken As String, refreshToken As String, Optional status As Action(Of String) = Nothing) As MsAccount
        ' 2. XBL
        Dim rps = If(msToken.StartsWith("d=", StringComparison.Ordinal), msToken, "d=" & msToken)
        Dim xblBody = "{""Properties"":{""AuthMethod"":""RPS"",""SiteName"":""user.auth.xboxlive.com"",""RpsTicket"":""" &
                      JsonEscape(rps) & """},""RelyingParty"":""http://auth.xboxlive.com"",""TokenType"":""JWT""}"
        Dim xblJson = PostJson("https://user.auth.xboxlive.com/user/authenticate", xblBody)
        Dim xblToken As String
        Using doc = JsonDocument.Parse(xblJson)
            xblToken = GetStr(doc.RootElement, "Token")
        End Using
        If String.IsNullOrEmpty(xblToken) Then Throw New Exception("Xbox 验证失败（第 2 步）")

        ' 3. XSTS
        status?.Invoke("正在验证 Xbox 权限…")
        Dim xstsBody = "{""Properties"":{""SandboxId"":""RETAIL"",""UserTokens"":[""" & JsonEscape(xblToken) &
                       """]},""RelyingParty"":""rp://api.minecraftservices.com/"",""TokenType"":""JWT""}"
        Dim xstsJson As String
        Try
            xstsJson = PostJson("https://xsts.auth.xboxlive.com/xsts/authorize", xstsBody)
        Catch ex As Exception
            Dim m = ex.Message
            If m.Contains("2148916233") Then Throw New Exception("这个微软账号还没有注册 Xbox 账号，请先到 xbox.com 注册后再登录。")
            If m.Contains("2148916235") Then Throw New Exception("你所在的地区无法使用 Xbox 登录，请更换网络后再试。")
            If m.Contains("2148916238") Then Throw New Exception("账号年龄不足，请先在微软账号页把出生日期改为 18 岁以上，等一分钟后重试。")
            If m.Contains("2148916227") Then Throw New Exception("该账号已被微软封禁，无法登录。")
            Throw
        End Try
        Dim xstsToken As String = ""
        Dim uhs As String = ""
        Using doc = JsonDocument.Parse(xstsJson)
            xstsToken = GetStr(doc.RootElement, "Token")
            Dim dc As JsonElement
            If doc.RootElement.TryGetProperty("DisplayClaims", dc) Then
                Dim xui As JsonElement
                If dc.TryGetProperty("xui", xui) AndAlso xui.ValueKind = JsonValueKind.Array Then
                    Dim first = xui.EnumerateArray().FirstOrDefault()
                    If first.ValueKind = JsonValueKind.Object Then uhs = GetStr(first, "uhs")
                End If
            End If
        End Using
        If String.IsNullOrEmpty(xstsToken) OrElse String.IsNullOrEmpty(uhs) Then Throw New Exception("Xbox 权限验证失败（第 3 步）")

        ' 4. Minecraft 令牌
        status?.Invoke("正在获取 Minecraft 令牌…")
        Dim mcBody = "{""identityToken"":""XBL3.0 x=" & JsonEscape(uhs) & ";" & JsonEscape(xstsToken) & """}"
        Dim mcJson As String
        Try
            mcJson = PostJson("https://api.minecraftservices.com/authentication/login_with_xbox", mcBody)
        Catch ex As Exception
            Dim m = ex.Message
            If m.Contains("Invalid app registration") Then
                Throw New Exception("启动器的应用身份未通过 Mojang 审核，无法登录正版。这与你的网络无关。")
            End If
            If m.Contains("429") Then Throw New Exception("登录尝试太频繁，请等几分钟再试。")
            Throw New Exception("获取 Minecraft 令牌失败：" & m)
        End Try
        Dim mcToken As String = ""
        Dim expiresIn As Integer = 86400
        Using doc = JsonDocument.Parse(mcJson)
            mcToken = GetStr(doc.RootElement, "access_token")
            Dim e As JsonElement
            If doc.RootElement.TryGetProperty("expires_in", e) AndAlso e.ValueKind = JsonValueKind.Number Then expiresIn = e.GetInt32()
        End Using
        If String.IsNullOrEmpty(mcToken) Then Throw New Exception("获取 Minecraft 令牌失败（第 4 步）")

        ' 5. 是否拥有游戏
        status?.Invoke("正在检查游戏所有权…")
        Try
            Dim ent = GetJson("https://api.minecraftservices.com/entitlements/mcstore", mcToken)
            Using doc = JsonDocument.Parse(ent)
                Dim items As JsonElement
                Dim has = False
                If doc.RootElement.TryGetProperty("items", items) AndAlso items.ValueKind = JsonValueKind.Array Then
                    has = items.GetArrayLength() > 0
                End If
                If Not has Then Throw New Exception("这个账号没有购买 Minecraft（或 Xbox Game Pass 已到期），无法登录正版。")
            End Using
        Catch ex As Exception
            If ex.Message.Contains("没有购买") Then Throw
            ModLog.Err(ex, "查询游戏所有权失败（不阻断登录）")
        End Try

        ' 6. 玩家档案
        status?.Invoke("正在读取玩家档案…")
        Dim acc As New MsAccount With {
            .AccessToken = mcToken,
            .RefreshToken = refreshToken,
            .ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn - 3600
        }
        Try
            Dim prof = GetJson("https://api.minecraftservices.com/minecraft/profile", mcToken)
            Using doc = JsonDocument.Parse(prof)
                acc.Name = GetStr(doc.RootElement, "name")
                acc.Uuid = GetStr(doc.RootElement, "id")
            End Using
        Catch ex As Exception
            ModLog.Err(ex, "读取玩家档案失败")
        End Try
        If String.IsNullOrEmpty(acc.Name) Then Throw New Exception("读取玩家档案失败，可能该账号没有 Minecraft Java 版档案。")
        Return acc
    End Function

#End Region

#Region "HTTP"

    Private Function PostForm(url As String, body As String) As String
        Using req As New HttpRequestMessage(HttpMethod.Post, url)
            req.Headers.UserAgent.ParseAdd("TYWLauncher/1.0 (Linux)")
            req.Content = New StringContent(body, Text.Encoding.UTF8, "application/x-www-form-urlencoded")
            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(30))
                Using resp = ModDownload.Client.Send(req, cts.Token)
                    Dim text = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult()
                    If Not resp.IsSuccessStatusCode Then
                        Throw New Exception("HTTP " & CInt(resp.StatusCode) & "：" & Trim(text, 400))
                    End If
                    Return text
                End Using
            End Using
        End Using
    End Function

    Private Function PostJson(url As String, body As String) As String
        Using req As New HttpRequestMessage(HttpMethod.Post, url)
            req.Headers.UserAgent.ParseAdd("TYWLauncher/1.0 (Linux)")
            req.Content = New StringContent(body, Text.Encoding.UTF8, "application/json")
            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(30))
                Using resp = ModDownload.Client.Send(req, cts.Token)
                    Dim text = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult()
                    If Not resp.IsSuccessStatusCode Then
                        Throw New Exception("HTTP " & CInt(resp.StatusCode) & "：" & Trim(text, 400))
                    End If
                    Return text
                End Using
            End Using
        End Using
    End Function

    Private Function GetJson(url As String, bearer As String) As String
        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            req.Headers.UserAgent.ParseAdd("TYWLauncher/1.0 (Linux)")
            req.Headers.Authorization = New Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer)
            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(30))
                Using resp = ModDownload.Client.Send(req, cts.Token)
                    Dim text = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult()
                    If Not resp.IsSuccessStatusCode Then
                        Throw New Exception("HTTP " & CInt(resp.StatusCode) & "：" & Trim(text, 400))
                    End If
                    Return text
                End Using
            End Using
        End Using
    End Function

    Private Function Trim(s As String, n As Integer) As String
        If String.IsNullOrEmpty(s) Then Return ""
        Return If(s.Length > n, s.Substring(0, n), s)
    End Function

    Private Function GetStr(el As JsonElement, name As String) As String
        Dim v As JsonElement
        If el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        Return ""
    End Function

    Private Function JsonEscape(s As String) As String
        If s Is Nothing Then Return ""
        Return s.Replace("\", "\\").Replace("""", "\""")
    End Function

#End Region

End Module
