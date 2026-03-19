Imports System.IO
Imports System.Text.Json

' 1. 定义配置的数据结构
Public Class AppConfig
    Public Property NamingFormat As String = "%s-%a"
    Public Property ThemeIndex As Integer = 0    ' 0: 明亮, 1: 暗黑
    Public Property LanguageIndex As Integer = 0 ' 0: 中文, 1: 英文
End Class

' 2. 负责读写 JSON 文件的管理器
Public Class ConfigManager

    ' 【修改这里】：动态获取 AppData 路径，并自动创建专属文件夹
    Private Shared ReadOnly Property ConfigPath As String
        Get
            ' 1. 获取 Windows 系统的 AppData\Roaming 隐藏目录
            Dim appDataPath As String = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)

            ' 2. 在里面为你的软件创建一个专属文件夹
            Dim myAppFolder As String = Path.Combine(appDataPath, "NcmMaster")

            ' 3. 如果文件夹不存在，则自动创建它
            If Not Directory.Exists(myAppFolder) Then
                Directory.CreateDirectory(myAppFolder)
            End If

            ' 4. 返回最终的配置文件完整路径
            Return Path.Combine(myAppFolder, "config.json")
        End Get
    End Property

    Public Shared Function Load() As AppConfig
        Try
            If File.Exists(ConfigPath) Then
                Dim json = File.ReadAllText(ConfigPath)
                Return JsonSerializer.Deserialize(Of AppConfig)(json)
            End If
        Catch
            ' 如果读取失败（比如格式错误），就默默返回默认配置
        End Try
        Return New AppConfig()
    End Function

    Public Shared Sub Save(config As AppConfig)
        Try
            ' 开启缩进，让生成的 JSON 文件像代码一样排版整齐，方便人类阅读
            Dim options As New JsonSerializerOptions With {.WriteIndented = True}
            Dim json = JsonSerializer.Serialize(config, options)
            File.WriteAllText(ConfigPath, json)
        Catch
        End Try
    End Sub
End Class