Imports System.Security.Cryptography
Imports System.Text

''' <summary>离线登录：用户名校验与离线 UUID（与原版 UUID.nameUUIDFromBytes("OfflinePlayer:"+name) 一致）。</summary>
Public Module McOffline

    ''' <summary>Java 版用户名规则：3-16 位，仅英文、数字、下划线。</summary>
    Public Function IsValidName(name As String) As Boolean
        If String.IsNullOrEmpty(name) Then Return False
        If name.Length < 3 OrElse name.Length > 16 Then Return False
        For Each c As Char In name
            Dim ok As Boolean = (c >= "a"c AndAlso c <= "z"c) OrElse
                                (c >= "A"c AndAlso c <= "Z"c) OrElse
                                (c >= "0"c AndAlso c <= "9"c) OrElse
                                c = "_"c
            If Not ok Then Return False
        Next
        Return True
    End Function

    ''' <summary>由用户名生成离线 UUID（MD5，版本 3，变体 10xx）。</summary>
    Public Function UuidOf(name As String) As String
        Dim src = Encoding.UTF8.GetBytes("OfflinePlayer:" & If(name, ""))
        Using md = MD5.Create()
            Dim h = md.ComputeHash(src)
            h(6) = CByte((h(6) And &H0F) Or &H30)
            h(8) = CByte((h(8) And &H3F) Or &H80)
            Dim s = BitConverter.ToString(h).Replace("-", "").ToLowerInvariant()
            Return s.Substring(0, 8) & "-" & s.Substring(8, 4) & "-" & s.Substring(12, 4) & "-" &
                   s.Substring(16, 4) & "-" & s.Substring(20, 12)
        End Using
    End Function

End Module
