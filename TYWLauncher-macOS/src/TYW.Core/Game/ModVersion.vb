Imports System.IO
Imports System.Text.Json

''' <summary>本地已安装的版本。</summary>
Public Class McVersionItem
    Public Property Id As String = ""
    Public Property Type As String = ""
    Public Property Folder As String = ""
    Public Property JsonPath As String = ""

    Public ReadOnly Property Display As String
        Get
            Return Id
        End Get
    End Property
End Class

''' <summary>
''' 扫描 ~/.minecraft/versions。对应 Windows 版 McInstance 的版本识别。
''' </summary>
Public Module ModVersion

    Private _cache As List(Of McVersionItem) = Nothing

    ''' <summary>列出全部已安装的 Java 版实例（不含基岩版，Linux 版不支持基岩版）。</summary>
    Public Function ListVersions(Optional force As Boolean = False) As List(Of McVersionItem)
        If _cache IsNot Nothing AndAlso Not force Then Return _cache
        Dim result As New List(Of McVersionItem)
        Try
            Dim dir = Path.Combine(ModPaths.McFolder, "versions")
            If Directory.Exists(dir) Then
                For Each subDir In Directory.GetDirectories(dir)
                    Dim verName = Path.GetFileName(subDir)
                    Dim json = Path.Combine(subDir, verName & ".json")
                    If Not File.Exists(json) Then Continue For
                    Dim item As New McVersionItem With {.Id = verName, .Folder = subDir, .JsonPath = json}
                    item.Type = ReadType(json)
                    ' 基岩版实例（Windows 端写入的伪造 JSON）在 Linux 上没有意义，跳过
                    If item.Type = "bedrock" Then Continue For
                    result.Add(item)
                Next
            End If
        Catch ex As Exception
            ModLog.Err(ex, "扫描版本")
        End Try
        result.Sort(Function(a, b) String.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase))
        _cache = result
        Return result
    End Function

    Private Function ReadType(jsonPath As String) As String
        Try
            Using doc = JsonDocument.Parse(File.ReadAllText(jsonPath))
                Dim root = doc.RootElement
                Dim bedrock As JsonElement
                If root.TryGetProperty("bedrock", bedrock) Then Return "bedrock"
                Dim tEl As JsonElement
                If root.TryGetProperty("type", tEl) Then Return tEl.GetString()
            End Using
        Catch
        End Try
        Return "unknown"
    End Function

    ''' <summary>取上次选择的版本；不存在时返回第一个。</summary>
    Public Function SelectedOrDefault() As McVersionItem
        Dim list = ListVersions()
        If list.Count = 0 Then Return Nothing
        Dim sel = ModSettings.GetString("VersionSelect")
        If Not String.IsNullOrEmpty(sel) Then
            Dim hit = list.FirstOrDefault(Function(v) v.Id = sel)
            If hit IsNot Nothing Then Return hit
        End If
        Return list(0)
    End Function

    ''' <summary>
    ''' 该实例的游戏目录。按「默认版本隔离」设置决定：
    ''' 0 = 不隔离（.minecraft）；1 = 仅正式版隔离；2 = 全部隔离；隔离时为 versions/&lt;id&gt;。
    ''' </summary>
    Public Function GameDirOf(item As McVersionItem) As String
        If item Is Nothing Then Return ModPaths.McFolder
        Dim mode = ModSettings.GetInt("LaunchIsolation", 0)
        If mode <= 0 Then Return ModPaths.McFolder
        If mode = 1 AndAlso Not String.Equals(item.Type, "release", StringComparison.OrdinalIgnoreCase) Then
            Return ModPaths.McFolder
        End If
        Return Path.Combine(ModPaths.McFolder, "versions", item.Id)
    End Function

    ''' <summary>实例的 Mods 目录：优先用实例内的 mods，否则用共享的 mods。</summary>
    Public Function ModsDirOf(item As McVersionItem) As String
        If item IsNot Nothing Then
            Dim own = Path.Combine(ModPaths.McFolder, "versions", item.Id, "mods")
            If Directory.Exists(own) Then Return own
        End If
        Return Path.Combine(ModPaths.McFolder, "mods")
    End Function

    ''' <summary>实例的存档目录：优先用实例内的 saves，否则用共享的 saves。</summary>
    Public Function SavesDirOf(item As McVersionItem) As String
        If item IsNot Nothing Then
            Dim own = Path.Combine(ModPaths.McFolder, "versions", item.Id, "saves")
            If Directory.Exists(own) Then Return own
        End If
        Return Path.Combine(ModPaths.McFolder, "saves")
    End Function

    ''' <summary>
    ''' 实例图标资源名（对应 Windows 版 McInstanceState → Images/Blocks/*.png）。
    ''' 基岩版 Bedrock、Fabric/Forge/NeoForge/OptiFine 各自方块，纯原版按版本类型。
    ''' </summary>
    Public Function IconOf(item As McVersionItem) As String
        If item Is Nothing Then Return "RedstoneBlock"
        Try
            If Not File.Exists(item.JsonPath) Then Return "RedstoneBlock"
            Using doc = JsonDocument.Parse(File.ReadAllBytes(item.JsonPath))
                Dim root = doc.RootElement
                Dim tmp As JsonElement
                If root.TryGetProperty("bedrock", tmp) Then Return "Bedrock"

                Dim isSnapshot = String.Equals(item.Type, "snapshot", StringComparison.OrdinalIgnoreCase)
                Dim isOld = item.Type IsNot Nothing AndAlso item.Type.StartsWith("old", StringComparison.OrdinalIgnoreCase)

                Dim hasFabric As Boolean = False
                Dim hasForge As Boolean = False
                Dim hasNeoForge As Boolean = False
                Dim hasOptiFine As Boolean = False
                Dim hasLiteLoader As Boolean = False
                Dim libs As JsonElement
                If root.TryGetProperty("libraries", libs) Then
                    For Each libEl In libs.EnumerateArray()
                        Dim nm As JsonElement
                        If Not libEl.TryGetProperty("name", nm) Then Continue For
                        Dim name = If(nm.GetString(), "")
                        If name.Contains("fabric-loader") OrElse name.Contains("fabricmc:intermediary") Then hasFabric = True
                        If name.Contains("net.minecraftforge:forge") Then hasForge = True
                        If name.Contains("net.neoforged") Then hasNeoForge = True
                        If name.Contains("optifine:OptiFine") Then hasOptiFine = True
                        If name.Contains("liteloader") Then hasLiteLoader = True
                    Next
                End If
                ' 加载器优先于版本类型（与 Windows 版 McInstanceState 判定顺序一致）
                If hasFabric Then Return "Fabric"
                If hasNeoForge Then Return "NeoForge"
                If hasForge Then Return "Anvil"
                If hasOptiFine Then Return "GrassPath"
                If hasLiteLoader Then Return "Egg"
                If isOld Then Return "CobbleStone"
                If isSnapshot Then Return "CommandBlock"
                Return "Grass"
            End Using
        Catch
            Return "RedstoneBlock"
        End Try
    End Function

    Public Sub Invalidate()
        _cache = Nothing
    End Sub

End Module
