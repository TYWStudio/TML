Public Class PageSetupExt

    Private Shadows IsLoaded As Boolean = False

    Private Sub PageSetupExt_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        If IsLoaded Then Return
        IsLoaded = True
        AddHandler BtnImport.Click, AddressOf BtnImport_Click
        AddHandler BtnRefresh.Click, AddressOf BtnRefresh_Click
        AddHandler BtnOpenDir.Click, AddressOf BtnOpenDir_Click
        SetupDragDrop()
        ScanAndRefresh()
    End Sub

    Private Sub ScanAndRefresh()
        Try
            ModExt.Init()
            ModExt.Scan()
        Catch ex As Exception
            Logger.Warn(ex, "扫描拓展失败")
        End Try
        RefreshList()
    End Sub

    Private Sub BtnRefresh_Click(sender As Object, e As EventArgs)
        ScanAndRefresh()
        Hint("拓展列表已刷新", Log:=False)
    End Sub

    Private Sub BtnOpenDir_Click(sender As Object, e As EventArgs)
        Try
            Dim dir As String = ModExt.ExtensionRoot()
            If Directory.Exists(dir) Then
                Process.Start("explorer.exe", """" & dir & """")
            Else
                Hint("拓展目录还不存在，先导入一个拓展吧", Log:=False)
            End If
        Catch ex As Exception
            Logger.Warn(ex, "打开拓展目录失败")
            Hint("打开拓展目录失败：" & ex.Message, Log:=False)
        End Try
    End Sub

    Private Sub BtnImport_Click(sender As Object, e As EventArgs)
        Dim dlg As New Microsoft.Win32.OpenFileDialog With {
            .Filter = "TYW 拓展包 (*.tmlext;*.jar)|*.tmlext;*.jar|所有文件|*.*",
            .Title = "选择拓展包",
            .Multiselect = True
        }
        If dlg.ShowDialog() <> True Then Return
        InstallFiles(dlg.FileNames)
    End Sub

    ''' <summary>按钮导入与拖拽导入共用的安装入口。</summary>
    Friend Sub InstallFiles(files As IEnumerable(Of String))
        If files Is Nothing Then Return
        Dim okCount As Integer = 0
        Dim fail As New List(Of String)
        For Each f In files
            Try
                ModExt.Install(f)
                okCount += 1
            Catch ex As Exception
                fail.Add(IO.Path.GetFileName(f) & "：" & ex.Message)
            End Try
        Next
        If okCount > 0 Then Hint("已导入 " & okCount & " 个拓展，启用前请确认权限")
        If fail.Count > 0 Then MyMsgBox("以下拓展导入失败：" & vbCrLf & String.Join(vbCrLf, fail), "导入拓展", "确定")
        ScanAndRefresh()
    End Sub

#Region "拖拽导入"

    '用 FindName 取提示层，不直接依赖 XAML 生成的字段（离线兜底编译时用的是预生成 g.vb）
    Private _dropHint As Border = Nothing
    Private ReadOnly Property DropHint As Border
        Get
            If _dropHint Is Nothing Then _dropHint = TryCast(FindName("PanDropHint"), Border)
            Return _dropHint
        End Get
    End Property

    Private Sub SetDropHint(visible As Boolean)
        If DropHint Is Nothing Then Return
        DropHint.Visibility = If(visible, Visibility.Visible, Visibility.Collapsed)
    End Sub

    Private Sub SetupDragDrop()
        '窗口级 FormMain 也注册了拖放（DragEnter/DragOver 带 handledEventsToo:=True）。
        '这里先处理并把 e.Handled 置为 True，Effects 设为 Copy，窗口的 HandleDrag 会直接放行；
        'Drop 是普通 Handles，Handled=True 后不会再落到 FileDrag 里被当成 Mod 安装。
        System.Windows.DragDrop.AddDragEnterHandler(Me, New System.Windows.DragEventHandler(AddressOf Ext_DragEnter))
        System.Windows.DragDrop.AddDragOverHandler(Me, New System.Windows.DragEventHandler(AddressOf Ext_DragOver))
        System.Windows.DragDrop.AddDragLeaveHandler(Me, New System.Windows.DragEventHandler(AddressOf Ext_DragLeave))
        System.Windows.DragDrop.AddDropHandler(Me, New System.Windows.DragEventHandler(AddressOf Ext_Drop))
    End Sub

    ''' <summary>取出拖入的文件列表；非文件拖拽返回 Nothing，交还给窗口处理。</summary>
    Private Function GetDropFiles(data As System.Windows.IDataObject) As String()
        Try
            If data Is Nothing OrElse Not data.GetDataPresent(System.Windows.DataFormats.FileDrop) Then Return Nothing
            Dim raw As Object = data.GetData(System.Windows.DataFormats.FileDrop)
            Return TryCast(raw, String())
        Catch ex As Exception
            Logger.Warn(ex, "读取拖入的拓展文件失败")
            Return Nothing
        End Try
    End Function

    Private Shared Function IsExtPackage(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) Then Return False
        Dim ext As String = IO.Path.GetExtension(path)
        Return String.Equals(ext, ".tmlext", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(ext, ".jar", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function PickExtPackages(files As String()) As List(Of String)
        Dim result As New List(Of String)
        If files Is Nothing Then Return result
        For Each f In files
            If IsExtPackage(f) AndAlso IO.File.Exists(f) Then result.Add(f)
        Next
        Return result
    End Function

    Private Sub Ext_DragEnter(sender As Object, e As System.Windows.DragEventArgs)
        Ext_DragOver(sender, e)
    End Sub

    Private Sub Ext_DragOver(sender As Object, e As System.Windows.DragEventArgs)
        Dim targets As List(Of String) = PickExtPackages(GetDropFiles(e.Data))
        If targets.Count > 0 Then
            e.Effects = System.Windows.DragDropEffects.Copy
            SetDropHint(True)
        Else
            e.Effects = System.Windows.DragDropEffects.None
            SetDropHint(False)
        End If
        e.Handled = True
    End Sub

    Private Sub Ext_DragLeave(sender As Object, e As System.Windows.DragEventArgs)
        '在页面内部各子控件之间移动时也会触发 DragLeave，只有真正移出页面才收起提示
        Dim pos As Point = e.GetPosition(Me)
        If pos.X < 0 OrElse pos.Y < 0 OrElse pos.X > ActualWidth OrElse pos.Y > ActualHeight Then
            SetDropHint(False)
        End If
    End Sub

    Private Sub Ext_Drop(sender As Object, e As System.Windows.DragEventArgs)
        SetDropHint(False)
        Dim files As String() = GetDropFiles(e.Data)
        Dim targets As List(Of String) = PickExtPackages(files)
        If targets.Count = 0 Then Return '不是拓展包，不拦截，让窗口按原来的逻辑处理
        e.Handled = True
        e.Effects = System.Windows.DragDropEffects.Copy
        If files IsNot Nothing AndAlso files.Length > targets.Count Then
            Hint("已忽略 " & (files.Length - targets.Count) & " 个非拓展文件", Log:=False)
        End If
        InstallFiles(targets)
    End Sub

#End Region

    Private Sub RefreshList()
        PanExtList.Children.Clear()
        Dim list As List(Of ModExt.ExtInfo) = ModExt.GetAll()
        Dim onCount As Integer = 0
        Dim errCount As Integer = 0
        Dim noRuntime As Integer = 0
        For Each info In list
            If info.State = ModExt.ExtState.Enabled Then onCount += 1
            If info.State = ModExt.ExtState.Error Then errCount += 1
            If info.NeedsRuntime Then noRuntime += 1
        Next
        Dim summary As String = "已安装 " & list.Count & " 个拓展 · 已启用 " & onCount & " 个"
        If errCount > 0 Then summary &= " · " & errCount & " 个出错"
        If noRuntime > 0 Then summary &= " · " & noRuntime & " 个缺少运行环境"
        LabSummary.Text = summary

        If list.Count = 0 Then
            PanExtList.Children.Add(BuildEmpty())
            Return
        End If

        For Each info In list.OrderByDescending(Function(x) x.State = ModExt.ExtState.Enabled).
                                    ThenBy(Function(x) x.Name)
            PanExtList.Children.Add(BuildRow(info))
        Next
    End Sub

    Private Function BuildEmpty() As UIElement
        Dim card As New MyCard() With {.Margin = New Thickness(0, 0, 0, 12)}
        Dim pan As New StackPanel() With {.Margin = New Thickness(21, 18, 21, 20)}
        pan.Children.Add(New TextBlock With {
            .Text = "还没有安装任何拓展",
            .FontSize = 14, .FontWeight = FontWeights.Bold
        })
        pan.Children.Add(New TextBlock With {
            .Text = "点上面的「导入拓展」选一个 .tmlext 或 .jar 文件，或者直接把文件拖进窗口，装好后在这里确认权限再启用。" & vbCrLf &
                    "想自己写一个？下面有六种语言的官方模板。",
            .TextWrapping = TextWrapping.Wrap, .LineHeight = 19,
            .Margin = New Thickness(0, 8, 0, 0),
            .Foreground = Res("ColorBrushGray3", Color.FromRgb(140, 140, 140))
        })
        '必须走 Children：BorderChild 挂在 MyCard 内部 IsHitTestVisible=False 的 MainBorder 上，
        '整棵子树都不接收鼠标事件，卡片里的按钮会全部点不动。
        card.Children.Add(pan)
        Return card
    End Function

    Private Function BuildRow(info As ModExt.ExtInfo) As UIElement
        Dim card As New MyCard() With {.Margin = New Thickness(0, 0, 0, 12)}
        Dim root As New Grid() With {.Margin = New Thickness(21, 15, 21, 16)}
        root.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = New GridLength(1, GridUnitType.Star)})
        root.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = GridLength.Auto})
        For i As Integer = 0 To 3
            root.RowDefinitions.Add(New RowDefinition() With {.Height = GridLength.Auto})
        Next

        ' ── 标题行：名称 + 版本 + 状态徽标 ──
        Dim head As New StackPanel() With {.Orientation = Orientation.Horizontal}
        head.Children.Add(New TextBlock With {
            .Text = If(String.IsNullOrWhiteSpace(info.Name), info.Id, info.Name),
            .FontSize = 14.5, .FontWeight = FontWeights.Bold,
            .VerticalAlignment = VerticalAlignment.Center
        })
        head.Children.Add(New TextBlock With {
            .Text = "v" & If(String.IsNullOrWhiteSpace(info.Version), "0.0.0", info.Version),
            .FontSize = 12, .Margin = New Thickness(8, 0, 0, 0),
            .VerticalAlignment = VerticalAlignment.Center,
            .Foreground = Res("ColorBrushGray3", Color.FromRgb(140, 140, 140))
        })
        head.Children.Add(StateChip(info))
        root.Children.Add(head)

        ' ── 元信息 ──
        Dim meta As String = "作者 " & If(String.IsNullOrWhiteSpace(info.Author), "未知", info.Author) &
                             " · 运行时 " & If(String.IsNullOrWhiteSpace(info.Runtime), "未知", info.Runtime)
        If Not String.IsNullOrWhiteSpace(info.Format) Then meta &= " · 格式 " & info.Format
        If Not String.IsNullOrWhiteSpace(info.PackageDir) Then meta &= " · 已解包"
        Dim labMeta As New TextBlock With {
            .Text = meta, .FontSize = 12, .Margin = New Thickness(0, 6, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis,
            .Foreground = Res("ColorBrushGray4", Color.FromRgb(166, 166, 166))
        }
        Grid.SetRow(labMeta, 1)
        root.Children.Add(labMeta)

        ' ── 描述 ──
        If Not String.IsNullOrWhiteSpace(info.Description) Then
            Dim labDesc As New TextBlock With {
                .Text = info.Description, .TextWrapping = TextWrapping.Wrap, .LineHeight = 19,
                .Margin = New Thickness(0, 8, 0, 0)
            }
            Grid.SetRow(labDesc, 2)
            root.Children.Add(labDesc)
        End If

        ' ── 权限 / 事件 ──
        Dim permLine As New TextBlock With {
            .TextWrapping = TextWrapping.Wrap, .FontSize = 12, .LineHeight = 18,
            .Margin = New Thickness(0, 8, 0, 0),
            .Foreground = Res("ColorBrushGray3", Color.FromRgb(140, 140, 140))
        }
        Dim permText As String = "权限：" & PermLabel(info)
        If info.Subscribe IsNot Nothing AndAlso info.Subscribe.Count > 0 Then permText &= "　订阅：" & String.Join("、", info.Subscribe)
        permLine.Text = permText
        Grid.SetRow(permLine, 3)
        root.Children.Add(permLine)

        ' ── 按钮列 ──
        Dim btns As New StackPanel() With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center,
            .Margin = New Thickness(18, 0, 0, 0)
        }
        '用局部变量固定本次循环的 Id，避免闭包捕获 For Each 变量后所有按钮都指向最后一个拓展
        Dim curId As String = info.Id
        Dim btnDetail As New MyButton With {.Text = "详情", .Height = 33, .MinWidth = 82, .Padding = New Thickness(12, 0, 12, 0), .Margin = New Thickness(0, 0, 10, 0)}
        AddHandler btnDetail.Click, Sub(s, ev) ShowDetail(curId)
        btns.Children.Add(btnDetail)

        Dim btnToggle As New MyButton With {
            .Text = If(info.State = ModExt.ExtState.Enabled, "禁用", "启用"),
            .Height = 33, .MinWidth = 82, .Padding = New Thickness(12, 0, 12, 0), .Margin = New Thickness(0, 0, 10, 0),
            .ColorType = If(info.State = ModExt.ExtState.Enabled, MyButton.ColorState.Normal, MyButton.ColorState.Highlight)
        }
        ' 同一个包之前已确认过权限（AutoStart）就不再重复弹窗，换过包才会重新问
        Dim needConfirm As Boolean = Not info.AutoStart
        AddHandler btnToggle.Click, Sub(s, ev) ToggleExtension(curId, needConfirm)
        btns.Children.Add(btnToggle)

        Dim btnRemove As New MyButton With {
            .Text = "卸载", .Height = 33, .MinWidth = 82, .Padding = New Thickness(12, 0, 12, 0),
            .ColorType = MyButton.ColorState.Red
        }
        AddHandler btnRemove.Click, Sub(s, ev) UninstallExtension(curId)
        btns.Children.Add(btnRemove)

        Grid.SetColumn(btns, 1)
        Grid.SetRowSpan(btns, 4)
        root.Children.Add(btns)

        '同 BuildEmpty：走 Children，不要放进 IsHitTestVisible=False 的 BorderChild
        card.Children.Add(root)
        Return card
    End Function

    Private Function StateChip(info As ModExt.ExtInfo) As UIElement
        Dim text As String
        Dim fg As Color
        Dim bg As Color
        If info.NeedsRuntime Then
            text = "缺少运行环境"
            fg = Color.FromRgb(&HB7, &H79, &H1F)
            bg = Color.FromArgb(&H22, &HF5, &HA5, &H24)
        ElseIf info.State = ModExt.ExtState.Enabled Then
            text = "已启用"
            fg = Color.FromRgb(&H2E, &H9E, &H4F)
            bg = Color.FromArgb(&H22, &H34, &HC7, &H59)
        ElseIf info.State = ModExt.ExtState.Error Then
            text = "出错"
            fg = Color.FromRgb(&HE5, &H48, &H4D)
            bg = Color.FromArgb(&H22, &HFF, &H4C, &H4C)
        Else
            text = "已禁用"
            fg = Color.FromRgb(&H8C, &H8C, &H8C)
            bg = Color.FromArgb(&H1A, &H8C, &H8C, &H8C)
        End If
        Dim chip As New Border With {
            .CornerRadius = New CornerRadius(4), .Padding = New Thickness(7, 2, 7, 2),
            .Margin = New Thickness(10, 0, 0, 0), .VerticalAlignment = VerticalAlignment.Center,
            .Background = New SolidColorBrush(bg)
        }
        chip.Child = New TextBlock With {.Text = text, .FontSize = 11.5, .Foreground = New SolidColorBrush(fg)}
        Return chip
    End Function

    Private Function PermLabel(info As ModExt.ExtInfo) As String
        If info.Permissions Is Nothing OrElse info.Permissions.Count = 0 Then Return "无（仅基础运行）"
        Return String.Join("、", (From p In info.Permissions Select PermName(p)))
    End Function

    Private Sub ShowDetail(id As String)
        Dim info As ModExt.ExtInfo = ModExt.GetInfo(id)
        If info Is Nothing Then Return
        Dim lines As New List(Of String)
        lines.Add("标识：" & info.Id)
        lines.Add("版本：" & info.Version & "　作者：" & info.Author)
        lines.Add("运行时：" & info.Runtime & "　入口：" & info.Entry)
        If info.Platforms IsNot Nothing AndAlso info.Platforms.Count > 0 Then lines.Add("支持平台：" & String.Join("、", info.Platforms))
        lines.Add("权限：" & PermLabel(info))
        If info.Subscribe IsNot Nothing AndAlso info.Subscribe.Count > 0 Then lines.Add("订阅事件：" & String.Join("、", info.Subscribe))
        If Not String.IsNullOrWhiteSpace(info.Sha256) Then lines.Add("SHA-256：" & info.Sha256)
        If Not String.IsNullOrWhiteSpace(info.PackageDir) Then lines.Add("解包目录：" & info.PackageDir)
        If Not String.IsNullOrWhiteSpace(info.PrivateDir) Then lines.Add("私有数据：" & info.PrivateDir)
        If info.State = ModExt.ExtState.Error AndAlso Not String.IsNullOrWhiteSpace(info.ErrorMessage) Then
            lines.Add("错误：" & info.ErrorMessage)
        End If
        lines.Add("")
        lines.Add("拓展只能通过标准输入输出与启动器通信，无法直接访问文件、网络、账号或启动器内部数据。")
        MyMsgBox(String.Join(vbCrLf, lines), If(String.IsNullOrWhiteSpace(info.Name), info.Id, info.Name) & " 详情", "关闭")
    End Sub

    Private Sub ToggleExtension(id As String, Optional needConfirm As Boolean = True)
        Dim info As ModExt.ExtInfo = ModExt.GetInfo(id)
        If info Is Nothing Then Return
        If info.State = ModExt.ExtState.Enabled Then
            ModExt.Disable(id)
            Hint("已禁用拓展：" & info.Name, Log:=False)
            RefreshList()
            Return
        End If
        If info.State = ModExt.ExtState.Error Then
            MyMsgBox("该拓展存在错误，无法启用：" & info.ErrorMessage, "无法启用", "确定")
            Return
        End If
        If info.NeedsRuntime Then
            MyMsgBox("这个拓展需要本机已安装 " & info.Runtime & " 运行环境，启动器不会自动下载。安装好运行环境后点「刷新」再试。",
                     "缺少运行环境", "确定")
            Return
        End If
        If needConfirm Then
            Dim permText As String
            If info.Permissions Is Nothing OrElse info.Permissions.Count = 0 Then
                permText = "（无，仅基础运行权限）"
            Else
                permText = String.Join(vbCrLf & "· ", (From p In info.Permissions Select PermName(p)))
            End If
            If MyMsgBox("拓展「" & info.Name & "」申请以下权限：" & vbCrLf & "· " & permText & vbCrLf & vbCrLf &
                        "启用后该拓展将在独立的子进程中运行，仅能使用上述白名单能力，无法访问文件、网络、账号或启动器内部数据。是否确认启用？",
                        "启用拓展", "确认启用", "取消") <> 1 Then
                Return
            End If
        End If
        If ModExt.Enable(id, True) Then
            Hint("已启用拓展：" & info.Name, Log:=False)
        Else
            MyMsgBox("拓展启用失败，可能是缺少运行环境（" & info.Runtime & "），请安装后在系统中重试。", "启用失败", "确定")
        End If
        RefreshList()
    End Sub

    Private Sub UninstallExtension(id As String)
        Dim info As ModExt.ExtInfo = ModExt.GetInfo(id)
        If info Is Nothing Then Return
        If MyMsgBox("确定要卸载拓展「" & info.Name & "」吗？这会删除拓展包与它的私有数据。", "卸载拓展", "卸载", "取消") = 1 Then
            ModExt.Uninstall(id)
            Hint("已卸载拓展：" & info.Name, Log:=False)
            RefreshList()
        End If
    End Sub

    Private Function PermName(p As String) As String
        Select Case p
            Case "log" : Return "写入日志"
            Case "toast" : Return "弹出提示"
            Case "host.info" : Return "读取启动器 / 系统信息"
            Case "instances.list" : Return "读取游戏实例列表（仅名称与版本）"
            Case "events" : Return "订阅启动器事件"
            Case "storage" : Return "读写拓展私有存储"
            Case "settings" : Return "读写拓展自身设置"
            Case Else : Return p
        End Select
    End Function

    Private Shared Function Res(key As String, fallback As Color) As Brush
        Try
            Dim b As Brush = TryCast(System.Windows.Application.Current.TryFindResource(key), Brush)
            If b IsNot Nothing Then Return b
        Catch
        End Try
        Return New SolidColorBrush(fallback)
    End Function

End Class
