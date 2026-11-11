Imports System.Collections.Generic
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

Public Class PageSetupStore

    Private Shadows IsLoaded As Boolean = False
    Private AllCats As New List(Of ModStore.StoreCategory)
    Private SelectedCategory As Integer = -1 ' -1 = 全部
    Private CurrentSearch As String = ""

    Private Sub PageSetupStore_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        PanBack.ScrollToHome()
        LabKey.Text = ModStore.StoreKeyMasked()
        If IsLoaded Then Return
        IsLoaded = True
        AddHandler BtnRefresh.Click, AddressOf BtnRefresh_Click
        AddHandler SearchBox.TextChanged, AddressOf SearchBox_TextChanged
        LoadTree()
    End Sub

#Region "数据加载"

    Private Sub BtnRefresh_Click(sender As Object, e As RoutedEventArgs)
        LoadTree()
    End Sub

    Private Sub LoadTree()
        LabStatus.Text = "正在从下载站加载拓展列表……"
        RunInNewThread(
            Sub()
                Try
                    Dim cats = ModStore.FetchTree()
                    RunInUi(
                        Sub()
                            AllCats = cats
                            BuildCats()
                            ShowProjects()
                            If cats.Count = 0 Then
                                LabStatus.Text = "下载站暂无可展示的分类，请先在下载站新增分类与项目。"
                            Else
                                Dim total = 0
                                For Each c In cats : total += c.Projects.Count : Next
                                LabStatus.Text = "共 " & cats.Count & " 个分类、" & total & " 个项目（应用凭证：" & ModStore.StoreKeyMasked() & "）"
                            End If
                        End Sub)
                Catch ex As Exception
                    RunInUi(Sub()
                                If ModSecret.IsNetworkIssue(ex) Then
                                    LabStatus.Text = "暂时无法从下载站获取列表，请检查网络后点「刷新」。"
                                    ModSecret.HintNetworkIssue()
                                Else
                                    LabStatus.Text = "加载拓展列表失败：" & ex.Message
                                End If
                            End Sub)
                End Try
            End Sub, "拓展商店加载")
    End Sub

    Private Sub BuildCats()
        PanCats.Children.Clear()
        Dim all As New MyButton With {
            .Text = "全部", .Height = 33, .MinWidth = 90, .Padding = New Thickness(14, 0, 14, 0),
            .Margin = New Thickness(0, 0, 10, 10), .Tag = -1}
        AddHandler all.Click, AddressOf Cat_Click
        PanCats.Children.Add(all)
        For Each c In AllCats
            Dim btn As New MyButton With {
                .Text = If(String.IsNullOrWhiteSpace(c.Name), "未命名分类", c.Name),
                .Height = 33, .MinWidth = 90, .Padding = New Thickness(14, 0, 14, 0),
                .Margin = New Thickness(0, 0, 10, 10), .Tag = c.Id}
            AddHandler btn.Click, AddressOf Cat_Click
            PanCats.Children.Add(btn)
        Next
    End Sub

    Private Sub Cat_Click(sender As Object, e As RoutedEventArgs)
        Dim btn = TryCast(sender, MyButton)
        If btn Is Nothing Then Return
        SelectedCategory = If(btn.Tag Is Nothing, -1, CInt(btn.Tag))
        ShowProjects()
    End Sub

    Private Sub SearchBox_TextChanged(sender As Object, e As EventArgs)
        CurrentSearch = If(SearchBox.Text, "").Trim().ToLowerInvariant()
        ShowProjects()
    End Sub

    Private Sub ShowProjects()
        PanProjects.Children.Clear()
        If AllCats.Count = 0 Then Return
        Dim list As New List(Of ModStore.StoreProject)
        For Each c In AllCats
            If SelectedCategory = -1 OrElse c.Id = SelectedCategory Then
                For Each p In c.Projects : list.Add(p) : Next
            End If
        Next
        If CurrentSearch <> "" Then
            list = list.FindAll(Function(p) MatchesSearch(p, CurrentSearch))
        End If
        If list.Count = 0 Then
            PanProjects.Children.Add(BuildEmpty("没有匹配的拓展。"))
            Return
        End If
        For Each p In list.OrderBy(Function(x) x.Name)
            PanProjects.Children.Add(BuildProjectCard(p))
        Next
    End Sub

    Private Shared Function MatchesSearch(p As ModStore.StoreProject, q As String) As Boolean
        If p.Name.ToLowerInvariant().Contains(q) Then Return True
        If p.Tagline.ToLowerInvariant().Contains(q) Then Return True
        If p.Description.ToLowerInvariant().Contains(q) Then Return True
        Return False
    End Function

#End Region

#Region "卡片"

    Private Function BuildEmpty(text As String) As UIElement
        Dim card As New MyCard() With {.Margin = New Thickness(0, 0, 0, 12)}
        Dim pan As New StackPanel() With {.Margin = New Thickness(21, 18, 21, 20)}
        pan.Children.Add(New TextBlock With {
            .Text = text, .FontSize = 13.5,
            .Foreground = Res("ColorBrushGray3", Color.FromRgb(140, 140, 140))})
        card.Children.Add(pan)
        Return card
    End Function

    Private Function BuildProjectCard(p As ModStore.StoreProject) As UIElement
        Dim card As New MyCard() With {.Margin = New Thickness(0, 0, 0, 12)}
        Dim root As New Grid() With {.Margin = New Thickness(21, 15, 21, 16)}
        root.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = New GridLength(1, GridUnitType.Star)})
        root.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = GridLength.Auto})
        For i As Integer = 0 To 3
            root.RowDefinitions.Add(New RowDefinition() With {.Height = GridLength.Auto})
        Next

        ' ── 标题行：名称 + 标识 ──
        Dim head As New StackPanel() With {.Orientation = Orientation.Horizontal}
        head.Children.Add(New TextBlock With {
            .Text = If(String.IsNullOrWhiteSpace(p.Name), p.Slug, p.Name),
            .FontSize = 14.5, .FontWeight = FontWeights.Bold,
            .VerticalAlignment = VerticalAlignment.Center})
        If Not String.IsNullOrWhiteSpace(p.Tagline) Then
            head.Children.Add(New TextBlock With {
                .Text = p.Tagline, .FontSize = 12, .Margin = New Thickness(8, 0, 0, 0),
                .VerticalAlignment = VerticalAlignment.Center,
                .Foreground = Res("ColorBrushGray3", Color.FromRgb(140, 140, 140))})
        End If
        root.Children.Add(head)

        ' ── 描述 ──
        If Not String.IsNullOrWhiteSpace(p.Description) Then
            Dim labDesc As New TextBlock With {
                .Text = p.Description, .TextWrapping = TextWrapping.Wrap, .LineHeight = 19,
                .Margin = New Thickness(0, 8, 0, 0)}
            Grid.SetRow(labDesc, 1)
            root.Children.Add(labDesc)
        End If

        ' ── 统计 ──
        Dim meta As New List(Of String)
        If p.Downloads > 0 Then meta.Add(p.Downloads & " 次下载")
        If p.Files > 0 Then meta.Add(p.Files & " 个文件")
        If Not String.IsNullOrWhiteSpace(p.SizeHuman) Then meta.Add(p.SizeHuman)
        If Not String.IsNullOrWhiteSpace(p.Latest) Then meta.Add("更新于 " & p.Latest.Substring(0, Math.Min(10, p.Latest.Length)))
        Dim labMeta As New TextBlock With {
            .Text = If(meta.Count = 0, "", String.Join(" · ", meta)),
            .FontSize = 12, .Margin = New Thickness(0, 8, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis,
            .Foreground = Res("ColorBrushGray4", Color.FromRgb(166, 166, 166))}
        Grid.SetRow(labMeta, 2)
        root.Children.Add(labMeta)

        ' ── 按钮列 ──
        Dim btns As New StackPanel() With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center,
            .Margin = New Thickness(18, 0, 0, 0)}
        Dim curId As Integer = p.Id
        Dim btnImport As New MyButton With {
            .Text = "一键导入", .Height = 33, .MinWidth = 110, .Padding = New Thickness(12, 0, 12, 0),
            .ColorType = MyButton.ColorState.Highlight}
        AddHandler btnImport.Click, Sub(s, ev) ImportProject(curId)
        btns.Children.Add(btnImport)
        Grid.SetColumn(btns, 1)
        Grid.SetRowSpan(btns, 4)
        root.Children.Add(btns)

        card.Children.Add(root)
        Return card
    End Function

    Private Sub ImportProject(id As Integer)
        Dim p = FindProject(id)
        If p Is Nothing Then Return
        Dim name = If(String.IsNullOrWhiteSpace(p.Name), "拓展", p.Name)
        Hint("正在导入「" & name & "」……", Log:=False)
        RunInNewThread(
            Sub()
                Try
                    ModStore.DownloadAndInstall(p, Sub(s) RunInUi(Sub() Hint(s, Log:=False)))
                    RunInUi(Sub() Hint("已导入拓展：「" & name & "」，请在「拓展」页确认权限后启用。", Log:=False))
                Catch ex As Exception
                    RunInUi(Sub()
                                If ModSecret.IsNetworkIssue(ex) Then
                                    ModSecret.HintNetworkIssue()
                                Else
                                    MyMsgBox("导入拓展失败：" & ex.Message, "导入失败", "确定")
                                End If
                            End Sub)
                End Try
            End Sub, "拓展商店导入")
    End Sub

    Private Function FindProject(id As Integer) As ModStore.StoreProject
        For Each c In AllCats
            For Each p In c.Projects
                If p.Id = id Then Return p
            Next
        Next
        Return Nothing
    End Function

#End Region

    Private Shared Function Res(key As String, fallback As Color) As Brush
        Try
            Dim b As Brush = TryCast(System.Windows.Application.Current.TryFindResource(key), Brush)
            If b IsNot Nothing Then Return b
        Catch
        End Try
        Return New SolidColorBrush(fallback)
    End Function

End Class
