'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 TYW Launcher 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "OpenSource"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLDebug"
    '★ 密钥一律不进源码 ★
    '下面三个常量在开源仓库里恒为空串。官方发行包由打包脚本 / GitHub Actions 在编译期
    '把值填进来（scripts/inject_secrets.py，读环境变量或 _deploy/*.txt），构建完立即还原，
    '所以仓库里永远看不到真实值。运行时的同名环境变量优先级更高，方便本机调试。
    '   PCL_MS_CLIENT_ID         → BuiltInOAuthClientId（微软登录 ClientId）
    '   PCL_CURSEFORGE_API_KEY   → BuiltInCurseForgeKey（CurseForge API Key）
    '   TYW_CLS_URL              → ClsBaseUrl（匿名上报地址）
    Public Const BuiltInOAuthClientId As String = ""
    Public Const BuiltInCurseForgeKey As String = ""
    '用于微软登录的 ClientId
    Public OAuthClientId As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), BuiltInOAuthClientId)
    'CurseForge API Key
    Public CurseForgeAPIKey As String = If(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), BuiltInCurseForgeKey)
    'TYW Launcher 自建服务端地址（公告 / 启动器更新下载，须以 / 结尾）
    '
    '★ 开源版默认留空 ★ 仓库里不带任何服务器地址，所有服务端功能（公告、自动更新、
    '   内测码校验、封禁查询、匿名上报）在地址为空时自动关闭，启动器纯本地运行。
    '
    '想接到自己的服务端，任选一种（优先级从高到低）：
    '   1) 设置里写 ServerUrl（启动器设置 → 启动器 → 服务端地址）
    '   2) 环境变量 TYW_SERVER
    '   3) 启动器目录或缓存目录下的 server.txt，内容就一行地址
    '   4) 编译期常量（打包脚本用 TYW_SERVER_URL 注入）
    Public ReadOnly Property ServerAddress As String
        Get
            If _serverAddress IsNot Nothing Then Return _serverAddress
            _serverAddress = ResolveServerAddress()
            Return _serverAddress
        End Get
    End Property
    Private _serverAddress As String = Nothing

    Private Function ResolveServerAddress() As String
        Try
            Dim custom As String = ""
            Try
                custom = Settings.Get(Of String)("ServerUrl")
            Catch
            End Try
            If String.IsNullOrWhiteSpace(custom) Then custom = Environment.GetEnvironmentVariable("TYW_SERVER")
            If String.IsNullOrWhiteSpace(custom) Then
                For Each Candidate In {IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server.txt"),
                                       IO.Path.Combine(PathTemp, "server.txt")}
                    Try
                        If IO.File.Exists(Candidate) Then
                            custom = IO.File.ReadAllText(Candidate).Trim()
                            If custom <> "" Then Exit For
                        End If
                    Catch
                    End Try
                Next
            End If
            If String.IsNullOrWhiteSpace(custom) Then custom = BuiltInServerAddress
            custom = If(custom, "").Trim()
            If custom = "" Then Return ""
            If Not custom.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then custom = "http://" & custom
            If Not custom.EndsWith("/") Then custom &= "/"
            Return custom
        Catch ex As Exception
            Return ""
        End Try
    End Function

    ''' <summary>编译期注入的默认地址。打包脚本（_deploy/build.py，环境变量 TYW_SERVER_URL）会替换这一行；
    ''' 开源仓库里恒为空。</summary>
    Public Const BuiltInServerAddress As String = ""
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

#Region "网络鉴权"

    Public Const RestrictMcDownload As Boolean = False
    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    Public ColorHue As Integer = 210, ColorSat As Integer = 85, ColorLightAdjust As Integer = 0, ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If NewTheme >= 0 Then ThemeNow = NewTheme
            '根据主题编号确定颜色参数（开源版缺失的映射，此处补全）
            If ThemeNow < 0 Then ThemeNow = Settings.Get(Of Integer)("UiLauncherTheme")
            Select Case ThemeNow
                Case 0 '龙猫蓝
                    ColorHue = 210 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                Case 1 '甜柠青
                    ColorHue = 150 : ColorSat = 65 : ColorLightAdjust = 8 : ColorHueTopbarDelta = 0
                Case 2 '小草绿
                    ColorHue = 105 : ColorSat = 70 : ColorLightAdjust = 5 : ColorHueTopbarDelta = 0
                Case 3 '菠萝黄
                    ColorHue = 48 : ColorSat = 85 : ColorLightAdjust = 6 : ColorHueTopbarDelta = 0
                Case 4 '橡木棕
                    ColorHue = 25 : ColorSat = 45 : ColorLightAdjust = 2 : ColorHueTopbarDelta = 0
                Case 5 '玄素黑
                    ColorHue = 210 : ColorSat = 0 : ColorLightAdjust = -2 : ColorHueTopbarDelta = 0
                Case 6 '铁杆粉
                    ColorHue = 330 : ColorSat = 80 : ColorLightAdjust = 6 : ColorHueTopbarDelta = 0
                Case 7 '神秘紫
                    ColorHue = 270 : ColorSat = 70 : ColorLightAdjust = 5 : ColorHueTopbarDelta = 0
                Case 8 '秋仪金
                    ColorHue = 40 : ColorSat = 90 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                Case 9 '活跃橙
                    ColorHue = 25 : ColorSat = 95 : ColorLightAdjust = 5 : ColorHueTopbarDelta = 0
                Case 10 '跳票红
                    ColorHue = 350 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
                Case 11 '极客蓝
                    ColorHue = 205 : ColorSat = 90 : ColorLightAdjust = -3 : ColorHueTopbarDelta = 0
                Case 12, 13 '滑稽彩 / 欧皇彩（每次刷新随机 hue，顶栏彩虹渐变）
                    ColorHue = RandomInteger(0, 359)
                    ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 120
                Case 14 '自定义
                    ColorHue = Settings.Get(Of Integer)("UiLauncherHue")
                    ColorSat = Settings.Get(Of Integer)("UiLauncherSat")
                    ColorLightAdjust = Settings.Get(Of Integer)("UiLauncherLight")
                    ColorHueTopbarDelta = Settings.Get(Of Integer)("UiLauncherDelta")
            End Select

            Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
            Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
            Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
            Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
            Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
            Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
            Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
            Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
            ColorBg1 = New MyColor(190, Color7)

            ColorSemiTransparent = New MyColor(1, Color8)
            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '顶部条背景
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat, 48 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat, 54 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat, 48 + ColorLightAdjust)})
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
        '解锁全部主题：启用所有默认隐藏的主题选项，并隐藏覆盖在上面的“解锁”遮罩
        Try
            If IsNothing(FrmSetupUI) Then Return
            FrmSetupUI.RadioLauncherTheme5.IsEnabled = True
            FrmSetupUI.RadioLauncherTheme5.Opacity = 1
            FrmSetupUI.RadioLauncherTheme5Gray.Visibility = Visibility.Collapsed
            FrmSetupUI.RadioLauncherTheme6.IsEnabled = True
            FrmSetupUI.RadioLauncherTheme7.IsEnabled = True
            FrmSetupUI.RadioLauncherTheme8.IsEnabled = True
            FrmSetupUI.RadioLauncherTheme9.IsEnabled = True
            FrmSetupUI.RadioLauncherTheme10.IsEnabled = True
            FrmSetupUI.RadioLauncherTheme11.IsEnabled = True
            FrmSetupUI.RadioLauncherTheme12.IsEnabled = True
            FrmSetupUI.RadioLauncherTheme13.IsEnabled = True
            FrmSetupUI.RadioLauncherTheme14.IsEnabled = True
            '隐藏遮罩层：这些 TextBlock 会盖在主题单选框上面，拦截点击，看起来就像“需要解锁”
            FrmSetupUI.LabLauncherTheme5Unlock.Visibility = Visibility.Collapsed
            FrmSetupUI.LabLauncherTheme8Copy.Visibility = Visibility.Collapsed
            FrmSetupUI.LabLauncherTheme9Copy.Visibility = Visibility.Collapsed
            FrmSetupUI.LabLauncherTheme11Click.Visibility = Visibility.Collapsed
            '去掉“需要解锁”的悬浮提示
            FrmSetupUI.RadioLauncherTheme8.ToolTip = Nothing
            FrmSetupUI.RadioLauncherTheme9.ToolTip = Nothing
            FrmSetupUI.RadioLauncherTheme14.ToolTip = Nothing
            FrmSetupUI.LabLauncherTheme8Copy.ToolTip = Nothing
            FrmSetupUI.LabLauncherTheme9Copy.ToolTip = Nothing
        Catch ex As Exception
            Logger.Error(ex, "解锁主题失败")
        End Try
    End Sub
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        Return True
    End Function
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        '个性化（含隐藏主题）已经全部直接可用，不存在"刚刚解锁"这件事，
        '因此这里恒返回 False：调用方的「xxx 已解锁！」弹窗不会再反复出现。
        Return False
    End Function

#End Region

#Region "更新"

    ''' <summary>本编译是否为内测版（内测版不走任何更新检查，且必须联网并连上服务端）。</summary>
    Friend ReadOnly Property IsBetaChannel As Boolean
        Get
            Return BuildType = BuildTypes.Beta
        End Get
    End Property

    ''' <summary>
    ''' 当前生效的更新通道：True = 快照通道。
    ''' ★ 只看「快照版测试功能」开关：内测版固定为内测通道；
    '''   快照版关闭该开关后应当回到正式通道（走正式版 exe），而不是继续留在快照通道。
    ''' </summary>
    Friend ReadOnly Property IsSnapshotChannel As Boolean
        Get
            If IsBetaChannel Then Return False
            Return Settings.Get(Of Boolean)("SystemSnapshotTest")
        End Get
    End Property

    ''' <summary>当前通道名（用于提示与上报）。</summary>
    Friend ReadOnly Property ChannelName As String
        Get
            If IsBetaChannel Then Return "内测版"
            Return If(IsSnapshotChannel, "快照版", "正式版")
        End Get
    End Property

    ''' <summary>当前更新通道对应的启动器文件名（服务端 www 中的文件）。</summary>
    Friend ReadOnly Property ChannelExeName As String
        Get
            If IsBetaChannel Then Return "TYW Launcher Beta.exe"
            Return If(IsSnapshotChannel, "TYW Launcher Snapshot.exe", "TYW Launcher.exe")
        End Get
    End Property

    ''' <summary>
    ''' 当前通道在 Notice.cfg 中的版本码列号。
    ''' 格式：标题|快照码|正式码|正式公告|快照公告|内测码|内测公告
    ''' （旧版 5 段格式仍然兼容，内测码缺失时回退到正式码）
    ''' </summary>
    Friend ReadOnly Property ChannelCodeIndex As Integer
        Get
            If IsBetachannel Then Return 5
            Return If(IsSnapshotChannel, 1, 2)
        End Get
    End Property

    ''' <summary>
    ''' 读取服务端公告（Notice.cfg）。
    ''' 新格式：标题|快照版版本号|正式版版本号|正式版公告|快照版公告|内测版版本号|内测版公告（换行写 \n）。
    ''' 旧格式只有前 5 段，仍然可用。
    ''' </summary>
    Friend Function ReadServerNotice(Optional TimeoutMs As Integer = 10000) As String()
        Dim Content As String = ServerRequest(ServerAddress & "Notice.cfg", TimeoutMs)
        Dim Segments = Content.Split("|")
        If Segments.Length < 3 Then Throw New Exception("服务端 Notice.cfg 格式错误（需要至少 3 段）")
        Return Segments
    End Function

    ''' <summary>
    ''' 启动时向服务端查询本设备是否被封禁（GET /ban?ident=设备码）。
    ''' 被封禁返回封禁原因；未封禁返回空字符串；网络失败等异常返回 Nothing（放行，避免断网无法使用）。
    ''' </summary>
    Friend Function CheckDeviceBanned() As String
        Try
            Dim Url As String = ServerAddress & "ban?ident=" & Uri.EscapeDataString(DeviceCode)
            Dim Raw As String = ServerRequest(Url, 8000)
            Dim Json As Newtonsoft.Json.Linq.JObject = Newtonsoft.Json.Linq.JObject.Parse(Raw)
            If Json("banned") IsNot Nothing AndAlso CBool(Json("banned")) Then
                Return If(Json("reason") Is Nothing, "", Json("reason").ToString)
            End If
        Catch ex As Exception
            Logger.Info("封禁状态查询失败（已放行）：" & ex.Message)
        End Try
        Return Nothing
    End Function

    ''' <summary>
    ''' 取当前通道对应的服务端公告：第 4 段正式版，第 5 段快照版，第 7 段内测版。
    ''' 本通道没有单独写公告时回退到正式版公告；都没有则返回空字符串。
    ''' 与客户端内置的更新日志（ModUpdateLog）完全独立。
    ''' </summary>
    Friend Function GetChannelNotice(Segments As String()) As String
        Dim Index As Integer = If(IsBetaChannel, 6, If(IsSnapshotChannel, 4, 3))
        If Segments.Length <= Index OrElse Segments(Index) = "" Then Index = 3
        If Segments.Length <= Index Then Return ""
        Return If(Segments(Index), "").Replace("\n", vbCrLf)
    End Function

    ''' <summary>
    ''' 取「更新详情」：检查更新对话框里显示的本次更新内容，与启动弹窗的公告完全分开。
    ''' Notice.cfg 第 8/9/10 段 = 正式/快照/内测更新详情；本通道留空时回退到启动器内置更新日志。
    ''' </summary>
    Friend Function GetChannelUpdateDetail(Segments As String(), LatestCode As Integer) As String
        Dim Index As Integer = If(IsBetaChannel, 9, If(IsSnapshotChannel, 8, 7))
        Dim Text As String = ""
        If Segments.Length > Index Then Text = If(Segments(Index), "").Replace("\n", vbCrLf)
        If Text.Trim <> "" Then Return Text
        '回退：启动器内置更新日志（发布新版本时记得在 ModUpdateLog 里登记）
        Try
            Dim Builtin = ModUpdateLog.GetUpdateLog(LatestCode)
            If Builtin.Item2 IsNot Nothing AndAlso Builtin.Item2.Length > 0 Then
                Return Builtin.Item1 & " 更新内容：" & vbCrLf & "· " & String.Join(vbCrLf & "· ", Builtin.Item2)
            End If
        Catch ex As Exception
            Logger.Info("读取内置更新日志失败：" & ex.Message)
        End Try
        Return ""
    End Function

    ''' <summary>
    ''' 判断公告是否只是「X.X.X 已发布，更新内容请在检查更新里查看」这类例行发布通知。
    ''' 这类通知与启动器内置更新日志（更新后首次启动自动弹）完全重复，不弹，交给本地更新日志。
    ''' 真正的活动、维护等公告不受影响，照常弹出。
    ''' </summary>
    Friend Function IsGenericReleaseNotice(Text As String) As Boolean
        Return Text.Contains("已发布") AndAlso Text.Contains("检查更新")
    End Function

    ''' <summary>取指定通道的版本码，缺段时回退到正式版码。</summary>
    Friend Function GetChannelCode(Segments As String(), Channel As String) As Integer
        Dim Index As Integer
        Select Case Channel
            Case "Snapshot" : Index = 1
            Case "Beta" : Index = 5
            Case Else : Index = 2
        End Select
        Dim Raw As String = ""
        If Segments.Length > Index Then Raw = If(Segments(Index), "")
        If Raw.Trim = "" Then Raw = If(Segments.Length > 2, Segments(2), "")
        Dim Value As Integer
        Return If(Integer.TryParse(Raw.Trim, Value), Value, 0)
    End Function

    ''' <summary>
    ''' 同一条服务端公告只自动弹出一次：把上次弹过的「通道 + 标题 + 正文」指纹记到注册表，
    ''' 内容没变就不再弹；服务端改了公告（内容变化）才会再弹一次。
    ''' 用注册表而不是临时目录，避免 TEMP 被清理后又弹一遍。
    ''' </summary>
    Friend Function ShouldShowNotice(Title As String, NoticeText As String) As Boolean
        Try
            Dim Raw As String = (If(IsSnapshotChannel, "S", "R")) & "|" & Title & "|" & NoticeText
            Dim Hash As String
            Using Md5 As System.Security.Cryptography.MD5 = System.Security.Cryptography.MD5.Create()
                Hash = BitConverter.ToString(Md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Raw))).Replace("-", "")
            End Using
            Using Key As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\" & RegFolder)
                Dim Last As String = TryCast(Key.GetValue("NoticeShownHash"), String)
                If Last = Hash Then Return False '同一条公告已经弹过了
                Key.SetValue("NoticeShownHash", Hash, Microsoft.Win32.RegistryValueKind.String)
            End Using
            Return True
        Catch ex As Exception
            '记录失败不影响正常使用，最多是多弹一次
            Logger.Warn(ex, "记录公告显示状态失败")
            Return True
        End Try
    End Function

    Friend Sub UpdateCheckByButton()
        '内测版不提供检查更新：内测版只跟随服务端人工下发的内测包
        If IsBetaChannel Then
            MyMsgBox("内测版不提供检查更新。" & vbCrLf &
                     "内测版由服务端人工发布，需要更新时请等待服务端下发新版本。" & vbCrLf & vbCrLf &
                     "如果想回到正式版，请在 设置 - 其他 中取消「内测版体验」。", "内测版")
            Return
        End If
        Try
            Hint("正在检查启动器更新……")
            Dim Segments = ReadServerNotice()
            Dim LatestCode As Integer = Segments(ChannelCodeIndex)
            Dim LatestName As String = Segments(0)
            '更新详情：优先用服务端「更新详情」（第 8/9/10 段），留空回退到启动器内置更新日志
            '注意这里不用 GetChannelNotice —— 公告是启动弹窗用的，跟更新详情是两回事
            Dim NoticeText As String = GetChannelUpdateDetail(Segments, LatestCode)
            If LatestCode <= VersionCode Then
                MyMsgBox("当前已经是最新版本！" & vbCrLf & "当前版本：" & VersionBaseName & "（" & ChannelName & "通道）", "更新检查")
                Return
            End If
            If MyMsgBox($"发现新版本：{LatestName}{vbCrLf}当前版本：{VersionBaseName}{If(NoticeText = "", "", vbCrLf & vbCrLf & NoticeText)}{vbCrLf}{vbCrLf}是否立即下载并更新？",
                        "启动器更新", "更新", "取消") = 2 Then Return
            Hint("正在下载更新，进度见右下角，完成后会自动替换重启……", HintType.Blue)
            '★ 必须切到工作线程下载：WaitForExit 阻塞的是当前线程，放在按钮的 UI 线程会导致窗口长时间未响应
            RunInNewThread(AddressOf UpdateDownloadAndRestart, "启动器更新下载")
        Catch ex As Exception
            '没网 / 连不上服务器 不算错误：不弹框、不上报，只在左下角提示
            If IsNetworkIssue(ex) Then
                HintNetworkIssue()
                Return
            End If
            MyMsgBox("更新检查失败：" & ex.Message & vbCrLf & "请检查服务端是否正常运行（" & ServerAddress & "）", "启动器更新失败")
        End Try
    End Sub

    ''' <summary>从服务端下载当前通道的最新版启动器，完成后自动替换重启（右下角显示下载进度）。
    ''' ★ 必须在工作线程调用（内部 WaitForExit 阻塞），否则会卡死 UI 线程。</summary>
    Private Sub UpdateDownloadAndRestart()
        Dim UpdatePath As String = PathTemp & "TYW Launcher\Update.exe"
        FileUtils.Delete(UpdatePath)
        Dim DlLoader As New LoaderDownload("下载启动器更新",
            New List(Of NetFile) From {New NetFile({ServerAddress & ChannelExeName}, UpdatePath)})
        Dim Combo As New LoaderCombo(Of Integer)("下载启动器更新", {DlLoader})
        Combo.Start()
        LoaderTaskbarAdd(Combo)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        Combo.WaitForExit()
        '下载被取消或失败：不替换程序，启动器继续运行
        If Combo.State <> LoadState.Finished Then
            Logger.Warn($"更新下载未完成（{Combo.State}），已放弃替换，启动器继续运行")
            FileUtils.Delete(UpdatePath)
            Hint("更新下载未完成，启动器保持当前版本", HintType.Blue, True)
            Return
        End If
        '兜底校验：必须是完整下载的 exe（大小合理且带 MZ 头），否则不替换
        Dim Size As Long = 0
        Try
            If IO.File.Exists(UpdatePath) Then Size = New IO.FileInfo(UpdatePath).Length
        Catch
        End Try
        Dim IsExe As Boolean = False
        If Size > 2 * 1024 * 1024 Then
            Try
                Dim Head(1) As Byte
                Using Fs As IO.FileStream = IO.File.OpenRead(UpdatePath)
                    Fs.Read(Head, 0, 2)
                End Using
                IsExe = (Head(0) = &H4D AndAlso Head(1) = &H5A) 'MZ
            Catch
            End Try
        End If
        If Not IsExe Then
            Logger.Warn($"更新下载得到的文件异常（{Size} 字节），已放弃替换")
            FileUtils.Delete(UpdatePath)
            Hint("更新下载失败，启动器保持当前版本", HintType.Red, True)
            Return
        End If
        Hint("启动器更新下载完成，即将自动替换……", HintType.Green)
        '启动替换进程：--update 旧进程PID 旧文件 新文件 是否重启
        Dim NewProcess As New ProcessStartInfo(UpdatePath) With {
            .Arguments = $"--update {Process.GetCurrentProcess().Id} ""{PathExe}"" ""{UpdatePath}"" True",
            .WindowStyle = ProcessWindowStyle.Hidden,
            .UseShellExecute = False}
        Process.Start(NewProcess)
        FrmMain.EndProgram(False)
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    ''' <summary>
    ''' 由 --update 参数调用：等待旧进程退出后，用新文件替换旧文件并可选重启。
    ''' </summary>
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
        Try
            Dim OldProcess = Process.GetProcessById(ProcessId)
            If Not OldProcess.HasExited Then OldProcess.WaitForExit(30000)
        Catch
        End Try
        Thread.Sleep(500)
        FileUtils.Delete(OldFileName)
        FileUtils.Copy(NewFileName, OldFileName)
        If TriggerRestart Then Process.Start(OldFileName)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 TYW Launcher，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        Try
            If FileUtils.Exists(PathTemp & "Latest.exe") Then Return
            If String.IsNullOrWhiteSpace(ServerAddress) Then
                Throw New Exception("未配置服务端地址（设置 → 启动器 → 服务端地址，或程序目录下的 server.txt）")
            End If
            '★ LoaderDownload.Start(Input) 会把入参强制转成 IEnumerable(Of NetFile)。
            '   以前这里直接把外层的 LoaderTask(Of Integer, Integer) 传进去，
            '   导出整合包时必定抛 InvalidCastException（"无法将 LoaderTask`2 强制转换为 IEnumerable`1[NetFile]"）。
            '   与其它下载启动器的写法保持一致：用 LoaderCombo 包一层，不传 Input。
            Dim DlLoader As New LoaderDownload("下载 TYW Launcher 正式版",
                New List(Of NetFile) From {New NetFile({ServerAddress & "TYW Launcher.exe"}, PathTemp & "Latest.exe")})
            Dim Task As New LoaderCombo(Of Integer)("下载 TYW Launcher 正式版", {DlLoader})
            Task.WaitForExit(, LoaderToSyncProgress)
            If Task.State <> LoadState.Finished OrElse Not FileUtils.Exists(PathTemp & "Latest.exe") Then
                Throw New Exception("下载未完成或未得到文件")
            End If
        Catch ex As Exception
            Throw New Exception("从服务端下载 TYW Launcher 正式版失败（" & ServerAddress & "）：" & ex.Message, ex)
        End Try
    End Sub

#End Region

#Region "网络状态"

    '上一次网络提示的时间戳，避免同时多个请求失败时在左下角刷屏
    Private LastNetworkHintTick As Long = 0

    ''' <summary>当前是否有已连接的网络接口，用于区分“没网”和“连不上服务器”。</summary>
    Friend Function HasNetworkAvailable() As Boolean
        Try
            Return System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()
        Catch
            Return True
        End Try
    End Function

    ''' <summary>
    ''' 判断该异常/日志内容是否只是网络问题（没网、连不上服务器）。
    ''' 这类问题不算错误：不上报、不弹错误框，只在左下角提示。
    ''' </summary>
    Friend Function IsNetworkIssue(Ex As Exception, Optional Message As String = "") As Boolean
        If Ex IsNot Nothing Then
            Try
                If Ex.IsBadNetwork() Then Return True
            Catch
            End Try
        End If
        Dim Text As String = If(Message, "")
        If Ex IsNot Nothing Then Text &= " " & If(Ex.Message, "")
        For Each Keyword As String In {
                "网络请求失败", "连接服务器超时", "网络环境不佳", "无法连接到", "基础连接已经关闭",
                "操作超时", "连接超时", "远程服务器", "网络不可用", "NameResolutionFailure"}
            If Text.Contains(Keyword) Then Return True
        Next
        Return False
    End Function

    ''' <summary>在左下角提示网络问题：没网 / 连不上服务器。5 秒内只提示一次。</summary>
    Friend Sub HintNetworkIssue()
        Try
            If GetTimeMs() - LastNetworkHintTick < 5000 Then Return
            LastNetworkHintTick = GetTimeMs()
            If HasNetworkAvailable() Then
                Hint("连不上服务器，请稍后重试", HintType.Blue, True)
            Else
                Hint("没网，请检查网络连接", HintType.Blue, True)
            End If
        Catch
        End Try
    End Sub

#End Region

#Region "错误与反馈上报"

    '上报缓存的锁：避免多线程同时补报造成重复发送
    Private ReadOnly ReportLock As New Object

    ''' <summary>
    ''' 离线上报缓存目录：启动器所在目录\TYW Launcher\temp\。
    ''' 没网或连不上服务端时先存这里，联网后自动补报，上报成功后删除。
    ''' </summary>
    Private ReadOnly Property ReportCacheFolder As String
        Get
            Return IO.Path.GetDirectoryName(PathExe) & "\TYW Launcher\temp\"
        End Get
    End Property

    ''' <summary>把一条上报内容写入离线缓存。先落盘再发送，保证断网甚至崩溃时都不会丢。</summary>
    Private Sub SavePendingReport(Report As String)
        Try
            If Not IO.Directory.Exists(ReportCacheFolder) Then IO.Directory.CreateDirectory(ReportCacheFolder)
            Dim FileName As String = "report-" & Now.ToString("yyyyMMdd-HHmmss-fff") & "-" & Guid.NewGuid().ToString("N").Substring(0, 6) & ".log"
            IO.File.WriteAllText(ReportCacheFolder & FileName, Report, New Text.UTF8Encoding(False))
            '最多保留 100 条，超出时清理最早的
            Dim Files = IO.Directory.GetFiles(ReportCacheFolder, "report-*.log")
            Array.Sort(Files, StringComparer.OrdinalIgnoreCase)
            For i = 0 To Files.Length - 101
                Try : IO.File.Delete(Files(i)) : Catch : End Try
            Next
        Catch
        End Try
    End Sub

    ''' <summary>当前还有多少条记录等待上报（用于界面提示）。</summary>
    Friend Function ReportPendingCount() As Integer
        Try
            If Not IO.Directory.Exists(ReportCacheFolder) Then Return 0
            Return IO.Directory.GetFiles(ReportCacheFolder, "report-*.log").Length
        Catch
            Return 0
        End Try
    End Function

    '同步发送一条上报，返回是否成功
    Private Function TryPostReport(Content As String) As Boolean
        Try
            Dim Req As System.Net.HttpWebRequest = CType(System.Net.WebRequest.Create(ServerAddress & "report"), System.Net.HttpWebRequest)
            Req.Method = "POST"
            Req.ContentType = "text/plain; charset=utf-8"
            Req.Timeout = 15000
            Dim Body = System.Text.Encoding.UTF8.GetBytes(Content)
            Req.ContentLength = Body.Length
            Using S = Req.GetRequestStream()
                S.Write(Body, 0, Body.Length)
            End Using
            Using Resp = Req.GetResponse()
            End Using
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 把离线缓存的上报按时间顺序补发到服务端，成功一条删一条；一旦失败就停下，等下次连上再试。
    ''' </summary>
    Friend Sub FlushPendingReports()
        SyncLock ReportLock
            Try
                If Not IO.Directory.Exists(ReportCacheFolder) Then Return
                Dim Files = IO.Directory.GetFiles(ReportCacheFolder, "report-*.log")
                If Files.Length = 0 Then Return
                Array.Sort(Files, StringComparer.OrdinalIgnoreCase)
                Dim Sent As Integer = 0
                For Each F In Files
                    Dim Content As String = Nothing
                    Try
                        Content = IO.File.ReadAllText(F, Text.Encoding.UTF8)
                    Catch
                        Continue For
                    End Try
                    If Content = "" Then
                        Try : IO.File.Delete(F) : Catch : End Try
                        Continue For
                    End If
                    If TryPostReport(Content) Then
                        Try : IO.File.Delete(F) : Catch : End Try
                        Sent += 1
                    Else
                        Exit For '还是连不上，保留剩余记录等下次
                    End If
                Next
                If Sent > 0 Then Logger.Info($"已上报 {Sent} 条记录到服务端（含离线补报）")
            Catch ex As Exception
                Logger.Warn(ex, "补报离线记录失败")
            End Try
        End SyncLock
    End Sub

    ''' <summary>
    ''' 上报用的平台描述，例如「Windows 11 专业版 24H2（24H2，build 26100，x64）」。
    ''' .NET Framework 4.8 的 Environment.OSVersion 在 Win10+ 会报 6.2，所以读注册表。
    ''' </summary>
    Friend Function PlatformDisplay() As String
        Dim Name As String = "Windows"
        Dim Extra As New List(Of String)
        Dim BuildNum As Integer = 0
        Try
            Using Key As Microsoft.Win32.RegistryKey =
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SOFTWARE\Microsoft\Windows NT\CurrentVersion")
                If Key IsNot Nothing Then
                    Dim Product = TryCast(Key.GetValue("ProductName"), String)
                    Dim DisplayVersion = TryCast(Key.GetValue("DisplayVersion"), String)
                    Dim Build = TryCast(Key.GetValue("CurrentBuild"), String)
                    Integer.TryParse(If(Build, ""), BuildNum)
                    If Not String.IsNullOrWhiteSpace(Product) Then
                        'Win11 的注册表 ProductName 仍写着 Windows 10，用 build 号纠正
                        If BuildNum >= 22000 AndAlso Product.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase) Then
                            Product = "Windows 11" & Product.Substring("Windows 10".Length)
                        End If
                        Name = Product
                    End If
                    If Not String.IsNullOrWhiteSpace(DisplayVersion) Then Extra.Add(DisplayVersion)
                End If
            End Using
        Catch
        End Try
        If BuildNum > 0 Then Extra.Add("build " & BuildNum)
        Dim Arch As String = If(Environment.Is64BitOperatingSystem, "x64", "x86")
        Try
            If (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") & "").Equals("ARM64", StringComparison.OrdinalIgnoreCase) Then Arch = "arm64"
        Catch
        End Try
        Extra.Add(Arch)
        Return Name & "（" & String.Join("，", Extra) & "）"
    End Function

    ''' <summary>
    ''' 错误自动上报（服务端存入 User-Errors）。全程自动，不需要用户手动操作。
    ''' 可在 设置-其他 中关闭（SystemErrorReport，默认开启）。
    ''' </summary>
    Friend Sub ReportErrorToServer(DetailText As String, Optional Kind As String = "错误")
        ReportToServer("TYW Launcher 匿名上报（" & Kind & "）｜" & PlatformDisplay(), If(DetailText, ""))
    End Sub

    ''' <summary>
    ''' 把任意内容上报到服务端（POST /report）。先写入本地缓存再异步发送：
    ''' 发送成功即删除缓存；断网则留在 启动器目录\TYW Launcher\temp，联网后启动时自动补报。
    ''' </summary>
    Friend Sub ReportToServer(Title As String, DetailText As String, Optional IgnoreSetting As Boolean = False)
        Try
            If Not IgnoreSetting AndAlso Not Settings.Get(Of Boolean)("SystemErrorReport") Then Return
            If IsUpdateWaitingRestart Then Return
            '没配置服务端（开源离线版）：不落盘、不发请求，避免缓存目录无限堆积
            If String.IsNullOrWhiteSpace(ServerAddress) Then Return
            Dim Report As String =
                Title & vbCrLf &
                "平台：" & PlatformDisplay() & vbCrLf &
                "时间：" & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & vbCrLf &
                "版本：" & VersionBaseName & "（版本码 " & VersionCode & "）" & vbCrLf &
                "版本类型：" & BuildTypeDisplay & "（" & ChannelName & "通道）" & vbCrLf &
                "设备码：" & DeviceCode & vbCrLf &
                New String("="c, 48) & vbCrLf &
                If(DetailText, "")
            '★ 先落盘：即使进程随后崩溃或断网也不会丢
            SavePendingReport(Report)
            RunInNewThread(AddressOf FlushPendingReports, "TYW Launcher 上报")
        Catch
        End Try
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    ''' <summary>服务端是否开启了「功能强制云控」（开启后云控下发的功能设置客户端改不回去）。</summary>
    Public CloudForceFeatureOn As Boolean = False
    ''' <summary>被「功能强制云控」锁定的设置键。</summary>
    Public CloudLockedKeys As New HashSet(Of String)
    ''' <summary>是否正在应用云控：此时允许写入被锁定的键。</summary>
    Public IsApplyingCloud As Boolean = False

    ''' <summary>
    ''' 启动时从服务端获取公告与版本信息（Notice.cfg），缓存到 Cache\Notice.cfg 并显示公告；若开启自动更新且有新版本，则自动更新。
    ''' </summary>
    Public ServerLoader As New LoaderTask(Of Integer, Integer)("TYW Launcher 配置更新",
        Sub(Loader As LoaderTask(Of Integer, Integer))
            Try
                Dim Segments = ReadServerNotice()
                '已连上服务端：把断网期间缓存的记录补报上去
                RunInNewThread(Sub() FlushPendingReports(), "补报离线记录", ThreadPriority.BelowNormal)
                '写入缓存，供 IsLauncherNewest（更新提示）使用
                FileUtils.Write(PathTemp & "Cache\Notice.cfg", Segments.Join("|"))
                '可选：从服务端获取 config.json（全功能云控，强制应用服务端下发的设置）
                Try
                    ServerConfig = JObject.Parse(ServerRequest(ServerAddress & "config.json", 5000))
                    ApplyCloudConfig()
                    FileUtils.Write(PathTemp & "Cache\config.json", ServerConfig.ToString(Newtonsoft.Json.Formatting.Indented))
                    Logger.Info("已从服务端获取并应用 config.json 云控配置")
                Catch
                    '服务端未提供 config.json 时忽略
                End Try
                '自动更新（按当前通道的版本码对比）；云控强制更新优先于自动更新开关
                '内测版没有自动更新，直接跳过
                If Not IsBetaChannel AndAlso
                    (Settings.Get(Of Boolean)("SystemAutoUpdate") OrElse Settings.Get(Of Boolean)("CloudForceUpdate")) AndAlso
                    Segments(ChannelCodeIndex) > VersionCode Then
                    RunInNewThread(AddressOf UpdateAuto, "TYW Launcher 自动更新")
                    Return
                End If
                '显示公告（服务端公告，按正式版/快照版分开；公告频率设置为“关闭所有公告”时不显示）
                '同一条公告只自动弹一次，服务端改了内容才会再弹
                Dim NoticeText As String = GetChannelNotice(Segments)
                Dim NoticeTitle As String = If(Segments(0) = "", "公告", Segments(0))
                '例行发布通知（"已发布…请到检查更新查看"）不弹：与本地更新日志重复
                If NoticeText <> "" AndAlso Not IsGenericReleaseNotice(NoticeText) AndAlso
                    Settings.Get(Of Integer)("HintNotice") < 2 AndAlso ShouldShowNotice(NoticeTitle, NoticeText) Then
                    RunInUi(Sub() MyMsgBox(NoticeText, NoticeTitle))
                End If
            Catch ex As Exception
                '没网 / 连不上服务器 不算错误：不弹框、不上报，只在左下角提示，启动器照常离线使用
                If Not IsNetworkIssue(ex) Then Throw
                HintNetworkIssue()
            End Try
        End Sub, Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

    ''' <summary>
    ''' 应用服务端全功能云控：config.json 中 settings 对象的键值会强制写入客户端设置，每次启动生效。
    ''' 键名为 Settings.vb 中的设置键，值支持 bool / int / string。
    ''' 上次被云控接管、本次却没有下发的键会自动恢复默认值，这样服务端取消隐藏（恢复功能）后客户端也能跟着恢复。
    ''' </summary>
    Private Sub ApplyCloudConfig()
        Try
            If ServerConfig Is Nothing Then Return
            '云控强制更新：作为普通设置项下发（CloudForceUpdate），优先级高于自动更新开关。
            '每次启动先重置为关闭，避免上一次云控的残留；随后由云控 settings 覆盖。
            RunInUiWait(Sub() Settings.Set("CloudForceUpdate", False))
            '上一次被云控接管过的设置键（用于回滚）
            Dim KeysFile As String = PathTemp & "Cache\CloudAppliedKeys.txt"
            Dim Previous As New List(Of String)
            Try
                If IO.File.Exists(KeysFile) Then
                    For Each Line In IO.File.ReadAllText(KeysFile).Split({ChrW(13), ChrW(10)}, StringSplitOptions.RemoveEmptyEntries)
                        Dim OldKey = Line.Trim()
                        If OldKey <> "" Then Previous.Add(OldKey)
                    Next
                End If
            Catch
            End Try
            '本次下发的键值
            Dim Props As New List(Of KeyValuePair(Of String, JToken))
            Dim Sec = ServerConfig.Item("settings")
            If Sec IsNot Nothing AndAlso Sec.Type = JTokenType.Object Then
                For Each Prop As KeyValuePair(Of String, JToken) In CType(Sec, JObject)
                    Props.Add(Prop)
                Next
            End If
            Dim Applied As Integer = 0
            Dim Reverted As Integer = 0
            Dim Current As New List(Of String)
            '★ 必须在 UI 线程执行：部分设置项（如 UiHiddenPageLink）的 OnChanged 会直接操作界面元素，
            '  在后台线程调用 Settings.Set 会抛“调用线程无法访问此对象”
            IsApplyingCloud = True
            Try
                RunInUiWait(
                Sub()
                    For Each Prop In Props
                        Try
                            If Prop.Value.Type = JTokenType.Boolean Then
                                Settings.Set(Prop.Key, CBool(Prop.Value))
                                Current.Add(Prop.Key)
                                Applied += 1
                            ElseIf Prop.Value.Type = JTokenType.Integer Then
                                Settings.Set(Prop.Key, CInt(Prop.Value))
                                Current.Add(Prop.Key)
                                Applied += 1
                            ElseIf Prop.Value.Type = JTokenType.String Then
                                Settings.Set(Prop.Key, CType(Prop.Value, JValue).ToString)
                                Current.Add(Prop.Key)
                                Applied += 1
                            End If
                        Catch ex As Exception
                            Logger.Warn(ex, $"云控设置项 {Prop.Key} 应用失败")
                        End Try
                    Next
                    '上次被云控改过、本次却没有下发的键 → 恢复默认值（服务端已取消对应限制/隐藏）
                    For Each OldKey In Previous
                        If Current.Contains(OldKey) Then Continue For
                        Try
                            If Settings.Entries.ContainsKey(OldKey) Then
                                Settings.Reset(OldKey)
                                Reverted += 1
                            End If
                        Catch ex As Exception
                            Logger.Warn(ex, $"云控恢复设置项 {OldKey} 失败")
                        End Try
                    Next
                End Sub)
            Finally
                IsApplyingCloud = False
            End Try
            '记录本次接管的键，供下次对比
            Try
                If Not IO.Directory.Exists(PathTemp & "Cache") Then IO.Directory.CreateDirectory(PathTemp & "Cache")
                IO.File.WriteAllText(KeysFile, String.Join(vbCrLf, Current))
            Catch
            End Try
            If Applied > 0 Then Logger.Info($"云控已强制应用 {Applied} 项设置")
            If Reverted > 0 Then Logger.Info($"云控已恢复 {Reverted} 项设置到默认值（服务端已取消对应限制）")
            '功能强制云控：锁定本次下发的功能设置，客户端在设置里改了也会被拒绝
            CloudForceFeatureOn = Settings.Get(Of Boolean)("CloudForceFeature")
            CloudLockedKeys.Clear()
            If CloudForceFeatureOn Then
                For Each Key In Current
                    If Key <> "CloudForceFeature" AndAlso Key <> "CloudForceUpdate" Then CloudLockedKeys.Add(Key)
                Next
                Logger.Info($"功能强制云控已开启，锁定 {CloudLockedKeys.Count} 项设置")
            End If
        Catch ex As Exception
            Logger.Error(ex, "应用服务端云控配置失败")
        End Try
    End Sub

    ''' <summary>
    ''' 启动时自动更新：静默下载当前通道的新版本，替换后自动重启（设置 → 其他 → 自动更新 开启时生效）。
    ''' </summary>
    Friend Sub UpdateAuto()
        '内测版没有自动更新
        If IsBetaChannel Then Return
        Try
            Hint("发现 TYW Launcher 新版本，正在自动更新……")
            UpdateDownloadAndRestart()
        Catch ex As Exception
            '没网 / 连不上服务器 不算错误：不弹框、不上报，只在左下角提示
            If IsNetworkIssue(ex) Then
                HintNetworkIssue()
                Return
            End If
            Logger.Warn(ex, "自动更新失败")
            Hint("自动更新失败：" & ex.Message & "，可在 设置 - 其他 中手动检查更新。", HintType.Red)
        End Try
    End Sub

    ''' <summary>
    ''' 切换更新通道后立即检查一次当前通道的版本：有新版本则自动下载并重启，否则提示已是最新。
    ''' </summary>
    Friend Sub CheckChannelUpdateAndRestart()
        RunInNewThread(
            Sub()
                Try
                    Dim TargetChannel As String = If(IsBetaChannel, "Beta", If(IsSnapshotChannel, "Snapshot", "Release"))
                    Dim ChannelText As String = ChannelDisplay(TargetChannel)
                    Hint("正在检查" & ChannelText & "更新……")
                    Dim Segments = ReadServerNotice()
                    '当前跑的还是别的通道的程序 → 必须先把程序本体换成目标通道的
                    Dim RunningChannel As String =
                        If(BuildType = BuildTypes.Beta, "Beta", If(BuildType = BuildTypes.Snapshot, "Snapshot", "Release"))
                    If RunningChannel <> TargetChannel Then
                        Logger.Info($"通道切换：{RunningChannel} → {TargetChannel}，开始替换启动器程序")
                        '内测版程序分发受服务端保护：下载 Beta.exe 必须附带设备码与内测码
                        If Not SwitchChannel(TargetChannel, Ident:=DeviceCode,
                                             Key:=If(TargetChannel = "Beta", Settings.Get(Of String)("BetaCode"), "")) Then
                            '下载被取消 / 失败：程序本体没换，继续用当前通道运行
                            Hint("通道切换未完成，仍在使用" & ChannelDisplay(RunningChannel) & "启动器", HintType.Red)
                        End If
                        Return
                    End If
                    Dim LatestCode As Integer = GetChannelCode(Segments, TargetChannel)
                    If LatestCode <= VersionCode Then
                        Hint("当前已是最新" & ChannelText & "（版本码 " & VersionCode & "）", HintType.Green)
                        Return
                    End If
                    UpdateAuto()
                Catch ex As Exception
                    '没网 / 连不上服务器 不算错误：不弹框、不上报，只在左下角提示
                    If IsNetworkIssue(ex) Then
                        HintNetworkIssue()
                        Return
                    End If
                    Logger.Warn(ex, "切换通道后检查更新失败")
                    Hint("检查更新失败：" & ex.Message & "，可稍后在 设置 - 其他 中手动检查更新。", HintType.Red)
                End Try
            End Sub, "TYW Launcher 通道切换更新")
    End Sub

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.Rank98
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' ★ 这个值同时是注册表设置的 DES 密钥来源（Settings 里用 "PCL" &amp; Identify 加密），
    ''' 一旦变化，所有老用户的加密设置都会解不开，所以必须保持常量不变。
    ''' 展示/复制给用户看的设备码请用 DeviceCode。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

    Private _DeviceCode As String = Nothing
    ''' <summary>
    ''' 展示用的设备识别码（形如 A1B2-C3D4-E5F6-7890）。
    ''' 由机器 GUID、机器名、CPU 标识做 MD5 得到，同一台机器稳定不变，不含明文个人信息。
    ''' </summary>
    Friend ReadOnly Property DeviceCode As String
        Get
            If _DeviceCode IsNot Nothing Then Return _DeviceCode
            Try
                Dim Seed As New System.Text.StringBuilder()
                Try
                    Using Base As Microsoft.Win32.RegistryKey =
                            Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
                        Using Key As Microsoft.Win32.RegistryKey = Base.OpenSubKey("SOFTWARE\Microsoft\Cryptography")
                            If Key IsNot Nothing Then Seed.Append(Key.GetValue("MachineGuid").ToString())
                        End Using
                    End Using
                Catch
                    '读不到机器 GUID 时退化为其它硬件标识，结果依然稳定
                End Try
                Seed.Append("|").Append(Environment.MachineName)
                Seed.Append("|").Append(Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"))
                Seed.Append("|").Append(Environment.GetEnvironmentVariable("SystemRoot"))
                Using Md5 As System.Security.Cryptography.MD5 = System.Security.Cryptography.MD5.Create()
                    Dim Hash = Md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Seed.ToString()))
                    Dim Hex As String = BitConverter.ToString(Hash).Replace("-", "").Substring(0, 16).ToUpperInvariant()
                    _DeviceCode = $"{Hex.Substring(0, 4)}-{Hex.Substring(4, 4)}-{Hex.Substring(8, 4)}-{Hex.Substring(12, 4)}"
                End Using
            Catch ex As Exception
                Logger.Warn(ex, "生成设备识别码失败")
                _DeviceCode = Identify
            End Try
            Return _DeviceCode
        End Get
    End Property

#End Region

End Module
