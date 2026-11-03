Imports System.Net
Imports System.Net.Http

''' <summary>
''' 网络访问。对应 Windows 版 ModNet 的 ServerRequest / NetGet。
''' </summary>
Public Module ModNet

    Public ReadOnly ServerAddress As String = "http://Launcher.mc.tywmc.cn:17403/"

    Private ReadOnly Client As HttpClient = CreateClient()

    ''' <summary>POST /report 专用客户端（明文，不加密）。</summary>
    Public ReadOnly PostClient As HttpClient = New HttpClient(New HttpClientHandler()) With
        {.Timeout = TimeSpan.FromSeconds(15)}

    Private Function CreateClient() As HttpClient
        Try
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12
            ServicePointManager.Expect100Continue = False
        Catch
        End Try
        Dim h As New HttpClientHandler With {.AutomaticDecompression = DecompressionMethods.GZip}
        Dim c As New HttpClient(h) With {.Timeout = TimeSpan.FromSeconds(12)}
        Try
            c.DefaultRequestHeaders.UserAgent.ParseAdd("TYWLauncher/Linux/" & ModApp.VersionBaseName)
        Catch
        End Try
        Return c
    End Function

    ''' <summary>请求服务端接口（自动带加密协商头），返回明文文本。失败返回 Nothing。</summary>
    Public Function GetServer(path As String, Optional timeoutSec As Integer = 12) As String
        Try
            Dim url = If(path.StartsWith("http"), path, ServerAddress & path.TrimStart("/"c))
            Using req As New HttpRequestMessage(HttpMethod.Get, url)
                Dim added As Boolean = req.Headers.TryAddWithoutValidation("X-TYW-Enc", "1")
                If Not added Then ModLog.Warn("无法附加加密协商头")
                Using cts = New Threading.CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec))
                    Using resp = Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).GetAwaiter().GetResult()
                        If Not resp.IsSuccessStatusCode Then Return Nothing
                        Dim bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                        Dim plain = ModCrypto.Decrypt(bytes)
                        If plain IsNot Nothing Then Return Text.Encoding.UTF8.GetString(plain)
                        Return Text.Encoding.UTF8.GetString(bytes)
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ModLog.Err(ex, "请求 " & path)
            Return Nothing
        End Try
    End Function

    ''' <summary>请求服务端文本，失败抛异常（供需要区分网络错误的调用方）。</summary>
    Public Function GetServerStrict(path As String, Optional timeoutSec As Integer = 12) As String
        Dim url = If(path.StartsWith("http"), path, ServerAddress & path.TrimStart("/"c))
        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            req.Headers.TryAddWithoutValidation("X-TYW-Enc", "1")
            Using cts = New Threading.CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec))
                Using resp = Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).GetAwaiter().GetResult()
                    If Not resp.IsSuccessStatusCode Then Throw New Exception("服务端返回错误码 " & CInt(resp.StatusCode) & "（" & url & "）")
                    Dim bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                    Dim plain = ModCrypto.Decrypt(bytes)
                    If plain IsNot Nothing Then Return Text.Encoding.UTF8.GetString(plain)
                    Return Text.Encoding.UTF8.GetString(bytes)
                End Using
            End Using
        End Using
    End Function

    ''' <summary>下载文件到本地路径（超时 120 秒，支持进度回调）。</summary>
    Public Function DownloadFile(url As String, localPath As String, Optional progress As Action(Of Double) = Nothing) As Boolean
        Try
            Using resp = Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult()
                resp.EnsureSuccessStatusCode()
                Dim total = If(resp.Content.Headers.ContentLength, -1)
                Using src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                    Using dst = File.Create(localPath)
                        Dim buf(65535) As Byte
                        Dim done As Long = 0
                        Dim read = src.Read(buf, 0, buf.Length)
                        While read > 0
                            dst.Write(buf, 0, read)
                            done += read
                            If total > 0 AndAlso progress IsNot Nothing Then progress(done / total)
                            read = src.Read(buf, 0, buf.Length)
                        End While
                    End Using
                End Using
            End Using
            Return True
        Catch ex As Exception
            ModLog.Err(ex, "下载 " & url)
            Try
                If File.Exists(localPath) Then File.Delete(localPath)
            Catch
            End Try
            Return False
        End Try
    End Function

    Public Function GetUrl(url As String) As String
        Try
            Return Client.GetStringAsync(url).GetAwaiter().GetResult()
        Catch ex As Exception
            ModLog.Err(ex, "请求 " & url)
            Return Nothing
        End Try
    End Function

End Module
