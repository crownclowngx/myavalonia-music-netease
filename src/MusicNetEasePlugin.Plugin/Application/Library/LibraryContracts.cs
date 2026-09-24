using System.Collections.Immutable;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Application.Library;

/// <summary>显式区分普通、内置喜欢与其他类型。Unknown 不能获得编辑权限，名称和列表位置不参与权限判断。</summary>
public enum LibraryPlaylistKind { Unknown, Normal, Liked, Shared, Other }
public sealed record LibraryPlaylist(long Id, string Name, string? Description, bool DescriptionKnown,
    long CreatorId, LibraryPlaylistKind Kind, bool? Subscribed, IReadOnlyList<long> TrackIds,
    bool IsComplete, int? TrackCount, long? UpdateTime = null)
{
    public bool CanEdit(long accountId) => accountId > 0 && CreatorId == accountId && Kind == LibraryPlaylistKind.Normal;
    public bool CanSubscribe(long accountId) => accountId > 0 && CreatorId > 0 && CreatorId != accountId && Subscribed.HasValue;
}

public enum LibraryOperationKind { Like, Subscribe, Create, Rename, Description, AddTracks, RemoveTracks, Delete }
public enum LibraryOutcome { NotSent, Rejected, Confirmed, PartiallyConfirmed, NeedsRecheck, StaleSession, Busy, Conflict }
public enum LibraryReceiptState { NotSent, Accepted, Rejected, Uncertain }
public enum LibraryItemState { Added, AlreadyPresent, Removed, AlreadyAbsent, Rejected, NeedsRecheck }
public sealed record LibraryItemResult(long TrackId, LibraryItemState State);

/// <summary>回执仅描述请求是否得到明确答复，不等同于写后读取已确认。不会包含响应正文、令牌或底层异常。</summary>
public sealed record LibraryWriteReceipt(LibraryReceiptState State, string Message = "", long? CreatedId = null,
    string? ServerName = null, bool CredentialSaveFailed = false, bool StopRecheck = false, TimeSpan? RetryAfter = null);

/// <summary>本地操作关联不是服务端幂等键；接受后属于共享协调器，Document 关闭不代表远端撤销。</summary>
public sealed record LibraryIntent(LibraryOperationKind Kind, long TargetId = 0, bool Desired = false,
    string? Text = null, IReadOnlyList<long>? TrackIds = null, LibraryPlaylist? Baseline = null,
    long ExpectedEpoch = 0, long ExpectedAccountId = 0);
public sealed record LibraryMutationResult(Guid OperationId, LibraryOutcome Outcome, string Message,
    LibraryWriteReceipt? Receipt = null, LibraryPlaylist? Playlist = null,
    IReadOnlyList<LibraryItemResult>? Items = null, bool NoChange = false);

/// <summary>唯一共享事实快照。喜欢集合为 null 表示尚未获得可靠全量读取，空集合才表示已确认无喜欢歌曲。</summary>
public sealed record MusicLibrarySnapshot(long AccountId, long Epoch, long Revision,
    ImmutableHashSet<long>? Likes = null, bool IsBusy = false, string Message = "喜欢状态待加载。",
    LibraryMutationResult? LastResult = null,
    ImmutableDictionary<string, LibraryMutationResult>? Pending = null)
{
    public bool? IsLiked(long id) => Likes is null ? null : Likes.Contains(id);
}
/// <summary>事件只携带失效身份，不指挥页面导航；页面在 UI 回调中再检查账号代次和自己的导航版本。</summary>
public sealed record LibraryChanged(MusicLibrarySnapshot Snapshot, long? PlaylistId = null, bool DirectoryChanged = false,
    bool Deleted = false, bool LikesChanged = false);

public interface ILikedSongsApi
{
    Task<ImmutableHashSet<long>> ReadAsync(MusicSession session, CancellationToken ct);
    Task<LibraryWriteReceipt> SetAsync(long trackId, bool desired, MusicSession session, CancellationToken ct);
}
/// <summary>管理操作所需的权威详情。与普通浏览分开，避免每次翻页都增加动态状态请求；只读端口不含写方法。</summary>
public interface ILibraryPlaylistQuery
{
    Task<LibraryPlaylist> ReadAsync(long id, MusicSession session, CancellationToken ct);
}
public interface IPlaylistMutationApi
{
    Task<LibraryWriteReceipt> WriteAsync(LibraryIntent intent, MusicSession session, CancellationToken ct);
}

public enum LibraryReadError { Network, Timeout, RateLimited, Restricted, SignedOut, MissingOrInaccessible, Protocol }
/// <summary>不可访问与已删除不是同一事实；仅带安全分类和可选等待时间，禁止把服务端正文传播到 UI。</summary>
public sealed class LibraryReadException(LibraryReadError kind, string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public LibraryReadError Kind { get; } = kind;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public bool StopsRecheck => Kind is LibraryReadError.RateLimited or LibraryReadError.Restricted or LibraryReadError.SignedOut;
}
