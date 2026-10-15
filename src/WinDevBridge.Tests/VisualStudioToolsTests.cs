namespace WinDevBridge.Tests;

public sealed class VisualStudioToolsTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("VisualStudioToolsTests").FullName;
    string MissingVswhere => Path.Combine(root, "missing", "vswhere.exe");

    public void Dispose() => Directory.Delete(root, recursive: true);

    string CreateVswhere(params string[] lines) => FakeTool.Printing(root, "vswhere", lines);

    string CreateVswhereEchoingArguments() => FakeTool.EchoingArguments(root, "vswhere");

    [Fact]
    public async Task ConfiguredMSBuildIsUsedWithoutVswhere()
        => Assert.Equal(@"C:\MSBuild.exe", await new VisualStudioTools(@"C:\MSBuild.exe", null, MissingVswhere).MSBuildAsync());

    [Fact]
    public async Task ConfiguredVSTestIsUsedWithoutVswhere()
        => Assert.Equal(@"C:\vstest.console.exe", await new VisualStudioTools(null, @"C:\vstest.console.exe", MissingVswhere).VSTestAsync());

    [Fact]
    public async Task MSBuildIsSearchedInVisualStudioWithCppWorkload()
    {
        var found = await new VisualStudioTools(null, null, CreateVswhereEchoingArguments()).MSBuildAsync();

        Assert.Contains("-requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64", found);
        Assert.Contains(@"-find MSBuild\**\Bin\MSBuild.exe", found);
    }

    [Fact]
    public async Task VSTestIsSearchedInVisualStudio()
    {
        var found = await new VisualStudioTools(null, null, CreateVswhereEchoingArguments()).VSTestAsync();

        Assert.Contains(@"-find Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe", found);
    }

    [Fact]
    public async Task FirstPathFoundByVswhereIsUsed()
        => Assert.Equal("/vs/18/MSBuild.exe", await new VisualStudioTools(null, null, CreateVswhere("/vs/18/MSBuild.exe", "/vs/17/MSBuild.exe")).MSBuildAsync());

    [Fact]
    public async Task NothingFoundByVswhereIsRejectedWithSettingName()
    {
        var error = await Assert.ThrowsAsync<RequestRejectedException>(() => new VisualStudioTools(null, null, CreateVswhere()).MSBuildAsync());

        Assert.Contains("msbuildPath", error.Message);
    }

    [Fact]
    public async Task MissingVswhereIsRejectedWithSettingName()
    {
        var error = await Assert.ThrowsAsync<RequestRejectedException>(() => new VisualStudioTools(null, null, MissingVswhere).VSTestAsync());

        Assert.Contains("vstestPath", error.Message);
    }
}
