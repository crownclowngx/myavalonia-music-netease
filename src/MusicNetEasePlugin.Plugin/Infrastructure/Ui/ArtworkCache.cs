using System.Buffers.Binary;
using System.Security.Cryptography;
using Avalonia.Media.Imaging;

namespace MusicNetEasePlugin.Infrastructure.Ui;

/// <summary>
/// 位图只在表现层解码。引用计数使两个 Document 可以安全共用一张图；淘汰绝不释放仍在绘制的资源。
/// 缓存已满且全部被使用时返回占位，宁可少展示装饰，也不突破容器的 16 MiB / 24 张硬上限。
/// </summary>
public sealed class ArtworkCache : IDisposable
{
    public const int ByteLimit = 16 * 1024 * 1024;
    public const int EntryLimit = 24;
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly object _sync = new();
    private long _clock;
    private bool _disposed;
    public event EventHandler? Cleared;
    private sealed class Entry(Bitmap bitmap, long used)
    {
        public Bitmap Bitmap { get; } = bitmap;
        public int Bytes => checked(Bitmap.PixelSize.Width * Bitmap.PixelSize.Height * 4);
        public int References;
        public long Used = used;
        public bool Retired;
    }
    public int Count { get { lock (_sync) return _entries.Count; } }
    public int Bytes { get { lock (_sync) return _entries.Values.Sum(x => x.Bytes); } }
    public sealed class Lease(Bitmap bitmap, Action release) : IDisposable
    {
        private Action? _release = release;
        public Bitmap Image { get; } = bitmap;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
    public Lease? Acquire(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 and <= 2 * 1024 * 1024 }) return null;
        var key = Convert.ToHexString(SHA256.HashData(bytes));
        lock (_sync)
        {
            if (_disposed) return null;
            if (!_entries.TryGetValue(key, out var entry))
            {
                var (width, height) = ReadSize(bytes);
                // 先读头再请求缩略解码，拒绝未知格式、动画与异常尺寸，防止小压缩包诱发大分配。
                if (width <= 0 || height <= 0 || (long)width * height > 4 * 1024 * 1024) return null;
                const int edge = 256;
                while (_entries.Count >= EntryLimit || Bytes + edge * edge * 4 > ByteLimit)
                {
                    var victim = _entries.Where(x => x.Value.References == 0).OrderBy(x => x.Value.Used).FirstOrDefault();
                    if (victim.Value is null) return null;
                    _entries.Remove(victim.Key); victim.Value.Bitmap.Dispose();
                }
                try
                {
                    using var stream = new MemoryStream(bytes, false);
                    var image = width >= height ? Bitmap.DecodeToWidth(stream, Math.Min(edge, width)) : Bitmap.DecodeToHeight(stream, Math.Min(edge, height));
                    if (image.PixelSize.Width > edge || image.PixelSize.Height > edge) { image.Dispose(); return null; }
                    entry = new(image, ++_clock); _entries.Add(key, entry);
                }
                catch (Exception) { return null; }
            }
            if (entry.Retired) return null;
            entry.References++; entry.Used = ++_clock;
            return new(entry.Bitmap, () => Release(key, entry));
        }
    }
    private void Release(string key, Entry entry)
    {
        lock (_sync)
        {
            if (--entry.References == 0 && entry.Retired) { _entries.Remove(key); entry.Bitmap.Dispose(); }
        }
    }
    public void Clear()
    {
        lock (_sync)
        {
            foreach (var (key, entry) in _entries.ToArray())
            {
                entry.Retired = true;
                if (entry.References == 0) { _entries.Remove(key); entry.Bitmap.Dispose(); }
            }
        }
        // 订阅者在 UI 线程先断开 Image.Source，再释放租约，杜绝一次绘制持有已 Dispose 的 Bitmap。
        Cleared?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() { lock (_sync) _disposed = true; Clear(); }

    internal static (int Width, int Height) ReadSize(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return (BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..]));
        if (data.Length < 4 || data[0] != 255 || data[1] != 216) return default;
        for (var i = 2; i + 4 <= data.Length;)
        {
            if (data[i++] != 255) return default;
            while (i < data.Length && data[i] == 255) i++;
            if (i >= data.Length) return default;
            var marker = data[i++];
            if (marker is 0xd9 or 0xda) return default;
            if (marker is 0x01 or >= 0xd0 and <= 0xd8) continue;
            if (i + 2 > data.Length) return default;
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[i..]);
            if (length < 2 || i + length > data.Length) return default;
            if (marker is >= 0xc0 and <= 0xc3 && length >= 7)
                return (BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(i + 3)..]));
            i += length;
        }
        return default;
    }
}
