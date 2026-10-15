namespace WinDevBridge.Tests;

public sealed class ProjectFileTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("ProjectFileTests").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    string Create(string name, string content = "")
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void CppProjectNeedsMSBuild()
        => Assert.True(ProjectFile.NeedsMSBuild(Create("App.vcxproj")));

    [Fact]
    public void CSharpProjectDoesNotNeedMSBuild()
        => Assert.False(ProjectFile.NeedsMSBuild(Create("App.csproj")));

    [Fact]
    public void SolutionWithCppProjectNeedsMSBuild()
        => Assert.True(ProjectFile.NeedsMSBuild(Create("App.sln",
            """Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "App", "App\App.vcxproj", "{1A2B}" """)));

    [Fact]
    public void SlnxWithCppProjectNeedsMSBuild()
        => Assert.True(ProjectFile.NeedsMSBuild(Create("App.slnx", """<Solution><Project Path="App/App.VCXPROJ" /></Solution>""")));

    [Fact]
    public void SolutionWithOnlyCSharpProjectsDoesNotNeedMSBuild()
        => Assert.False(ProjectFile.NeedsMSBuild(Create("App.slnx", """<Solution><Project Path="App/App.csproj" /></Solution>""")));
}
