namespace WinDevBridge.Tests;

public sealed class BridgeSettingsTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("BridgeSettingsTests").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    static string Quote(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    string CreateFile(string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "");
        return path;
    }

    string Json(string body) => "{ \"workspaceRoot\": " + System.Text.Json.JsonSerializer.Serialize(root) + body + " }";

    [Fact]
    public void MinimalSettingsUseDefaultTimeouts()
    {
        var settings = BridgeSettings.Parse(Json(""));

        Assert.Equal(root, settings.WorkspaceRoot);
        Assert.Equal(TimeSpan.FromMinutes(10), settings.BuildTimeout);
        Assert.Equal(TimeSpan.FromMinutes(15), settings.TestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.MaxObservation);
        Assert.Empty(settings.Apps);
        Assert.Null(settings.MSBuildPath);
        Assert.Null(settings.VSTestPath);
    }

    [Fact]
    public void VisualStudioToolPathsAreRead()
    {
        var msbuild = CreateFile("MSBuild.exe");
        var vstest = CreateFile("vstest.console.exe");

        var settings = BridgeSettings.Parse(Json(", \"msbuildPath\": " + Quote(msbuild) + ", \"vstestPath\": " + Quote(vstest)));

        Assert.Equal(msbuild, settings.MSBuildPath);
        Assert.Equal(vstest, settings.VSTestPath);
    }

    [Fact]
    public void RelativeToolPathIsAnError()
        => Assert.Throws<InvalidDataException>(() => BridgeSettings.Parse(Json(""", "msbuildPath": "MSBuild.exe" """)));

    [Fact]
    public void MissingToolFileIsAnError()
        => Assert.Throws<InvalidDataException>(() => BridgeSettings.Parse(Json(", \"vstestPath\": " + Quote(Path.Combine(root, "missing.exe")))));

    [Fact]
    public void TimeoutsAndAppsAreRead()
    {
        var settings = BridgeSettings.Parse(Json("""
            , "buildTimeoutSeconds": 120, "testTimeoutSeconds": 300, "maxObservationSeconds": 20,
              "apps": { "Sample": "samples/Sample/bin/Debug/Sample.exe" }
            """));

        Assert.Equal(TimeSpan.FromSeconds(120), settings.BuildTimeout);
        Assert.Equal(TimeSpan.FromSeconds(300), settings.TestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(20), settings.MaxObservation);
        Assert.Equal("samples/Sample/bin/Debug/Sample.exe", settings.Apps["Sample"]);
    }

    [Fact]
    public void MissingWorkspaceRootIsAnError()
        => Assert.Throws<InvalidDataException>(() => BridgeSettings.Parse("{}"));

    [Fact]
    public void RelativeWorkspaceRootIsAnError()
        => Assert.Throws<InvalidDataException>(() => BridgeSettings.Parse("""{ "workspaceRoot": "repos/app" }"""));

    [Fact]
    public void MissingWorkspaceFolderIsAnError()
        => Assert.Throws<InvalidDataException>(() => BridgeSettings.Parse(
            "{ \"workspaceRoot\": " + System.Text.Json.JsonSerializer.Serialize(Path.Combine(root, "missing")) + " }"));

    [Fact]
    public void NonPositiveTimeoutIsAnError()
        => Assert.Throws<InvalidDataException>(() => BridgeSettings.Parse(Json(""", "buildTimeoutSeconds": 0""")));
}
