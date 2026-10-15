namespace WinDevBridge;

/// <summary>ブリッジが許す dotnet の操作(ビルドとテスト)のコマンドライン。</summary>
public static class DotnetCommand
{
    // ビルドサーバー(MSBuild のノード、VBCSCompiler)が残ると、bin や obj を握ったままになり、Dropbox の同期や次の編集とぶつかる。
    static readonly string[] CommonOptions = ["--nologo", "--verbosity", "minimal", "--disable-build-servers"];

    public static CommandLine Build(string projectPath, string configuration, string? platform)
    {
        string[] platformOption = BuildOptions.Platform(platform) is { } p ? [$"--property:Platform={p}"] : [];
        return new("dotnet", ["build", projectPath, "--configuration", BuildOptions.Configuration(configuration), .. CommonOptions, .. platformOption]);
    }

    public static CommandLine Test(string projectPath, string? filter)
        => new("dotnet", ["test", projectPath, .. CommonOptions, .. string.IsNullOrWhiteSpace(filter) ? [] : new[] { "--filter", filter }]);
}
