Imports System.IO
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json.Nodes


Public Class NeteaseCloudMusicStream
        Inherits Stream
        Implements TagLib.File.IFileAbstraction

        Public Shared ReadOnly CoreKey As Byte() = {&H68, &H7A, &H48, &H52, &H41, &H6D, &H73, &H6F, &H35, &H6B, &H49, &H6E, &H62, &H61, &H78, &H57, 0}
        Public Shared ReadOnly ModifyKey As Byte() = {&H23, &H31, &H34, &H6C, &H6A, &H6B, &H5F, &H21, &H5C, &H5D, &H26, &H30, &H55, &H3C, &H27, &H28, 0}

        Public Property FilePath As String

        Public Enum NcmFormat
            Mp3
            Flac
        End Enum

        Public Property Format As NcmFormat
        Public Property ImageData As Byte()
        Private ReadOnly _rawStream As Stream
        Private _outputStream As FileStream = Nothing
        Public ReadOnly Property KeyBox As Byte() = New Byte(255) {}
        Public Property Metadata As NeteaseCloudMusicMetadata?
        Public Property AlbumPicUrl As String

        Private _decryptedData As List(Of Byte) = Nothing
        Private _position As Long = 0
        Private _rawStreamOffset As Long = 0

        Public Overrides ReadOnly Property CanRead As Boolean = True
        Public Overrides ReadOnly Property CanSeek As Boolean = True
        Public Overrides ReadOnly Property CanWrite As Boolean = True

        Public Overrides ReadOnly Property Length As Long
            Get
                If _decryptedData IsNot Nothing Then
                    Return _decryptedData.Count
                ElseIf _outputStream IsNot Nothing Then
                    Return _outputStream.Length
                Else
                    Return _rawStream.Length - _rawStreamOffset
                End If
            End Get
        End Property

        Public Overrides Property Position As Long
            Get
                If _decryptedData IsNot Nothing Then
                    Return _position
                ElseIf _outputStream IsNot Nothing Then
                    Return _outputStream.Position
                Else
                    Return _rawStream.Position - _rawStreamOffset
                End If
            End Get
            Set(value As Long)
                If _outputStream IsNot Nothing Then
                    _outputStream.Position = value
                ElseIf _decryptedData Is Nothing Then
                    _rawStream.Position = value + _rawStreamOffset
                End If
                _position = value
            End Set
        End Property

        Public ReadOnly Property Name As String Implements TagLib.File.IFileAbstraction.Name
            Get
                Return If(FilePath Is Nothing, "ncm" & Format.ToString().ToLower(), Path.GetFileNameWithoutExtension(FilePath) & "." & Format.ToString().ToLower())
            End Get
        End Property

        Public ReadOnly Property ReadStream As Stream Implements TagLib.File.IFileAbstraction.ReadStream
            Get
                Return Me
            End Get
        End Property

        Public ReadOnly Property WriteStream As Stream Implements TagLib.File.IFileAbstraction.WriteStream
            Get
                Return Me
            End Get
        End Property

        ' 【修复】取消使用 Span，改为经典的 offset 和 count
        Private Function ReadRaw(buffer As Byte(), offset As Integer, count As Integer) As Integer
            Return _rawStream.Read(buffer, offset, count)
        End Function

        Private Function IsNcmFile() As Boolean
            Dim header As Byte() = New Byte(3) {}
            Return ReadRaw(header, 0, 4) = 4 AndAlso BitConverter.ToUInt32(header, 0) = &H4E455443 AndAlso
                   ReadRaw(header, 0, 4) = 4 AndAlso BitConverter.ToUInt32(header, 0) = &H4D414446
        End Function

        Private Sub BuildKeyBox(key As Byte(), keyLen As Integer)
            For i As Integer = 0 To 255
                KeyBox(i) = CByte(i)
            Next

            Dim lastByte As Byte = 0
            Dim keyOffset As Byte = 0

            For i As Integer = 0 To 255
                Dim swap As Byte = KeyBox(i)
                Dim c As Byte = CByte((CInt(swap) + lastByte + key(keyOffset)) And &HFF)
                keyOffset += 1
                If keyOffset >= keyLen Then keyOffset = 0

                KeyBox(i) = KeyBox(c)
                KeyBox(c) = swap
                lastByte = c
            Next
        End Sub

        ' 【修复】取消使用 Span，改为经典的数组传参
        Private Sub Decrypt(buffer As Byte(), offset As Integer, count As Integer)
            For i As Integer = 0 To count - 1
                Dim j As Integer = (i + 1) And &HFF
                buffer(offset + i) = buffer(offset + i) Xor KeyBox((CInt(KeyBox(j)) + KeyBox((CInt(KeyBox(j)) + j) And &HFF)) And &HFF)
            Next
        End Sub

        Public Sub DumpToMemory()
            Dim buffer As Byte() = New Byte(&H7FFF) {}
            _decryptedData = New List(Of Byte)()
            Dim currentPosition = Position

            While True
                Dim n As Integer
                Try
                    n = Read(buffer, 0, buffer.Length)
                Catch e As EndOfStreamException
                    Exit While
                End Try
                If n = 0 Then Exit While

                Dim readData(n - 1) As Byte
                Array.Copy(buffer, 0, readData, 0, n)
                _decryptedData.AddRange(readData)
            End While
            Position = currentPosition
        End Sub

        Public Sub DumpToFile(path As String, name As String)
            Dim output As FileStream
            Try
                Directory.CreateDirectory(path)
                output = File.Create(IO.Path.Join(path, $"{name}.{Format.ToString().ToLower()}"))
            Catch ex As Exception
                Throw New Exception($"create output file failed at ""{path}""")
            End Try

            Dim buffer As Byte() = New Byte(&H7FFF) {}
            Dim currentPosition = Position

            While True
                Dim n As Integer
                Try
                    n = Read(buffer, 0, buffer.Length)
                    If n = 0 Then Exit While
                Catch e As EndOfStreamException
                    Exit While
                End Try
                output.Write(buffer, 0, n)
            End While

            Position = currentPosition
            _outputStream = output
            _outputStream.Flush()
        End Sub

        Public Async Function DumpToFileAsync(path As String, name As String) As Task
            Dim output As FileStream
            Try
                Directory.CreateDirectory(path)
                output = File.Create(IO.Path.Join(path, $"{name}.{Format.ToString().ToLower()}"))
            Catch ex As Exception
                Throw New Exception($"create output file failed at ""{path}""")
            End Try

            Dim buffer As Byte() = New Byte(&H7FFF) {}
            Dim currentPosition = Position

            While True
                Dim n As Integer
                Try
                    n = Await ReadAsync(buffer, 0, buffer.Length)
                    If n = 0 Then Exit While
                Catch e As EndOfStreamException
                    Exit While
                End Try
                Await output.WriteAsync(buffer, 0, n)
            End While

            Position = currentPosition
            _outputStream = output
            Await _outputStream.FlushAsync()
        End Function

        Public Sub FixMetadata(fetchAlbumImageFromRemote As Boolean)
            If (ImageData Is Nothing OrElse ImageData.Length <= 0) AndAlso fetchAlbumImageFromRemote Then
                Try
                    Using client As New HttpClient()
                        Dim response = client.GetAsync(AlbumPicUrl).Result
                        If response IsNot Nothing AndAlso response.IsSuccessStatusCode Then
                            ImageData = response.Content.ReadAsByteArrayAsync().Result
                        End If
                    End Using
                Catch e As Exception
                    Throw New Exception("fetch album image failed", e)
                End Try
            End If

            Using tfile = TagLib.File.Create(Me)
                tfile.Tag.Title = Metadata?.Name
                tfile.Tag.Performers = Metadata?.Artist?.ToArray()
                tfile.Tag.Album = Metadata?.Album
                tfile.Tag.Description = Metadata?.Description

                If ImageData IsNot Nothing AndAlso ImageData.Length > 0 Then
                    Dim pic As New TagLib.Picture(ImageData)
                    tfile.Tag.Pictures = New TagLib.IPicture() {pic}
                End If
                Try
                    tfile.Save()
                Catch e As Exception
                    Throw New Exception("save metadata failed", e)
                End Try
            End Using
        End Sub

        Private Sub Initalize()
            If _rawStream Is Nothing OrElse Not IsNcmFile() Then
                Throw New Exception("not a ncm file")
            End If

            _rawStream.Seek(2, SeekOrigin.Current)

            Dim n As Byte() = New Byte(3) {}
            If ReadRaw(n, 0, 4) <> 4 Then Throw New Exception("read key len failed")

            Dim keyLen = CInt(BitConverter.ToUInt32(n, 0))
            Dim keyData As Byte() = New Byte(keyLen - 1) {}
            ReadRaw(keyData, 0, keyLen)

            For i As Integer = 0 To keyData.Length - 1
                keyData(i) = keyData(i) Xor &H64
            Next

            Dim decryptedKeyData As Byte()
            Try
                decryptedKeyData = AesEcbDecrypt(CoreKey.Take(16).ToArray(), keyData)
            Catch e As Exception
                Throw New Exception("decrypt key failed", e)
            End Try

            BuildKeyBox(decryptedKeyData.Skip(17).ToArray(), decryptedKeyData.Length - 17)

            If ReadRaw(n, 0, 4) <> 4 Then Throw New Exception("read metadata len failed")

            Dim metadataLen = CInt(BitConverter.ToUInt32(n, 0))

            If metadataLen <= 0 Then
                Metadata = Nothing
            Else
                Dim modifyData As Byte() = New Byte(metadataLen - 1) {}
                ReadRaw(modifyData, 0, metadataLen)

                For i As Integer = 0 To modifyData.Length - 1
                    modifyData(i) = modifyData(i) Xor &H63
                Next

                Dim descriptionData = Encoding.UTF8.GetString(modifyData)
                Dim swapModifyData = descriptionData.Substring(22)

                Dim modifyOutData As Byte()
                Try
                    modifyOutData = Convert.FromBase64String(swapModifyData)
                Catch ex As Exception
                    Throw New Exception("base64 decode modify data failed")
                End Try

                Dim modifyDecryptData As Byte()
                Try
                    modifyDecryptData = AesEcbDecrypt(ModifyKey.Take(16).ToArray(), modifyOutData)
                Catch ex As Exception
                    Throw New Exception("decrypt modify data failed")
                End Try

                Dim metadataString = Encoding.UTF8.GetString(modifyDecryptData.Skip(6).ToArray())
                AlbumPicUrl = GetAlbumPicUrl(metadataString)

                Metadata = New NeteaseCloudMusicMetadata(metadataString) With {
                    .Description = descriptionData
                }
            End If

            Try
                _rawStream.Seek(5, SeekOrigin.Current)
            Catch ex As Exception
                Throw New Exception("seek gap failed")
            End Try

            Dim coverFrameLen As Byte() = New Byte(3) {}
            If ReadRaw(coverFrameLen, 0, 4) <> 4 Then Throw New Exception("read cover frame len failed")
            If ReadRaw(n, 0, 4) <> 4 Then Throw New Exception("read cover frame data len failed")

            Dim coverFrameLenInt = CInt(BitConverter.ToUInt32(coverFrameLen, 0))
            Dim coverFrameDataLen = CInt(BitConverter.ToUInt32(n, 0))

            If coverFrameDataLen > 0 Then
                ImageData = New Byte(coverFrameDataLen - 1) {}
                ReadRaw(ImageData, 0, coverFrameDataLen)
            End If

            _rawStream.Seek(coverFrameLenInt - coverFrameDataLen, SeekOrigin.Current)
            _rawStreamOffset = _rawStream.Position

            Dim buffer As Byte() = New Byte(2) {}
            ' 【修复】：使用 Read 来读取解密后的数据，才能正确识别 ID3 (MP3) 标识
            If Read(buffer, 0, 3) <> 3 Then Throw New Exception("read format failed")

            If buffer(0) = &H49 AndAlso buffer(1) = &H44 AndAlso buffer(2) = &H33 Then
                Format = NcmFormat.Mp3
            Else
                Format = NcmFormat.Flac
            End If
            _rawStream.Seek(-3, SeekOrigin.Current)
        End Sub

        Public Sub New(filePath As String)
            Me.FilePath = filePath
            Try
                _rawStream = File.OpenRead(Me.FilePath)
            Catch e As Exception
                Throw New Exception("open file failed", e)
            End Try
            Initalize()
        End Sub

        Public Sub New(stream As Stream)
            _rawStream = stream
            Initalize()
        End Sub

        Private Shared Function GetAlbumPicUrl(meta As String) As String
            Dim json = TryCast(JsonNode.Parse(meta), JsonObject)
            Return json?("albumPic")?.ToString()
        End Function

        Private Shared Function AesEcbDecrypt(key As Byte(), src As Byte()) As Byte()
            Using aesAlg = Aes.Create()
                aesAlg.Mode = CipherMode.ECB
                aesAlg.Key = key
                aesAlg.Padding = PaddingMode.PKCS7

                Using decryptor = aesAlg.CreateDecryptor()
                    Using memoryStream As New MemoryStream(src)
                        Using cryptoStream As New CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read)
                            Using resultStream As New MemoryStream()
                                cryptoStream.CopyTo(resultStream)
                                Return resultStream.ToArray()
                            End Using
                        End Using
                    End Using
                End Using
            End Using
        End Function

        Public Overrides Sub Flush()
        End Sub

        ' 【修复】保留了最标准的 Stream Read 覆盖，移除了报错的 Span 重写
        Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
            Dim n As Integer
            If _decryptedData IsNot Nothing Then
                If _position + count > _decryptedData.Count Then
                    n = _decryptedData.Count - CInt(Position)
                Else
                    n = count
                End If

                If n > 0 Then
                    _decryptedData.CopyTo(CInt(Position), buffer, offset, n)
                    Position += n
                Else
                    Throw New EndOfStreamException()
                End If
            ElseIf _outputStream IsNot Nothing Then
                n = _outputStream.Read(buffer, offset, count)
            Else
                n = ReadRaw(buffer, offset, count)
                Decrypt(buffer, offset, n)
            End If
            Return n
        End Function

        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Select Case origin
                Case SeekOrigin.Begin
                    Position = offset
                Case SeekOrigin.Current
                    Position += offset
                Case SeekOrigin.End
                    Position = Length + offset
            End Select
            Return Position
        End Function

        Public Overrides Sub SetLength(value As Long)
            If _outputStream IsNot Nothing Then
                _outputStream.SetLength(value)
            Else
                If _decryptedData Is Nothing Then DumpToMemory()
                If Length > value Then
                    _decryptedData.RemoveRange(CInt(value), CInt(Length - value))
                ElseIf Length < value Then
                    _decryptedData.AddRange(New Byte(CInt(value - Length - 1)) {})
                End If
            End If
        End Sub

        ' 【修复】保留标准的 Stream Write 覆盖
        Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
            If _outputStream IsNot Nothing Then
                _outputStream.Write(buffer, offset, count)
            ElseIf _decryptedData IsNot Nothing Then
                Dim writeData(count - 1) As Byte
                Array.Copy(buffer, offset, writeData, 0, count)
                _decryptedData.AddRange(writeData)
            Else
                DumpToMemory()
                Dim writeData(count - 1) As Byte
                Array.Copy(buffer, offset, writeData, 0, count)
                _decryptedData.AddRange(writeData)
            End If
        End Sub

        Public Sub CloseStream(stream As Stream) Implements TagLib.File.IFileAbstraction.CloseStream
            ' 空实现
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _outputStream?.Dispose()
                _rawStream?.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub
    End Class
