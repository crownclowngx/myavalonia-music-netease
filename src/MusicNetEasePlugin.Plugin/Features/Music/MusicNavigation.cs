using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MusicNetEasePlugin.Features.Music;

public enum MusicBrowsePage { Search, Library, History }
public enum MusicSidePanel { None, Lyrics, Queue }

/// <summary>
/// 每个 Document 的浏览页与抽屉各有一个枚举，二者正交，切换侧栏不隐藏或重建浏览模型。
/// 实际宽度由内容区域决定，不回写用户期望宽度；尺寸变化也不改变播放和导航意图。
/// </summary>
public sealed class MusicNavigation : ObservableObject
{
    private MusicBrowsePage _page;
    private MusicSidePanel _panel;
    private double _width;
    private double _desiredWidth = 360;
    public MusicNavigation()
    {
        ShowSearchCommand = new RelayCommand(() => Browse(MusicBrowsePage.Search));
        ShowHistoryCommand = new RelayCommand(() => Browse(MusicBrowsePage.History));
        ShowQueueCommand = new RelayCommand(() => Toggle(MusicSidePanel.Queue));
        ShowLyricsCommand = new RelayCommand(() => Toggle(MusicSidePanel.Lyrics));
        BackCommand = new RelayCommand(Back);
    }
    public bool SearchSelected => _page == MusicBrowsePage.Search;
    public bool LibrarySelected => _page == MusicBrowsePage.Library;
    public bool HistorySelected => _page == MusicBrowsePage.History;
    public MusicSidePanel SidePanel => _panel;
    public bool IsSearch => SearchSelected;
    public bool IsLibrary => LibrarySelected;
    public bool IsHistory => HistorySelected;
    public bool IsLyrics => _panel == MusicSidePanel.Lyrics;
    public bool IsQueue => _panel == MusicSidePanel.Queue;
    public bool IsOpen => _panel != MusicSidePanel.None;
    public bool IsBeside => IsOpen && _width >= _desiredWidth + 620;
    public bool IsQueueBeside => IsQueue && IsBeside;
    public bool IsQueuePage => false;
    public bool CanBack => IsOpen;
    public double ActualDrawerWidth => Math.Min(_desiredWidth, Math.Max(0, _width - 16));
    public string PanelTitle => IsQueue ? "播放队列" : "歌词";
    public string LyricsAction => IsLyrics ? "收起歌词" : "展开歌词";
    public string QueueAction => IsQueue ? "收起播放队列" : "展开播放队列";
    public IRelayCommand ShowSearchCommand { get; }
    public IRelayCommand ShowHistoryCommand { get; }
    public IRelayCommand ShowQueueCommand { get; }
    public IRelayCommand ShowLyricsCommand { get; }
    public IRelayCommand BackCommand { get; }
    public void Browse(MusicBrowsePage page)
    {
        _page = page;
        Refresh();
    }
    public void SetAvailableSize(double width, double height)
    {
        var available = double.IsFinite(width) ? Math.Max(0, width) : 0;
        if (_width == available) return;
        _width = available; Refresh();
    }
    public void SetDesiredWidth(double width)
    {
        if (!double.IsFinite(width)) return;
        var desired = Math.Clamp(width, 320, 480);
        if (_desiredWidth == desired) return;
        _desiredWidth = desired; Refresh();
    }
    private void Toggle(MusicSidePanel panel) { _panel = _panel == panel ? MusicSidePanel.None : panel; Refresh(); }
    public void Back() { _panel = MusicSidePanel.None; Refresh(); }
    private void Refresh()
    {
        foreach (var name in new[] { nameof(SearchSelected), nameof(LibrarySelected), nameof(HistorySelected), nameof(IsSearch), nameof(IsLibrary), nameof(IsHistory),
            nameof(IsLyrics), nameof(IsQueue), nameof(IsQueueBeside), nameof(IsQueuePage), nameof(CanBack), nameof(IsOpen), nameof(IsBeside), nameof(ActualDrawerWidth),
            nameof(PanelTitle), nameof(LyricsAction), nameof(QueueAction), nameof(SidePanel) }) OnPropertyChanged(name);
    }
}
