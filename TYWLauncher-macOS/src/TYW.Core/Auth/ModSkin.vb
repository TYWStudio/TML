Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Threading

''' <summary>
''' 玩家皮肤获取（对应 Windows 版 ModLink + MySkin 的头像逻辑）：
''' 正版用 UUID 从 Mojang 会话服务器拿贴图地址，离线用玩家名查 UUID。
''' 下载后缓存到 Cache/Skins/{uuid}.png，界面裁 (8,8,8,8) 正脸 + (40,8,8,8) 帽子层。
''' </summary>
Public Module ModSkin

    ''' <summary>本地缓存的皮肤贴图路径。</summary>
    Public Function SkinPath(uuid As String) As String
        Return Path.Combine(ModPaths.CacheDir, "Skins", uuid.ToLower & ".png")
    End Function

    ''' <summary>
    ''' 按 UUID 拉取皮肤（有缓存直接用缓存）。失败返回空字符串，不抛异常。
    ''' </summary>
    Public Function FetchByUuid(uuid As String) As String
        If String.IsNullOrWhiteSpace(uuid) Then Return ""
        Dim dest = SkinPath(uuid)
        If File.Exists(dest) Then Return dest
        Try
            Dim json = GetJson("https://sessionserver.mojang.com/session/minecraft/profile/" & uuid.ToLower)
            Dim url = SkinUrlOf(json)
            If url = "" Then Return ""
            Return Download(url, dest)
        Catch ex As Exception
            ModLog.Err(ex, "拉取皮肤（UUID）")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 按玩家名拉取皮肤（离线登录用：名字 → UUID → 皮肤）。
    ''' 正版库里没有这个名字就返回空。
    ''' </summary>
    Public Function FetchByName(name As String) As String
        If String.IsNullOrWhiteSpace(name) Then Return ""
        Try
            Dim json = GetJson("https://api.mojang.com/users/profiles/minecraft/" & Uri.EscapeDataString(name))
            Dim uuid As String = ""
            Using doc = JsonDocument.Parse(json)
                uuid = GetStr(doc.RootElement, "id")
            End Using
            If uuid = "" Then Return ""
            Return FetchByUuid(uuid)
        Catch ex As Exception
            ModLog.Err(ex, "拉取皮肤（名字）")
            Return ""
        End Try
    End Function

    ''' <summary>删掉某个 UUID 的缓存（退出登录 / 换皮肤后刷新）。</summary>
    Public Sub Invalidate(uuid As String)
        Try
            If String.IsNullOrEmpty(uuid) Then Return
            Dim p = SkinPath(uuid)
            If File.Exists(p) Then File.Delete(p)
        Catch
        End Try
    End Sub

    ''' <summary>从 profile JSON（properties[0].value 是 base64 的贴图声明）解析 SKIN 地址。</summary>
    Private Function SkinUrlOf(profileJson As String) As String
        Using doc = JsonDocument.Parse(profileJson)
            Dim props As JsonElement
            If Not doc.RootElement.TryGetProperty("properties", props) OrElse props.GetArrayLength() = 0 Then Return ""
            Dim encoded = GetStr(props(0), "value")
            If encoded = "" Then Return ""
            Dim decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded))
            Using tex = JsonDocument.Parse(decoded)
                Dim t As JsonElement
                If Not tex.RootElement.TryGetProperty("textures", t) Then Return ""
                Dim skin As JsonElement
                If Not t.TryGetProperty("SKIN", skin) Then Return ""
                Return GetStr(skin, "url")
            End Using
        End Using
    End Function

    Private Function Download(url As String, dest As String) As String
        Directory.CreateDirectory(Path.GetDirectoryName(dest))
        Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(30))
            Using resp = ModDownload.Client.GetAsync(url, cts.Token).GetAwaiter().GetResult()
                resp.EnsureSuccessStatusCode()
                Dim bytes = resp.Content.ReadAsByteArrayAsync(cts.Token).GetAwaiter().GetResult()
                '简单校验是 PNG 且尺寸合理（64x64 / 64x32）
                If bytes.Length < 100 OrElse bytes(0) <> &H89 OrElse bytes(1) <> &H50 Then Return ""
                File.WriteAllBytes(dest, bytes)
                Return dest
            End Using
        End Using
    End Function

    Private Function GetJson(url As String) As String
        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            req.Headers.UserAgent.ParseAdd("TYWLauncher/1.0 (Linux)")
            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(20))
                Using resp = ModDownload.Client.Send(req, cts.Token)
                    Return resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult()
                End Using
            End Using
        End Using
    End Function

    Private Function GetStr(el As JsonElement, name As String) As String
        Dim v As JsonElement
        If el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return v.GetString()
        Return ""
    End Function

End Module
