using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MusicNetEasePlugin.Features.Music;

public enum MusicBrowsePage { Search, Library, History }

/// <summary>
/// 一个 Document 的导航记忆，仅保存展示状态。队列是否并排由实际可用空间决定，
/// 不把布局断点写入用户偏好，也不在页面切换时重新创建模型或请求数据。
/// 这使宽屏收窄、歌词返回及 Dock 重挂仍能回到原来的浏览位置。
/// </summary>
public sealed class MusicNavigation : ObservableObject
{
    private MusicBrowsePage _page;
    private bool _queueOpen;
    private bool _lyricsOpen;
    private bool _wide;
    public MusicNavigation()
    {
        ShowSearchCommand = new RelayCommand(() => Browse(MusicBrowsePage.Search));
        ShowHistoryCommand = new RelayCommand(() => Browse(MusicBrowsePage.History));
        ShowQueueCommand = new RelayCommand(() => { _queueOpen = !_queueOpen; Refresh(); });
        ShowLyricsCommand = new RelayCommand(() => { _lyricsOpen = !_lyricsOpen; if (!_wide) _queueOpen = false; Refresh(); });
        BackCommand = new RelayCommand(Back);
    }
    public bool SearchSelected => _page == MusicBrowsePage.Search;
    public bool LibrarySelected => _page == MusicBrowsePage.Library;
    public bool HistorySelected => _page == MusicBrowsePage.History;
    public bool IsSearch => SearchSelected && !_lyricsOpen && (!_queueOpen || _wide);
    public bool IsLibrary => LibrarySelected && !_lyricsOpen && (!_queueOpen || _wide);
    public bool IsHistory => HistorySelected && !_lyricsOpen && (!_queueOpen || _wide);
    public bool IsLyrics => _lyricsOpen && (!_queueOpen || _wide);
    public bool IsQueue => _queueOpen;
    public bool IsQueueBeside => _queueOpen && _wide;
    public bool IsQueuePage => _queueOpen && !_wide;
    public bool CanBack => _lyricsOpen || _queueOpen;
    public IRelayCommand ShowSearchCommand { get; }
    public IRelayCommand ShowHistoryCommand { get; }
    public IRelayCommand ShowQueueCommand { get; }
    public IRelayCommand ShowLyricsCommand { get; }
    public IRelayCommand BackCommand { get; }
    public void Browse(MusicBrowsePage page)
    {
        _page = page; _lyricsOpen = false;
        if (!_wide) _queueOpen = false;
        Refresh();
    }
    public void SetAvailableSize(double width, double height)
    {
        var wide = width >= 1120 && height >= 550;
        if (_wide == wide) return;
        _wide = wide; Refresh();
    }
    public void Back()
    {
        if (_queueOpen && !_wide) _queueOpen = false;
        else if (_lyricsOpen) _lyricsOpen = false;
        else _queueOpen = false;
        Refresh();
    }
    private void Refresh()
    {
        foreach (var name in new[] { nameof(SearchSelected), nameof(LibrarySelected), nameof(HistorySelected), nameof(IsSearch), nameof(IsLibrary), nameof(IsHistory), nameof(IsLyrics), nameof(IsQueue), nameof(IsQueueBeside), nameof(IsQueuePage), nameof(CanBack) }) OnPropertyChanged(name);
    }
}
