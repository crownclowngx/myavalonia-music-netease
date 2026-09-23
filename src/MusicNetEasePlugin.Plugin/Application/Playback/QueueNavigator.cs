namespace MusicNetEasePlugin.Application.Playback;

/// <summary>
/// 无 IO 的队列导航。仅由共享协调器在短状态锁内调用，模式用普通分支表达。
/// 随机使用无放回候选及真实访问历史，优先段独立 FIFO；可注入随机源重放边界场景。
/// </summary>
internal sealed class QueueNavigator(Func<int, int>? choose = null)
{
    private readonly Func<int, int> _choose = choose ?? Random.Shared.Next;
    private readonly List<QueueEntry> _entries = [];
    private readonly List<Guid> _priority = [];
    private readonly List<Guid> _bag = [];
    private readonly List<Guid> _history = [];
    private int _historyIndex = -1;
    public IReadOnlyList<QueueEntry> Entries => _entries;
    public Guid? CurrentId { get; private set; }
    public QueueEntry? Current => _entries.FirstOrDefault(e => e.EntryId == CurrentId);
    public PlaybackMode Mode { get; private set; }
    public bool CanNext => _entries.Count > 1 && (Mode != PlaybackMode.Sequential || _priority.Count > 0 || Index < _entries.Count - 1);
    public bool CanPrevious => _entries.Count > 1 && (Mode == PlaybackMode.Shuffle ? _historyIndex > 0 : Mode != PlaybackMode.Sequential || Index > 0);
    private int Index => _entries.FindIndex(e => e.EntryId == CurrentId);
    public void Replace(IReadOnlyList<QueueEntry> entries, int start)
    {
        _entries.Clear(); _entries.AddRange(entries); _priority.Clear(); _bag.Clear(); _history.Clear(); _historyIndex = -1;
        CurrentId = null; if (_entries.Count > 0) Select(_entries[start].EntryId);
        ResetCandidates();
    }
    public void Add(IReadOnlyList<QueueEntry> entries, bool next)
    {
        if (next && CurrentId is not null)
        {
            var last = _priority.LastOrDefault();
            var index = last == Guid.Empty ? Index + 1 : _entries.FindIndex(e => e.EntryId == last) + 1;
            _entries.InsertRange(Math.Max(0, index), entries); _priority.AddRange(entries.Select(e => e.EntryId));
        }
        else _entries.AddRange(entries);
        if (CurrentId is null && _entries.Count > 0) Select(_entries[0].EntryId);
        if (Mode == PlaybackMode.Shuffle)
        {
            var candidates = _bag.ToHashSet();
            _bag.AddRange(entries.Select(e => e.EntryId).Where(id => id != CurrentId && candidates.Add(id)));
        }
    }
    public void SetMode(PlaybackMode mode)
    {
        Mode = mode; _bag.Clear(); _history.Clear(); _historyIndex = -1;
        if (CurrentId is { } id) Record(id);
        ResetCandidates();
    }
    public bool Select(Guid id)
    {
        if (!_entries.Any(e => e.EntryId == id)) return false;
        CurrentId = id; _bag.Remove(id); Record(id); return true;
    }
    private void ResetCandidates()
    {
        // 在进入随机模式时建立本轮候选，不能等优先段播放完才初始化，否则已消费优先项会重新入袋。
        _bag.Clear();
        if (Mode == PlaybackMode.Shuffle) _bag.AddRange(_entries.Select(entry => entry.EntryId).Where(id => id != CurrentId));
    }
    public Guid? Next(bool natural, HashSet<Guid>? excluded = null)
    {
        bool Allowed(Guid id) => excluded?.Contains(id) != true;
        while (_priority.Count > 0)
        {
            var id = _priority[0]; _priority.RemoveAt(0);
            if (_entries.Any(e => e.EntryId == id) && Allowed(id)) { Select(id); return id; }
        }
        if (_entries.Count == 0) return null;
        if (Mode == PlaybackMode.RepeatOne && natural && CurrentId is { } current && Allowed(current)) return current;
        if (Mode == PlaybackMode.Shuffle)
        {
            if (!natural && _historyIndex + 1 < _history.Count && Allowed(_history[_historyIndex + 1]))
            { CurrentId = _history[++_historyIndex]; return CurrentId; }
            var existing = _entries.Select(e => e.EntryId).ToHashSet();
            _bag.RemoveAll(id => !existing.Contains(id) || !Allowed(id) || id == CurrentId);
            if (_bag.Count == 0) _bag.AddRange(_entries.Select(e => e.EntryId).Where(id => Allowed(id) && (id != CurrentId || _entries.Count == 1)));
            if (_bag.Count == 0) return null;
            var chosen = _bag[_choose(_bag.Count)]; Select(chosen); return chosen;
        }
        var index = Index;
        for (var step = 1; step <= _entries.Count; step++)
        {
            var candidate = index + step;
            if (candidate >= _entries.Count && Mode == PlaybackMode.Sequential) return null;
            var id = _entries[candidate % _entries.Count].EntryId;
            if (Allowed(id)) { Select(id); return id; }
        }
        return null;
    }
    public Guid? Previous()
    {
        if (!CanPrevious) return null;
        if (Mode == PlaybackMode.Shuffle) { CurrentId = _history[--_historyIndex]; return CurrentId; }
        var id = _entries[(Index - 1 + _entries.Count) % _entries.Count].EntryId; Select(id); return id;
    }
    public void Remove(Guid id)
    {
        var previousIndex = Index;
        _entries.RemoveAll(e => e.EntryId == id); _priority.RemoveAll(i => i == id); _bag.RemoveAll(i => i == id);
        var before = _history.Take(Math.Max(0, _historyIndex + 1)).Count(i => i != id);
        _history.RemoveAll(i => i == id); _historyIndex = before - 1;
        if (CurrentId == id)
        {
            CurrentId = null;
            if (_entries.Count > 0) Select(_entries[Math.Clamp(previousIndex, 0, _entries.Count - 1)].EntryId);
        }
    }
    public void Move(Guid id, int direction)
    {
        var index = _entries.FindIndex(e => e.EntryId == id);
        if (index < 0) return;
        var target = Math.Clamp(index + Math.Sign(direction), 0, _entries.Count - 1);
        var item = _entries[index]; _entries.RemoveAt(index); _entries.Insert(target, item);
    }
    public void UpdateTrack(MusicTrack track)
    {
        var index = Index;
        if (index >= 0 && _entries[index].TrackId == track.Id) _entries[index] = _entries[index] with { Track = track };
    }
    private void Record(Guid id)
    {
        if (_historyIndex + 1 < _history.Count) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        if (_history.Count == 0 || _history[^1] != id) _history.Add(id);
        if (_history.Count > 10000) _history.RemoveAt(0);
        _historyIndex = _history.Count - 1;
    }
}
