using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MusicNetEasePlugin.Tests;

/// <summary>产物在断言通过后写入运行身份。门禁核对本轮身份和内容，不能拿旧截图/旧 PCM 报告凑齐文件名。</summary>
internal static class TestEvidence
{
    internal static void Write(string name, object data)
    {
        var root = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS"); if (string.IsNullOrEmpty(root)) return;
        var value = JsonSerializer.SerializeToNode(data)!.AsObject();
        value["provenance"] = JsonSerializer.SerializeToNode(new
        {
            runId = Environment.GetEnvironmentVariable("NETEASE_TEST_RUN_ID"),
            revision = Environment.GetEnvironmentVariable("NETEASE_TEST_REVISION"),
            sourceSha256 = Environment.GetEnvironmentVariable("NETEASE_TEST_SOURCE_SHA256")
        });
        File.WriteAllText(Path.Combine(root, name), value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static void Screenshot(string name, int width, int height)
    {
        var root = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS"); if (string.IsNullOrEmpty(root)) return;
        Write(name + ".json", new { schemaVersion = 1, name, width, height, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, name)))), realHost = false });
    }
}
