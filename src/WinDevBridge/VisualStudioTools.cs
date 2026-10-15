namespace WinDevBridge;

/// <summary>
/// C++ のビルドとテストに使う Visual Studio のツールの場所。
/// bridge.json に書いてあればそれを使い、なければ vswhere で、C++ のワークロードの入った最新の Visual Studio から探す。
/// </summary>
public sealed class VisualStudioTools(string? msbuildPath, string? vstestPath, string vswherePath)
{
    // Visual Studio のインストーラーは、どの版でも vswhere をここに置く。
    public static readonly string DefaultVswherePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");

    const string CppWorkload = "Microsoft.VisualStudio.Component.VC.Tools.x86.x64";
    static readonly TimeSpan VswhereTimeout = TimeSpan.FromSeconds(30);

    public Task<string> MSBuildAsync()
        => msbuildPath is { } path ? Task.FromResult(path) : FindAsync(@"MSBuild\**\Bin\MSBuild.exe", "msbuildPath");

    public Task<string> VSTestAsync()
        => vstestPath is { } path ? Task.FromResult(path) : FindAsync(@"Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe", "vstestPath");

    async Task<string> FindAsync(string pattern, string settingName)
    {
        if (!File.Exists(vswherePath))
            throw NotFound(settingName);
        var outcome = await ProcessRunner.RunAsync(
            new(vswherePath, ["-latest", "-products", "*", "-requires", CppWorkload, "-find", pattern]),
            Path.GetDirectoryName(vswherePath)!, VswhereTimeout);
        return outcome.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            ?? throw NotFound(settingName);
    }

    static RequestRejectedException NotFound(string settingName)
        => new($"C++ のワークロード(C++ によるデスクトップ開発)の入った Visual Studio が見つかりません。" +
               $"インストールするか、{BridgeSettings.FileName} の {settingName} に実行ファイルの絶対パスを書いてください。");
}
