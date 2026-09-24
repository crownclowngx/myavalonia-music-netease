using MusicNetEasePlugin.Application.Library;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class LibraryRulesTests
{
    [Theory, InlineData("1", true), InlineData(" 123 ", true), InlineData("0", false), InlineData("-1", false), InlineData("+1", false), InlineData("9223372036854775808", false), InlineData("7?a=1", false)]
    public void 标识只接受正整数且拒绝溢出与任意网址(string value, bool valid) => Assert.Equal(valid, LibraryEditRules.TryId(value, out _));
    [Theory, InlineData("", false), InlineData("  ", false), InlineData("中文😃", true), InlineData("a\nb", false), InlineData("a\0b", false)]
    public void 名称按中文规则校验并去除首尾空白(string value, bool valid)
    {
        if (valid) Assert.Equal(value.Trim(), LibraryEditRules.Name(value));
        else Assert.Throws<ArgumentException>(() => LibraryEditRules.Name(value));
    }
    [Fact]
    public void Unicode标量临界长度和无效代理项不能混淆()
    {
        Assert.Equal(200, LibraryEditRules.Name(string.Concat(Enumerable.Repeat("😃", 100))).Length);
        Assert.Throws<ArgumentException>(() => LibraryEditRules.Name(string.Concat(Enumerable.Repeat("😃", 101))));
        Assert.Throws<ArgumentException>(() => LibraryEditRules.Name("\ud800"));
        Assert.Equal("一\n 二\n", LibraryEditRules.Description("一\r\n 二\r"));
        Assert.Equal("", LibraryEditRules.Description(""));
        Assert.Equal(1000, LibraryEditRules.Description(new string('字', 1000)).Length);
        Assert.Throws<ArgumentException>(() => LibraryEditRules.Description(new string('字', 1001)));
        Assert.Throws<ArgumentException>(() => LibraryEditRules.Description("\0"));
        Assert.Equal(new long[] { 3, 1, 2 }, LibraryEditRules.Tracks([3, 1, 3, 2]));
        Assert.Throws<ArgumentException>(() => LibraryEditRules.Tracks([]));
        Assert.Throws<ArgumentException>(() => LibraryEditRules.Tracks([0]));
        Assert.Throws<ArgumentException>(() => LibraryEditRules.Tracks(Enumerable.Range(1, 51).Select(i => (long)i).ToArray()));
    }
    [Theory, InlineData(LibraryPlaylistKind.Normal, 123, true), InlineData(LibraryPlaylistKind.Normal, 456, false), InlineData(LibraryPlaylistKind.Liked, 123, false), InlineData(LibraryPlaylistKind.Shared, 123, false), InlineData(LibraryPlaylistKind.Unknown, 123, false)]
    public void 权限只允许身份和类型均明确的自有普通歌单(LibraryPlaylistKind kind, long owner, bool allowed)
    {
        var p = new LibraryPlaylist(7, "任意名称不参与权限", "", true, owner, kind, null, [], true, 0);
        Assert.Equal(allowed, p.CanEdit(123)); Assert.False(p.CanSubscribe(123));
        Assert.False(p.CanEdit(0));
    }
    [Fact]
    public void 部分结果按唯一曲目逐项分类并保留原有成员()
    {
        var result = LibraryEditRules.Classify([1, 2, 3], [1], [1, 2], true);
        Assert.Equal(new[] { LibraryItemState.AlreadyPresent, LibraryItemState.Added, LibraryItemState.NeedsRecheck }, result.Select(r => r.State));
        Assert.Equal(new[] { LibraryItemState.Removed, LibraryItemState.AlreadyAbsent }, LibraryEditRules.Classify([1, 2], [1], [], false).Select(r => r.State));
    }
}
