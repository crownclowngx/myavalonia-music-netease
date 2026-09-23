using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Application.Playback;
using Xunit;

namespace MusicNetEasePlugin.Tests;

/// <summary>
/// 同一套生产页面在 V4/V5 上采样；只替换网络和声卡，报告明确标为 Headless，不冒充真实 Host 帧率。
/// 常规门禁采短样本检查证据结构，专用对比通过环境变量延长至三轮、每场景六十秒。
/// 不用一次 GC 后的下降证明无泄漏；页面引用和缓存释放另有确定性测试。
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class UiResourceObservationTests
{
    [Fact]
    public async Task 同条件界面资源采样保留环境与测量边界()
    {
        await HeadlessSessions.Get(typeof(DailyPlayerAcceptanceTests)).Dispatch(async () =>
        {
            await using var f = new DailyPlayerAcceptanceTests.Fixture(); await f.Initialize();
            using var page = f.Page();
            var window = new Window { Content = page.View, Width = 1200, Height = 720 };
            using var process = Process.GetCurrentProcess();
            var observations = new List<object>();
            var seconds = int.TryParse(Environment.GetEnvironmentVariable("NETEASE_OBSERVE_SECONDS"), out var value) ? Math.Clamp(value, 1, 60) : 1;
            var rounds = seconds == 60 ? 3 : 1;
            try
            {
                window.Show(); DesktopUiTests.Pump(window);
                await f.Player.Queue.ReplaceAsync(Enumerable.Range(1, 10000).Select(i => QueueEntry.FromTrack(MusicCatalog.Track(i))).ToArray(), 0, default);
                await f.Lyrics.Pending; DesktopUiTests.Pump(window);
                for (var round = 0; round < rounds; round++)
                {
                    var latency = new List<double>();
                    for (var i = 0; i < 120; i++)
                    {
                        var watch = Stopwatch.StartNew();
                        page.Music.ShowQueueCommand.Execute(null); DesktopUiTests.Pump(window);
                        page.Music.ShowSearchCommand.Execute(null);
                        if (page.Music.Navigation.IsQueue) page.Music.ShowQueueCommand.Execute(null);
                        DesktopUiTests.Pump(window);
                        if (i >= 20) latency.Add(watch.Elapsed.TotalMilliseconds);
                    }
                    latency.Sort();
                    foreach (var scenario in new[] { "paused-visible", "lyrics-visible", "playing-hidden" })
                    {
                        page.Music.ShowSearchCommand.Execute(null);
                        page.Music.ShowLyricsCommand.Execute(null);
                        page.View.IsVisible = scenario != "playing-hidden";
                        await f.Player.Queue.PauseAsync(scenario == "paused-visible", default); DesktopUiTests.Pump(window);
                        var allocated = GC.GetTotalAllocatedBytes(); var cpu = process.TotalProcessorTime; var clock = Stopwatch.StartNew();
                        long peak = 0;
                        while (clock.Elapsed.TotalSeconds < seconds)
                        {
                            if (scenario != "paused-visible") f.Player.Audio.Emit(f.Player.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, (long)clock.Elapsed.TotalMilliseconds);
                            DesktopUiTests.Pump(window); process.Refresh(); peak = Math.Max(peak, process.PrivateMemorySize64);
                            await Task.Delay(200);
                        }
                        process.Refresh();
                        observations.Add(new { round, scenario, elapsedMs = clock.Elapsed.TotalMilliseconds,
                            privateBytes = process.PrivateMemorySize64, peakPrivateBytes = peak,
                            allocatedBytes = GC.GetTotalAllocatedBytes() - allocated,
                            cpuOneCorePercent = (process.TotalProcessorTime - cpu).TotalMilliseconds / clock.Elapsed.TotalMilliseconds * 100,
                            visibleRows = page.View.GetVisualDescendants().OfType<ListBoxItem>().Count(row => row.IsEffectivelyVisible),
                            navigationSamples = latency.Count, navigationPairP50Ms = latency[49], navigationPairP95Ms = latency[94] });
                        page.View.IsVisible = true;
                    }
                }
                Assert.Equal(10000, page.Music.Queue.Rows.Count);
                TestEvidence.Write("ui-resource-observation.json", new { schemaVersion = 1, realHost = false,
                    environment = "Windows x64 / Avalonia Headless + Skia / fake audio and network", seconds, rounds,
                    processId = process.Id, processorCount = Environment.ProcessorCount, framework = Environment.Version.ToString(),
                    width = 1200, height = 720, queueEntries = 10000, lyricLines = 1000, observations });
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
}
