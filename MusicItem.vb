Imports System.ComponentModel
Imports System.Runtime.CompilerServices
Imports System.Windows.Media.Imaging

Public Class MusicItem
    Implements INotifyPropertyChanged

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
    Private Sub NotifyPropertyChanged(<CallerMemberName> Optional propertyName As String = "")
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
    End Sub

    ' 1. 基础信息
    Private _fileName As String
    Public Property FileName As String
        Get
            Return _fileName
        End Get
        Set(value As String)
            _fileName = value
            NotifyPropertyChanged()
        End Set
    End Property

    Private _artist As String
    Public Property Artist As String
        Get
            Return _artist
        End Get
        Set(value As String)
            _artist = value
            NotifyPropertyChanged()
        End Set
    End Property

    Private _album As String
    Public Property Album As String
        Get
            Return _album
        End Get
        Set(value As String)
            _album = value
            NotifyPropertyChanged()
        End Set
    End Property

    ' 2. 状态控制 (Key 用于多语言，Color 用于颜色)
    Private _status As String
    Public Property Status As String
        Get
            Return _status
        End Get
        Set(value As String)
            _status = value
            NotifyPropertyChanged()
        End Set
    End Property

    Private _statusKey As String = ""
    Public Property StatusKey As String
        Get
            Return _statusKey
        End Get
        Set(value As String)
            _statusKey = value
            NotifyPropertyChanged()
        End Set
    End Property

    Private _statusColor As String = "#999999"
    Public Property StatusColor As String
        Get
            Return _statusColor
        End Get
        Set(value As String)
            _statusColor = value
            NotifyPropertyChanged()
        End Set
    End Property

    ' 3. 媒体与交互
    Private _coverImage As BitmapImage
    Public Property CoverImage As BitmapImage
        Get
            Return _coverImage
        End Get
        Set(value As BitmapImage)
            _coverImage = value
            NotifyPropertyChanged()
        End Set
    End Property

    Private _filePath As String
    Public Property FilePath As String
        Get
            Return _filePath
        End Get
        Set(value As String)
            _filePath = value
            NotifyPropertyChanged()
        End Set
    End Property

    Private _isSelected As Boolean = True
    Public Property IsSelected As Boolean
        Get
            Return _isSelected
        End Get
        Set(value As Boolean)
            _isSelected = value
            NotifyPropertyChanged()
        End Set
    End Property

    Private _isPlaying As Boolean = False
    Public Property IsPlaying As Boolean
        Get
            Return _isPlaying
        End Get
        Set(value As Boolean)
            _isPlaying = value
            NotifyPropertyChanged()
        End Set
    End Property
End Class