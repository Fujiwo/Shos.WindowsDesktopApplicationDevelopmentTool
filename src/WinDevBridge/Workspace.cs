namespace WinDevBridge;

/// <summary>ブリッジが触ってよいフォルダー。相対パスを、その中にある実在のファイルの絶対パスに解決する。</summary>
public sealed class Workspace(string root)
{
    static readonly string[] ProjectExtensions = [".csproj", ".sln", ".slnx"];

    static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    readonly string rootWithSeparator = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

    public string ResolveProject(string relativePath)
    {
        var path = ResolveFile(relativePath);
        if (!ProjectExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new RequestRejectedException($"プロジェクトかソリューション({string.Join(", ", ProjectExtensions)})を指定してください: {relativePath}");
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
