namespace WinDevBridge;

/// <summary>ブリッジが許す dotnet の操作(ビルドとテスト)のコマンドライン。</summary>
public static class DotnetCommand
{
    static readonly string[] Configurations = ["Debug", "Release"];

    // ビルドサーバー(MSBuild のノード、VBCSCompiler)が残ると、bin や obj を握ったままになり、Dropbox の同期や次の編集とぶつかる。
    static readonly string[] CommonOptions = ["--nologo", "--verbosity", "minimal", "--disable-build-servers"];

    public static CommandLine Build(string projectPath, string configuration)
    {
        if (!Configurations.Contains(configuration))
            throw new RequestRejectedException($"構成は {string.Join(" か ", Configurations)} を指定してください: {configuration}");
        return new("dotnet", ["build", projectPath, "--configuration", configuration, .. CommonOptions]);
    }

    public static CommandLine Test(string projectPath, string? filter)
        => new("dotnet", ["test", projectPath, .. CommonOptions, .. string.IsNullOrWhiteSpace(filter) ? [] : new[] { "--filter", filter }]);
}
