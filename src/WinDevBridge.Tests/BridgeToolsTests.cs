using ModelContextProtocol;

namespace WinDevBridge.Tests;

public sealed class BridgeToolsTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("BridgeToolsTests").FullName;
    readonly BridgeTools tools;

    public BridgeToolsTests() => tools = Tools(new VisualStudioTools(null, null, MissingVswhere));

    string MissingVswhere => Path.Combine(root, "missing", "vswhere.exe");

    BridgeTools Tools(VisualStudioTools visualStudio)
    {
        var apps = new Dictionary<string, string> { ["Sample"] = "Sample.exe", ["Viewer"] = "Viewer.exe" };
        return new(new BridgeSettings(root, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), apps), visualStudio);
    }

    // C++ のビルドとテストが、どのツールに何を渡したかを、引数をそのまま出す偽のツールで確かめる。
    BridgeTools ToolsWithEchoingVisualStudio()
        => Tools(new VisualStudioTools(FakeTool.EchoingArguments(root, "MSBuild"), FakeTool.EchoingArguments(root, "vstest"), MissingVswhere));

    void CreateFile(string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
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

    [Fact]
    public async Task CppProjectIsBuiltWithMSBuild()
    {
        CreateFile("Native/Native.vcxproj");

        var report = await ToolsWithEchoingVisualStudio().BuildAsync("Native/Native.vcxproj", "Release", "x64");

        Assert.StartsWith("結果: 成功", report);
        Assert.Contains("-p:Configuration=Release -p:Platform=x64", report);
    }

    [Fact]
    public async Task CppProjectWithoutVisualStudioIsRejectedWithSettingName()
    {
        CreateFile("Native/Native.vcxproj");

        var error = await Assert.ThrowsAsync<McpException>(() => tools.BuildAsync("Native/Native.vcxproj"));

        Assert.Contains("msbuildPath", error.Message);
    }

    [Fact]
    public async Task TestDllIsRunWithVSTest()
    {
        CreateFile("x64/Debug/Native.Tests.dll");

        var report = await ToolsWithEchoingVisualStudio().TestAsync("x64/Debug/Native.Tests.dll", "Priority=1");

        Assert.StartsWith("結果: 成功", report);
        Assert.Contains("Native.Tests.dll /TestCaseFilter:Priority=1", report);
    }

    [Fact]
    public async Task TestOfCppProjectIsRejectedWithHintToPassDll()
    {
        CreateFile("Native/Native.vcxproj");

        var error = await Assert.ThrowsAsync<McpException>(() => tools.TestAsync("Native/Native.vcxproj"));

        Assert.Contains("DLL", error.Message);
    }
}
