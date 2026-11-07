Public Class PageOtherExt

    Private Shadows IsLoaded As Boolean = False

    Private Sub PageOtherExt_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        If IsLoaded Then Return
        IsLoaded = True
        AddHandler BtnImport.Click, AddressOf BtnImport_Click
        AddHandler BtnRefresh.Click, AddressOf BtnRefresh_Click
        ScanAndRefresh()
    End Sub

    Private Sub ScanAndRefresh()
        Try
            ModExt.Init()
            ModExt.Scan()
        Catch ex As Exception
            Logger.Warn(ex, "扫描扩展失败")
        End Try
        RefreshList()
    End Sub

    Private Sub BtnRefresh_Click(sender As Object, e As EventArgs)
        ScanAndRefresh()
    End Sub

    Private Sub BtnImport_Click(sender As Object, e As EventArgs)
        Dim dlg As New Microsoft.Win32.OpenFileDialog() With {
            .Filter = "TYW 扩展包 (*.tmlext;*.jar)|*.tmlext;*.jar",
            .Title = "选择扩展包"
        }
        If dlg.ShowDialog() = True Then
            Try
                Dim info As ModExt.ExtInfo = ModExt.Install(dlg.FileName)
                Hint("已导入扩展：" & info.Name)
                ScanAndRefresh()
            Catch ex As Exception
                MyMsgBox("导入失败：" & ex.Message, "导入扩展", "确定")
            End Try
        End If
    End Sub

    Private Sub RefreshList()
        PanExtList.Children.Clear()
        Dim list As List(Of ModExt.ExtInfo) = ModExt.GetAll()
        If list.Count = 0 Then
            Dim empty As New TextBlock() With {
                .Text = "暂无已安装的扩展。点击「导入拓展」选择 .tmlext 或 .jar 文件。",
                .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 6, 0, 0),
                .Foreground = New SolidColorBrush(Color.FromRgb(150, 150, 150))
            }
            PanExtList.Children.Add(empty)
            Return
        End If
        For Each info In list
            PanExtList.Children.Add(BuildRow(info))
        Next
    End Sub

    Private Function BuildRow(info As ModExt.ExtInfo) As UIElement
        Dim card As New MyCard() With {.Margin = New Thickness(0, 0, 0, 15)}
        Dim grid As New Grid()
        grid.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = New GridLength(1, GridUnitType.Star)})
        grid.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = GridLength.Auto})
        grid.RowDefinitions.Add(New RowDefinition() With {.Height = GridLength.Auto})
        grid.RowDefinitions.Add(New RowDefinition() With {.Height = GridLength.Auto})
        grid.Margin = New Thickness(21, 12, 21, 14)

        Dim title As New TextBlock() With {
            .Text = info.Name & "  v" & info.Version,
            .FontWeight = FontWeights.Bold, .FontSize = 14
        }
        grid.Children.Add(title)

        Dim status As String
        If info.State = ModExt.ExtState.Enabled Then
            status = "已启用"
        ElseIf info.State = ModExt.ExtState.Error Then
            status = "错误：" & info.ErrorMessage
        Else
            status = "已禁用"
        End If
        If info.NeedsRuntime Then status = "缺少运行环境：" & info.Runtime
        Dim detail As New TextBlock() With {
            .Text = If(String.IsNullOrEmpty(info.Description), "", info.Description & vbCrLf) &
                    "作者：" & info.Author & " · 运行环境：" & info.Runtime & " · 状态：" & status,
            .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 6, 0, 0)
        }
        Grid.SetRow(detail, 1)
        grid.Children.Add(detail)

        Dim btnPanel As New StackPanel() With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Grid.SetColumn(btnPanel, 1)
        Dim btnToggle As New MyButton
        btnToggle.Text = If(info.State = ModExt.ExtState.Enabled, "禁用", "启用")
        btnToggle.Height = 35
        btnToggle.MinWidth = 90
        btnToggle.Margin = New Thickness(0, 0, 10, 0)
        btnToggle.Padding = New Thickness(13, 0, 13, 0)
        AddHandler btnToggle.Click, Sub(s, ev) ToggleExtension(info.Id)
        Dim btnUninstall As New MyButton
        btnUninstall.Text = "卸载"
        btnUninstall.Height = 35
        btnUninstall.MinWidth = 90
        btnUninstall.Padding = New Thickness(13, 0, 13, 0)
        AddHandler btnUninstall.Click, Sub(s, ev) UninstallExtension(info.Id)
        btnPanel.Children.Add(btnToggle)
        btnPanel.Children.Add(btnUninstall)
        grid.Children.Add(btnPanel)

        card.BorderChild = grid
        Return card
    End Function

    Private Sub ToggleExtension(id As String)
        Dim info As ModExt.ExtInfo = ModExt.GetInfo(id)
        If info Is Nothing Then Return
        If info.State = ModExt.ExtState.Enabled Then
            ModExt.Disable(id)
            RefreshList()
            Return
        End If
        If info.State = ModExt.ExtState.Error Then
            MyMsgBox("该扩展存在错误，无法启用：" & info.ErrorMessage, "无法启用", "确定")
            Return
        End If
        Dim permText As String
        If info.Permissions.Count = 0 Then
            permText = "（无，仅基础运行权限）"
        Else
            permText = String.Join(vbCrLf & "· ", (From p In info.Permissions Select PermName(p)))
        End If
        If MyMsgBox("扩展「" & info.Name & "」申请以下权限：" & vbCrLf & "· " & permText & vbCrLf & vbCrLf &
                    "启用后该扩展将在独立的子进程中运行，仅能使用上述白名单能力，无法访问文件、网络、账号或启动器内部数据。是否确认启用？",
                    "启用扩展", "确认启用", "取消") = 1 Then
            If ModExt.Enable(id, True) Then
                Hint("已启用扩展：" & info.Name)
            Else
                MyMsgBox("扩展启用失败，可能是缺少运行环境（" & info.Runtime & "）。请在系统中安装对应运行环境后重试。", "启用失败", "确定")
            End If
            RefreshList()
        End If
    End Sub

    Private Sub UninstallExtension(id As String)
        Dim info As ModExt.ExtInfo = ModExt.GetInfo(id)
        If info Is Nothing Then Return
        If MyMsgBox("确定要卸载扩展「" & info.Name & "」吗？这将删除其文件与全部数据。", "卸载扩展", "卸载", "取消") = 1 Then
            ModExt.Uninstall(id)
            Hint("已卸载扩展：" & info.Name)
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
            Case "storage" : Return "读写扩展私有存储"
            Case "settings" : Return "读写扩展自身设置"
            Case Else : Return p
        End Select
    End Function

End Class
