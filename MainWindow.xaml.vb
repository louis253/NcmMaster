Imports System.Collections.ObjectModel
Imports System.IO
Imports System.Windows.Media.Imaging
Imports Myitian
Imports System.Windows.Media ' 增加这行用于引入 MediaPlayer
Imports System.Net.Http
Imports System.Text.Json
Imports System.Diagnostics

Class MainWindow
    Public Property MusicItems As New ObservableCollection(Of MusicItem)
    Private _currentConfig As AppConfig ' 用于存储当前的配置项
    Private _customOutputDir As String = "" ' 自定义输出目录，为空则存在原文件夹
    ' --- 播放器与临时目录声明 ---
    Private WithEvents _player As New MediaPlayer()
    Private _currentlyPlayingItem As MusicItem = Nothing
    Private _tempAudioFolder As String = IO.Path.Combine(IO.Path.GetTempPath(), "NcmMasterPreview")
    ' --- 软件版本与 GitHub 配置 ---
    ' --- 动态获取程序集版本号 ---
    Private ReadOnly Property AppVersion As String
        Get
            ' 自动读取项目属性里设置的 AssemblyVersion
            Dim version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
            ' 返回格式如 "1.0.1" (忽略末尾的 .0)
            Return $"{version.Major}.{version.Minor}.{version.Build}"
        End Get
    End Property
    Private _latestReleaseUrl As String = "https://github.com/louis253/NcmMaster/releases/latest"

    Public Sub New()
        InitializeComponent()
        MusicList.ItemsSource = MusicItems

        ' 【增强版】：启动时先清理一次，防止上次崩溃留下的残余
        Try
            If Directory.Exists(_tempAudioFolder) Then Directory.Delete(_tempAudioFolder, True)
        Catch
            ' 忽略占用中的文件夹
        End Try

        ' 重新创建干净的文件夹
        If Not Directory.Exists(_tempAudioFolder) Then Directory.CreateDirectory(_tempAudioFolder)

        '加载配置文件并应用到界面
        _currentConfig = ConfigManager.Load()
        TxtNameFormat.Text = _currentConfig.NamingFormat
        CmbTheme.SelectedIndex = _currentConfig.ThemeIndex
        CmbLanguage.SelectedIndex = _currentConfig.LanguageIndex

        ' 初始化主题
        ApplyThemeAndLanguage(_currentConfig.ThemeIndex = 1, _currentConfig.LanguageIndex)

        ' 【新增】：初始化界面版本号，并异步检查更新
        TxtCurrentVersion.Text = $"版本 v{AppVersion}"
        CheckForUpdatesAsync()

    End Sub

    ' --- 窗口基础控制 ---
    Private Sub TitleBar_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        If e.LeftButton = MouseButtonState.Pressed Then Me.DragMove()
    End Sub
    Private Sub BtnMinimize_Click(sender As Object, e As RoutedEventArgs)
        Me.WindowState = WindowState.Minimized
    End Sub
    Private Sub BtnClose_Click(sender As Object, e As RoutedEventArgs)
        Me.Close()
    End Sub

    ' --- 步骤 1：添加文件到列表（支持拖拽与按钮） ---
    Private Sub DropArea_DragOver(sender As Object, e As DragEventArgs)
        If e.Data.GetDataPresent(DataFormats.FileDrop) Then e.Effects = DragDropEffects.Copy
        e.Handled = True
    End Sub

    Private Sub DropArea_Drop(sender As Object, e As DragEventArgs)
        Dim files As String() = TryCast(e.Data.GetData(DataFormats.FileDrop), String())
        If files IsNot Nothing Then AddFilesToList(files)
    End Sub

    Private Sub BtnAddFiles_Click(sender As Object, e As RoutedEventArgs)
        Dim dlg As New Microsoft.Win32.OpenFileDialog() With {
            .Multiselect = True,
            .Filter = "NCM 音乐文件 (*.ncm)|*.ncm",
            .Title = "选择需要转换的音乐"
        }
        If dlg.ShowDialog() = True Then AddFilesToList(dlg.FileNames)
    End Sub

    Private Sub BtnAddFolder_Click(sender As Object, e As RoutedEventArgs)
        ' 这是 .NET 8 原生的选择文件夹对话框，非常现代
        Dim dlg As New Microsoft.Win32.OpenFolderDialog() With {
            .Title = "选择包含 NCM 的文件夹"
        }
        If dlg.ShowDialog() = True Then
            Dim files = Directory.GetFiles(dlg.FolderName, "*.ncm", SearchOption.AllDirectories)
            AddFilesToList(files)
        End If
    End Sub

    Private Sub BtnChangeOutputDir_Click(sender As Object, e As RoutedEventArgs)
        Dim dlg As New Microsoft.Win32.OpenFolderDialog() With {
            .Title = "选择转换后的保存目录"
        }
        If dlg.ShowDialog() = True Then
            _customOutputDir = dlg.FolderName
            TxtOutputDir.Text = _customOutputDir
        End If
    End Sub

    ' --- 核心辅助：静默解析歌曲信息并上屏 ---
    Private Sub AddFilesToList(files As String())


        For Each path In files
            ' 防止重复添加
            If MusicItems.Any(Function(x) x.FilePath = path) Then Continue For

            Dim item As New MusicItem() With {
                .FilePath = path,
                .FileName = IO.Path.GetFileNameWithoutExtension(path),
                .StatusKey = "Lang_StatusWait",
                .Status = GetLoc("Lang_StatusWait"),
                .StatusColor = "#999999",
                .Album = GetLoc("Lang_Parsing")
            }
            MusicItems.Add(item)

            ' 开启后台线程快速读取一次元数据（只读封面和歌名，不解密全文）
            Task.Run(Sub()
                         Try
                             Dim tempStream As New NeteaseCloudMusicStream(item.FilePath)
                             Dim bmp As BitmapImage = Nothing

                             If tempStream.ImageData IsNot Nothing AndAlso tempStream.ImageData.Length > 0 Then
                                 bmp = BytesToBitmapImage(tempStream.ImageData)
                             End If

                             ' 切回 UI 线程更新界面
                             Dispatcher.Invoke(Sub()
                                                   If tempStream.Metadata IsNot Nothing Then
                                                       item.FileName = tempStream.Metadata?.Name
                                                       item.Album = tempStream.Metadata?.Album

                                                       ' 新增：提取并拼接演唱者名字
                                                       If tempStream.Metadata?.Artist IsNot Nothing AndAlso tempStream.Metadata?.Artist.Count > 0 Then
                                                           item.Artist = String.Join(" / ", tempStream.Metadata?.Artist)
                                                       Else
                                                           item.Artist = GetLoc("Lang_UnknownArtist")
                                                       End If
                                                   End If
                                                   If bmp IsNot Nothing Then item.CoverImage = bmp
                                               End Sub)
                             tempStream.Dispose() ' 读完立刻释放，不占用文件
                         Catch ex As Exception
                             Dispatcher.Invoke(Sub() item.Album = GetLoc("Lang_ParseFail"))
                         End Try
                     End Sub)
        Next
        ' 【新增】：文件添加完后，统一更新一次界面状态
        UpdateUIState()

    End Sub

    ' --- 步骤 2：点击开始批量转换 ---
    Private Async Sub BtnStartConvert_Click(sender As Object, e As RoutedEventArgs)
        ' 【新增】：1. 提前筛选出所有需要转换的歌曲
        Dim itemsToConvert = MusicItems.Where(Function(x) x.IsSelected AndAlso (x.Status = GetLoc("Lang_StatusWait") Or x.Status = GetLoc("Lang_StatusFail"))).ToList()

        ' 【新增】：2. 如果一首需要转换的歌都没有，直接拦截并提示
        If itemsToConvert.Count = 0 Then
            MessageBox.Show(GetLoc("Lang_MsgSelectFirst"), GetLoc("Lang_MsgTip"), MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If

        ' 3. 正常进入转换流程，禁用按钮防止重复点击
        BtnConvert.IsEnabled = False
        BtnConvert.Content = GetLoc("Lang_Converting")

        ' 4. 遍历刚才筛选出来的歌曲进行处理
        For Each item In itemsToConvert
            Await ProcessNcmFile(item)
        Next

        ' 5. 全部完成后恢复按钮状态
        BtnConvert.Content = GetLoc("Lang_ConvertDone")
        BtnConvert.Background = New SolidColorBrush(Color.FromRgb(46, 204, 113)) ' 变成绿色
        BtnConvert.IsEnabled = True

        ' 【新增】：2. 挂起等待 2 秒钟，让用户看清楚绿色的成功提示
        Await Task.Delay(2000)

        ' 【新增】：3. 自动恢复成红色的初始状态，准备迎接下一批转换
        BtnConvert.Content = GetLoc("Lang_StartConvert")
        BtnConvert.Background = New SolidColorBrush(Color.FromRgb(255, 77, 77)) ' 恢复红色
        BtnConvert.IsEnabled = True

    End Sub

    Private Async Function ProcessNcmFile(item As MusicItem) As Task
        Try
            ' 更新状态为“解密中”
            item.StatusKey = "Lang_StatusDecrypt"
            item.Status = GetLoc(item.StatusKey)
            item.StatusColor = "#E74C3C" ' 红色表示正在忙碌

            Dim targetDir = If(String.IsNullOrEmpty(_customOutputDir), IO.Path.GetDirectoryName(item.FilePath), _customOutputDir)

            Using ncmStream As New NeteaseCloudMusicStream(item.FilePath)

                ' ================== 全新增强版：生成文件名逻辑 ==================
                Dim formatStr = TxtNameFormat.Text
                If String.IsNullOrWhiteSpace(formatStr) Then formatStr = "%s-%a"

                Dim now = DateTime.Now
                Dim origName = IO.Path.GetFileNameWithoutExtension(item.FilePath)

                ' 1. 替换新版详细变量
                Dim finalName = formatStr.Replace("%s", item.FileName) _
                                         .Replace("%a", item.Artist) _
                                         .Replace("%al", item.Album) _
                                         .Replace("%o", origName) _
                                         .Replace("%y", now.ToString("yyyy")) _
                                         .Replace("%m", now.ToString("MM")) _
                                         .Replace("%d", now.ToString("dd")) _
                                         .Replace("%t", now.ToString("HHmmss"))

                ' 3. 净化文件名，防止出现 "/"、"*" 等导致保存崩溃的非法字符
                finalName = GetSafeFileName(finalName)
                ' ================================================================

                ' 更新状态为“写入中”
                item.StatusKey = "Lang_StatusWrite"
                item.Status = GetLoc(item.StatusKey)
                Await ncmStream.DumpToFileAsync(targetDir, finalName)

                ' 更新状态为“打标签”
                item.StatusKey = "Lang_StatusTag"
                item.Status = GetLoc(item.StatusKey)
                ncmStream.FixMetadata(False)
            End Using

            ' 完成
            item.StatusKey = "Lang_StatusSuccess"
            item.Status = GetLoc(item.StatusKey)
            item.StatusColor = "#2ECC71" ' 成功变绿
            item.IsSelected = False

        Catch ex As Exception
            ' 失败
            item.StatusKey = "Lang_StatusFail"
            item.Status = GetLoc(item.StatusKey)
            item.StatusColor = "#E74C3C" ' 失败保持红色
        End Try
    End Function

    ' 将底层的字节流图片转为 WPF 的 BitmapImage
    Private Function BytesToBitmapImage(imageData As Byte()) As BitmapImage
        If imageData Is Nothing OrElse imageData.Length = 0 Then Return Nothing
        Dim bitmap As New BitmapImage()
        Using stream As New MemoryStream(imageData)
            bitmap.BeginInit()
            bitmap.CacheOption = BitmapCacheOption.OnLoad
            bitmap.StreamSource = stream
            bitmap.EndInit()
            bitmap.Freeze() ' 必须冻结，否则跨线程报错
        End Using
        Return bitmap
    End Function

    ' --- 右键菜单：移除单项 ---
    Private Sub MenuItem_Remove_Click(sender As Object, e As RoutedEventArgs)
        Dim menuItem = TryCast(sender, MenuItem)
        If menuItem IsNot Nothing Then
            Dim itemToRemove = TryCast(menuItem.DataContext, MusicItem)
            If itemToRemove IsNot Nothing Then
                MusicItems.Remove(itemToRemove)
                UpdateUIState() ' 【新增】：移除后检查一下是不是全删光了
            End If
        End If
    End Sub
    ' --- 响应拖拽区的双击事件 ---
    Private Sub DropArea_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        ' 判断是否是双击
        If e.ClickCount = 2 Then
            ' 只有在列表为空（提示面板可见）的时候，双击才等同于“添加文件”
            If DropPromptPanel.Visibility = Visibility.Visible Then
                BtnAddFiles_Click(Nothing, Nothing)
            End If
        End If
    End Sub

    ' --- 右键菜单：清除列表 ---
    Private Sub MenuItem_ClearList_Click(sender As Object, e As RoutedEventArgs)
        MusicItems.Clear() ' 清空底层数据集合
        UpdateUIState()    ' 恢复拖拽加号界面
    End Sub

    ' --- 辅助方法：根据列表数量自动切换 UI 状态 ---
    Private Sub UpdateUIState()
        If MusicItems.Count = 0 Then
            ' 列表空了，显示加号提示，隐藏列表
            DropPromptPanel.Visibility = Visibility.Visible
            MusicList.Visibility = Visibility.Collapsed
        Else
            ' 列表有数据，隐藏加号提示，显示列表
            DropPromptPanel.Visibility = Visibility.Collapsed
            MusicList.Visibility = Visibility.Visible
        End If
    End Sub

    ' --- 辅助：清理 Windows 文件名中的非法字符 ---
    Private Function GetSafeFileName(fileName As String) As String
        Dim invalidChars = IO.Path.GetInvalidFileNameChars()
        For Each c In invalidChars
            fileName = fileName.Replace(c, "_"c) ' 将所有非法字符替换为下划线
        Next
        Return fileName
    End Function

    ' --- 弹窗控制：设置与关于 ---
    Private Sub BtnSettings_Click(sender As Object, e As RoutedEventArgs)
        SettingsOverlay.Visibility = Visibility.Visible
    End Sub



    Private Sub BtnAbout_Click(sender As Object, e As RoutedEventArgs)
        AboutOverlay.Visibility = Visibility.Visible
    End Sub

    Private Sub BtnCloseAbout_Click(sender As Object, e As RoutedEventArgs)
        AboutOverlay.Visibility = Visibility.Collapsed
    End Sub


    ' --- 试听播放控制逻辑 ---
    Private Async Sub BtnPlay_Click(sender As Object, e As RoutedEventArgs)
        Dim btn = TryCast(sender, Button)
        If btn Is Nothing Then Return
        Dim item = TryCast(btn.DataContext, MusicItem)
        If item Is Nothing Then Return

        ' 1. 如果点击的是正在播放的歌曲，则立刻停止
        If item Is _currentlyPlayingItem AndAlso item.IsPlaying Then
            _player.Stop()
            item.IsPlaying = False
            _currentlyPlayingItem = Nothing
            Return
        End If

        ' 2. 如果有其他歌曲在播放，先切断它
        If _currentlyPlayingItem IsNot Nothing Then
            _currentlyPlayingItem.IsPlaying = False
            _player.Stop()
        End If

        ' 3. 设置这首歌为正在播放状态 (UI会立刻变成停止方块)
        _currentlyPlayingItem = item
        item.IsPlaying = True

        Try
            Dim safeName = GetSafeFileName(item.FileName)
            Dim generatedFile As String = ""

            ' 检查系统临时文件夹里是否已经解密过这首歌（避免重复等待）
            Dim existingFiles = Directory.GetFiles(_tempAudioFolder, safeName & ".*")
            If existingFiles.Length > 0 Then
                generatedFile = existingFiles(0)
            Else
                ' 没有的话，静默解密这首歌丢进临时文件夹
                Using ncmStream As New NeteaseCloudMusicStream(item.FilePath)
                    Await ncmStream.DumpToFileAsync(_tempAudioFolder, safeName)
                    generatedFile = IO.Path.Combine(_tempAudioFolder, safeName & "." & ncmStream.Format.ToString().ToLower())
                End Using
            End If

            ' 确保在解密这 1 秒钟期间，用户没有点击停止按钮
            If item.IsPlaying Then
                _player.Open(New Uri(generatedFile))
                _player.Play()
            End If
        Catch ex As Exception
            MessageBox.Show("试听加载失败：" & ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Error)
            item.IsPlaying = False
            _currentlyPlayingItem = Nothing
        End Try
    End Sub

    ' 音乐自然播放结束时，自动把方块变回三角
    Private Sub _player_MediaEnded(sender As Object, e As EventArgs) Handles _player.MediaEnded
        If _currentlyPlayingItem IsNot Nothing Then
            _currentlyPlayingItem.IsPlaying = False
            _currentlyPlayingItem = Nothing
        End If
    End Sub

    ' 窗口关闭时，自动把产生的试听垃圾文件全部删掉，保持系统干净！
    Protected Overrides Sub OnClosed(e As EventArgs)
        MyBase.OnClosed(e)
        Try
            _player.Close()
            If Directory.Exists(_tempAudioFolder) Then Directory.Delete(_tempAudioFolder, True)
        Catch
        End Try
    End Sub


    ' --- 应用主题与语言字典 ---

    Private Sub ApplyThemeAndLanguage(isDark As Boolean, langIndex As Integer)
        Try
            Me.Resources.MergedDictionaries.Clear()

            ' 1. 加载主题和语言字典
            Dim themeUri = If(isDark, "DarkTheme.xaml", "LightTheme.xaml")
            Me.Resources.MergedDictionaries.Add(New ResourceDictionary() With {.Source = New Uri(themeUri, UriKind.Relative)})

            Dim langUri = If(langIndex = 1, "Lang.en-US.xaml", "Lang.zh-CN.xaml")
            Me.Resources.MergedDictionaries.Add(New ResourceDictionary() With {.Source = New Uri(langUri, UriKind.Relative)})

            ' 【核心修复】：遍历列表，实时刷新已存在歌曲的文字
            For Each item In MusicItems
                ' 刷新状态文字
                If Not String.IsNullOrEmpty(item.StatusKey) Then
                    item.Status = GetLoc(item.StatusKey)
                End If

                ' 刷新“解析中”或“失败”的占位符文字
                If item.Album = "解析中..." Or item.Album = "Parsing..." Then
                    item.Album = GetLoc("Lang_Parsing")
                ElseIf item.Album = "信息解析失败" Or item.Album = "Parse Failed" Then
                    item.Album = GetLoc("Lang_ParseFail")
                End If

                ' 如果歌手是未知的，也刷新一下
                If item.Artist = "未知歌手" Or item.Artist = "Unknown Artist" Then
                    item.Artist = GetLoc("Lang_UnknownArtist")
                End If
            Next

            ' 【新增】：刷新版本号文字
            TxtCurrentVersion.Text = $"{GetLoc("Lang_VersionLabel")}v{AppVersion}"

            ' 【新增】：如果已经查到新版本，刷新 ToolTip
            If PanelUpdate.Visibility = Visibility.Visible Then
                BtnAbout.ToolTip = GetLoc("Lang_UpdateAvailable")
            End If

        Catch
        End Try
    End Sub

    ' --- 关闭设置面板时，执行保存并刷新 ---
    Private Sub BtnCloseSettings_Click(sender As Object, e As RoutedEventArgs)
        SettingsOverlay.Visibility = Visibility.Collapsed

        ' 把界面上的值写回配置对象
        _currentConfig.NamingFormat = TxtNameFormat.Text
        _currentConfig.ThemeIndex = CmbTheme.SelectedIndex
        _currentConfig.LanguageIndex = CmbLanguage.SelectedIndex

        ' 调用管理器写入 JSON 文件
        ConfigManager.Save(_currentConfig)

        ' 立即应用选中的主题
        ApplyThemeAndLanguage(_currentConfig.ThemeIndex = 1, _currentConfig.LanguageIndex)
    End Sub

    ' --- 辅助方法：动态获取当前语言字典里的字符串 ---
    Private Function GetLoc(key As String) As String
        Dim val = Me.TryFindResource(key)
        Return If(val IsNot Nothing, val.ToString(), key)
    End Function

    ' ==================== 版本更新与外部链接逻辑 ====================
    Private Async Sub CheckForUpdatesAsync()
        Try
            Using client As New HttpClient()
                ' GitHub API 规定必须设置合法的 User-Agent，否则会被拦截
                client.DefaultRequestHeaders.Add("User-Agent", "NcmMaster-UpdateChecker")

                ' 获取你的仓库的最新 Release JSON 数据
                Dim response = Await client.GetStringAsync("https://api.github.com/repos/louis253/NcmMaster/releases/latest")

                ' 解析 JSON，提取 tag_name (例如 GitHub 上打的标签是 "v1.0.1")
                Using doc = JsonDocument.Parse(response)
                    Dim latestTag = doc.RootElement.GetProperty("tag_name").GetString()

                    If Not String.IsNullOrEmpty(latestTag) Then
                        ' 剥离 "v" 字母，将 "1.0.1" 转换为严谨的版本对象用于对比
                        Dim latestVersionStr = latestTag.TrimStart("v"c, "V"c)
                        Dim vLatest As Version = Version.Parse(latestVersionStr)
                        Dim vCurrent As Version = Version.Parse(AppVersion)

                        ' 如果 GitHub 上的版本大于当前代码版本
                        If vLatest > vCurrent Then
                            ' 切回主线程更新 UI：将关于按钮变成醒目的红色，并显示下载面板
                            Dispatcher.Invoke(Sub()
                                                  BtnAbout.Foreground = New SolidColorBrush(Color.FromRgb(231, 76, 60))
                                                  BtnAbout.ToolTip = GetLoc("Lang_UpdateAvailable")

                                                  PanelUpdate.Visibility = Visibility.Visible
                                                  TxtLatestVersion.Text = latestTag
                                              End Sub)
                        End If
                    End If
                End Using
            End Using
        Catch ex As Exception
            ' 检查更新失败（比如没网、被墙、未发布Release等）就静默处理，不打扰用户正常使用
        End Try
    End Sub

    ' 点击 GitHub 链接
    Private Sub BtnGitHub_Click(sender As Object, e As RoutedEventArgs)
        OpenUrl("https://github.com/louis253/NcmMaster")
    End Sub

    ' 点击前往下载
    Private Sub BtnDownloadUpdate_Click(sender As Object, e As RoutedEventArgs)
        OpenUrl(_latestReleaseUrl)
    End Sub

    ' 唤起系统默认浏览器安全地打开网页
    Private Sub OpenUrl(url As String)
        Try
            Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
        Catch ex As Exception
            MessageBox.Show("无法自动调用浏览器，请手动复制访问：" & vbCrLf & url, "提示", MessageBoxButton.OK, MessageBoxImage.Information)
        End Try
    End Sub

    ' ==================== 打开输出目录逻辑 ====================
    Private Sub TxtOutputDir_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs)
        Try
            Dim targetDir As String = _customOutputDir

            ' 如果当前是“与原文件相同”模式，并且列表里有导入的歌曲
            If String.IsNullOrEmpty(targetDir) AndAlso MusicItems.Count > 0 Then
                ' 智能获取第一首歌所在的真实文件夹路径
                targetDir = IO.Path.GetDirectoryName(MusicItems(0).FilePath)
            End If

            ' 如果路径不为空且真实存在，则呼出 Windows 资源管理器打开该目录
            If Not String.IsNullOrEmpty(targetDir) AndAlso IO.Directory.Exists(targetDir) Then
                Process.Start("explorer.exe", targetDir)
            End If
        Catch ex As Exception
            ' 防止因为系统权限等意外报错闪退
        End Try
    End Sub


End Class