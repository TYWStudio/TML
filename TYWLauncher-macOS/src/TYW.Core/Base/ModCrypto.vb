Imports System.Security.Cryptography
Imports System.Text

''' <summary>
''' 与服务端 TYWServer\NetCrypto.vb 对称的封包解密。
''' 协议不变：只在请求头带 X-TYW-Enc: 1 时，响应体换成 TYW1 封包。
''' 封包："TYW1"(4) | IV(16) | AES-256-CBC 密文 | HMAC-SHA256(32)
''' </summary>
Public Module ModCrypto

    Private ReadOnly Magic As Byte() = Encoding.ASCII.GetBytes("TYW1")

    Private ReadOnly MasterKey As Byte() = Convert.FromBase64String("E3P5nVbLKOIi/2Cb47FIglFldry6+i0jlzNmL6ZWedc=")

    Private ReadOnly KeyEnc As Byte() = Derive("TYW-ENC")
    Private ReadOnly KeyMac As Byte() = Derive("TYW-MAC")

    Private Function Derive(Label As String) As Byte()
        Using Sha As SHA256 = SHA256.Create()
            Dim LabelBytes = Encoding.UTF8.GetBytes(Label)
            Dim Buf(MasterKey.Length + LabelBytes.Length - 1) As Byte
            System.Buffer.BlockCopy(MasterKey, 0, Buf, 0, MasterKey.Length)
            System.Buffer.BlockCopy(LabelBytes, 0, Buf, MasterKey.Length, LabelBytes.Length)
            Return Sha.ComputeHash(Buf)
        End Using
    End Function

    Public Function LooksEncrypted(packet As Byte()) As Boolean
        If packet Is Nothing OrElse packet.Length < 4 + 16 + 16 + 32 Then Return False
        For i = 0 To Magic.Length - 1
            If packet(i) <> Magic(i) Then Return False
        Next
        Return True
    End Function

    Public Function Decrypt(packet As Byte()) As Byte()
        Try
            If Not LooksEncrypted(packet) Then Return Nothing

            Dim BodyLen = packet.Length - 32
            Dim Tag(31) As Byte
            System.Buffer.BlockCopy(packet, BodyLen, Tag, 0, 32)

            Dim Expect As Byte()
            Using Hmac As New HMACSHA256(KeyMac)
                Dim Body(BodyLen - 1) As Byte
                System.Buffer.BlockCopy(packet, 0, Body, 0, BodyLen)
                Expect = Hmac.ComputeHash(Body)
            End Using
            Dim Diff As Integer = 0
            For i = 0 To 31
                Diff = Diff Or (Tag(i) Xor Expect(i))
            Next
            If Diff <> 0 Then Return Nothing

            Dim Iv(15) As Byte
            System.Buffer.BlockCopy(packet, Magic.Length, Iv, 0, 16)
            Dim CipherLen = BodyLen - Magic.Length - 16
            Dim Cipher(CipherLen - 1) As Byte
            System.Buffer.BlockCopy(packet, Magic.Length + 16, Cipher, 0, CipherLen)

            Using Aes As Aes = Aes.Create()
                Aes.KeySize = 256
                Aes.Mode = CipherMode.CBC
                Aes.Padding = PaddingMode.PKCS7
                Aes.Key = KeyEnc
                Aes.IV = Iv
                Using Dec As ICryptoTransform = Aes.CreateDecryptor()
                    Return Dec.TransformFinalBlock(Cipher, 0, Cipher.Length)
                End Using
            End Using
        Catch
            Return Nothing
        End Try
    End Function

End Module
