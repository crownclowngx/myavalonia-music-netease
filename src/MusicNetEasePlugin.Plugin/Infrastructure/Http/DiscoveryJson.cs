using System.Text.Json;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Playback;
namespace MusicNetEasePlugin.Infrastructure.Http;
/// <summary>只共享字段验证及分页预算；端点选择容器，不吞掉必需字段错误。</summary>
internal static class DiscoveryJson
{
    internal static JsonElement Required(JsonElement item, string name, JsonValueKind kind)
    { if (!item.TryGetProperty(name, out var value) || value.ValueKind != kind) throw new JsonException(); return value; }
    internal static long? Number(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && n >= 0 ? n : null;
    internal static string Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    internal static bool More(JsonElement item) => item.TryGetProperty("more", out var more) && more.ValueKind == JsonValueKind.True;
    internal static MusicCard Card(JsonElement item)
    {
        var id = Number(item, "id"); if (id is not > 0) throw new JsonException();
        var cover = Text(item, "picUrl"); if (cover.Length == 0) cover = Text(item, "coverImgUrl");
        var name = Text(item, "name");
        return new(id.Value, name.Length == 0 ? $"未命名作品 {id}" : name, cover, Text(item, "updateFrequency"));
    }
    internal static CatalogPage<T> Page<T>(JsonElement array, Func<JsonElement, T> parse, int offset = 0, bool more = false, int limit = DiscoveryLimits.Items)
    {
        if (array.ValueKind != JsonValueKind.Array) throw new JsonException();
        var raw = array.GetArrayLength(); var room = Math.Min(limit, DiscoveryLimits.Items - offset);
        var items = array.EnumerateArray().Take(room).Select(parse).ToArray(); var next = offset + raw;
        return new(items, next, more && raw > 0 && next < DiscoveryLimits.Items, !more && raw <= room);
    }
    internal static MusicTrack Song(JsonElement item) => NeteaseMusicApi.ParseTrack(item);
}
