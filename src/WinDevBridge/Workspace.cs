namespace WinDevBridge;

/// <summary>ブリッジが触ってよいフォルダー。相対パスを、その中にある実在のファイルの絶対パスに解決する。</summary>
public sealed class Workspace(string root)
{
    static readonly string[] ProjectExtensions = [".csproj", ".vcxproj", ".sln", ".slnx"];

    // C++ のネイティブのテストは、プロジェクトからではなく、ビルドしたテストの DLL から実行する。
    static readonly string[] TestTargetExtensions = [".csproj", ".sln", ".slnx", ".dll"];

    static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    readonly string rootWithSeparator = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

    public string ResolveProject(string relativePath)
        => ResolveFileOfKind(relativePath, ProjectExtensions, "プロジェクトかソリューション");

    public string ResolveTestTarget(string relativePath)
        => ResolveFileOfKind(relativePath, TestTargetExtensions, "テストのプロジェクトかソリューション、または C++ のテストのビルドした DLL");

    string ResolveFileOfKind(string relativePath, string[] extensions, string kind)
    {
        var path = ResolveFile(relativePath);
        if (!extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new RequestRejectedException($"{kind}({string.Join(", ", extensions)})を指定してください: {relativePath}");
        return path;
    }

    public string ResolveFile(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new RequestRejectedException($"作業フォルダーからの相対パスで指定してください: {relativePath}");

        var path = Path.GetFullPath(Path.Combine(rootWithSeparator, relativePath));
        if (!path.StartsWith(rootWithSeparator, PathComparison))
            throw new RequestRejectedException($"作業フォルダーの外は指定できません: {relativePath}");
        if (!File.Exists(path))
            throw new RequestRejectedException($"ファイルがありません: {relativePath}");
        return path;
    }
}
