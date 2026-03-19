Imports System.Text.Json.Nodes


Public Structure NeteaseCloudMusicMetadata
        Public Album As String
        Public Artist As List(Of String)
        Public Format As String
        Public Name As String
        Public Duration As Long
        Public Bitrate As Long
        Public Description As String

        Public Sub New(meta As String)
            Album = ""
            Artist = New List(Of String)(5)
            Format = ""
            Name = ""
            Duration = 0
            Bitrate = 0
            Description = ""

            If String.IsNullOrEmpty(meta) Then Return

            Dim json As JsonObject = TryCast(JsonNode.Parse(meta), JsonObject)
            If json IsNot Nothing Then
                Dim musicNameNode = json("musicName")
                If musicNameNode IsNot Nothing Then Name = musicNameNode.GetValue(Of String)()

                Dim albumNode = json("album")
                If albumNode IsNot Nothing Then Album = albumNode.GetValue(Of String)()

                Dim artistsNode = TryCast(json("artist"), JsonArray)
                If artistsNode IsNot Nothing AndAlso artistsNode.Count > 0 Then
                    For i As Integer = 0 To artistsNode.Count - 1
                        Dim array = TryCast(artistsNode(i), JsonArray)
                        If array IsNot Nothing AndAlso array.Count > 0 Then
                            Artist.Add(If(array(0)?.GetValue(Of String)(), ""))
                        End If
                    Next
                End If

                ' 【修复Bug】将获取的数值强制转为 Long，防止像 2655774465 这种超大 ID 导致崩溃
                Dim bitrateNode = json("bitrate")
                If bitrateNode IsNot Nothing Then Bitrate = bitrateNode.GetValue(Of Long)()

                Dim durationNode = json("duration")
                If durationNode IsNot Nothing Then Duration = durationNode.GetValue(Of Long)()

                Dim formatNode = json("format")
                If formatNode IsNot Nothing Then Format = formatNode.GetValue(Of String)()
            End If
        End Sub
    End Structure
