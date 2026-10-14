namespace WinDevBridge.Tests;

public sealed class WorkspaceTests : IDisposable
{
    readonly string parent = Directory.CreateTempSubdirectory("WorkspaceTests").FullName;
    readonly string root;
    readonly Workspace workspace;

    public WorkspaceTests()
    {
        root = Path.Combine(parent, "ws");
        CreateFile(root, "App/App.csproj");
        CreateFile(root, "App.slnx");
        CreateFile(root, "App/bin/App.exe");
        CreateFile(root, "tools/run.cmd");
        CreateFile(Path.Combine(parent, "ws2"), "Other.csproj");
        workspace = new Workspace(root);
    }

    public void Dispose() => Directory.Delete(parent, recursive: true);

    static void CreateFile(string directory, string relativePath)
    {
        var path = Path.Combine(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    [Fact]
    public void ProjectInsideWorkspaceIsResolvedToFullPath()
        => Assert.Equal(Path.Combine(root, "App", "App.csproj"), workspace.ResolveProject("App/App.csproj"));

    [Fact]
    public void SolutionInsideWorkspaceIsResolved()
        => Assert.Equal(Path.Combine(root, "App.slnx"), workspace.ResolveProject("App.slnx"));

    [Fact]
    public void ProjectOutsideWorkspaceIsRejected()
        => Assert.Throws<RequestRejectedException>(() => workspace.ResolveProject("../ws2/Other.csproj"));

    [Fact]
    public void ProjectEscapingThroughInnerParentIsRejected()
        => Assert.Throws<RequestRejectedException>(() => workspace.ResolveProject("App/../../ws2/Other.csproj"));

    [Fact]
    public void AbsolutePathIsRejected()
        => Assert.Throws<RequestRejectedException>(() => workspace.ResolveProject(Path.Combine(root, "App", "App.csproj")));

    [Fact]
    public void MissingProjectIsRejected()
        => Assert.Throws<RequestRejectedException>(() => workspace.ResolveProject("App/Missing.csproj"));

    [Fact]
    public void FileThatIsNotProjectIsRejected()
        => Assert.Throws<RequestRejectedException>(() => workspace.ResolveProject("tools/run.cmd"));

    [Fact]
    public void ExistingFileInsideWorkspaceIsResolved()
        => Assert.Equal(Path.Combine(root, "App", "bin", "App.exe"), workspace.ResolveFile("App/bin/App.exe"));

    [Fact]
    public void FileOutsideWorkspaceIsRejected()
        => Assert.Throws<RequestRejectedException>(() => workspace.ResolveFile("../ws2/Other.csproj"));
}
