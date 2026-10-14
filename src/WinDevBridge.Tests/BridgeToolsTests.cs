using ModelContextProtocol;

namespace WinDevBridge.Tests;

public sealed class BridgeToolsTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("BridgeToolsTests").FullName;
    readonly BridgeTools tools;

    public BridgeToolsTests()
    {
        var apps = new Dictionary<string, string> { ["Sample"] = "Sample.exe", ["Viewer"] = "Viewer.exe" };
        tools = new BridgeTools(new BridgeSettings(root, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), apps));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task BuildOfProjectOutsideWorkspaceIsRejectedWithReason()
    {
        var error = await Assert.ThrowsAsync<McpException>(() => tools.BuildAsync("../Other.csproj"));

        Assert.Contains("作業フォルダーの外", error.Message);
    }

    [Fact]
    public async Task TestOfMissingProjectIsRejectedWithReason()
    {
        var error = await Assert.ThrowsAsync<McpException>(() => tools.TestAsync("Missing.Tests.csproj"));

        Assert.Contains("ファイルがありません", error.Message);
    }

    [Fact]
    public async Task UnknownAppIsRejectedWithKnownAppNames()
    {
        var error = await Assert.ThrowsAsync<McpException>(() => tools.RunAppAsync("Notepad"));

        Assert.Contains("Sample, Viewer", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task ObservationOutsideAllowedRangeIsRejected(int seconds)
    {
        var error = await Assert.ThrowsAsync<McpException>(() => tools.RunAppAsync("Sample", seconds));

        Assert.Contains("1〜30 秒", error.Message);
    }
}
