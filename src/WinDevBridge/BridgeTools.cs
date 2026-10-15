using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace WinDevBridge;

/// <summary>Claude Code に見せる操作。ビルド、テスト、許可したアプリの起動と終了だけを置く。</summary>
[McpServerToolType]
public sealed class BridgeTools(BridgeSettings settings, VisualStudioTools visualStudio)
{
    // ビルドとテストが同時に走ると、同じ bin と obj を取り合う。要求は一つずつ処理する。
    static readonly SemaphoreSlim OneAtATime = new(1, 1);

    readonly Workspace workspace = new(settings.WorkspaceRoot);

    [McpServerTool(Name = "build")]
    [Description("Windows でプロジェクトかソリューションをビルドする。C# は dotnet、C++(.vcxproj と、それを含むソリューション)は Visual Studio の MSBuild を使う。結果(成功・失敗・時間切れ)と出力を返す。")]
    public Task<string> BuildAsync(
        [Description("作業フォルダーからの相対パス(例: samples/App/App.csproj、samples/Native/Native.vcxproj)")] string project,
        [Description("Debug か Release")] string configuration = "Debug",
        [Description("x64、Win32、x86、ARM64 のどれか。省略するとプロジェクトの既定")] string? platform = null)
        => Accept(async () =>
        {
            var path = workspace.ResolveProject(project);
            var command = ProjectFile.NeedsMSBuild(path)
                ? MSBuildCommand.Build(await visualStudio.MSBuildAsync(), path, configuration, platform)
                : DotnetCommand.Build(path, configuration, platform);
            return await RunAsync(command, settings.BuildTimeout);
        });

    [McpServerTool(Name = "test")]
    [Description("Windows で単体テストを実行する。C# のプロジェクトかソリューションは dotnet test(必要ならビルドもする)、C++ のテストはビルド済みの DLL を vstest.console で実行する。結果と出力を返す。")]
    public Task<string> TestAsync(
        [Description("テストのプロジェクトかソリューション、または C++ のテストのビルドした DLL の、作業フォルダーからの相対パス(例: samples/Native/x64/Debug/Native.Tests.dll)")] string project,
        [Description("テストを絞る式(例: FullyQualifiedName~Temperature)。省略するとすべて実行する")] string? filter = null)
        => Accept(async () =>
        {
            var path = workspace.ResolveTestTarget(project);
            var command = IsTestDll(path)
                ? VSTestCommand.Test(await visualStudio.VSTestAsync(), path, filter)
                : DotnetCommand.Test(path, filter);
            return await RunAsync(command, settings.TestTimeout);
        });

    [McpServerTool(Name = "run_app")]
    [Description("許可されたアプリを Windows で起動し、指定の秒数だけ観察した後に必ず終了させる。メインウィンドウの有無、応答の有無、終わり方を返す。")]
    public Task<string> RunAppAsync(
        [Description("bridge.json の apps に登録したアプリの名前")] string app,
        [Description("観察する秒数")] int seconds = 10)
        => Accept(() =>
        {
            var observation = Observation(seconds);
            var executable = workspace.ResolveFile(AppPath(app));
            return OneAtATimeAsync(async () =>
                ToolReport.Describe(await AppRunner.RunAsync(new(executable, []), Path.GetDirectoryName(executable)!, observation)));
        });

    static bool IsTestDll(string path) => Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase);

    Task<string> RunAsync(CommandLine command, TimeSpan timeout)
        => OneAtATimeAsync(async () =>
            ToolReport.Describe(await ProcessRunner.RunAsync(command, settings.WorkspaceRoot, timeout), timeout));

    static async Task<string> OneAtATimeAsync(Func<Task<string>> operation)
    {
        await OneAtATime.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    TimeSpan Observation(int seconds)
    {
        var max = (int)settings.MaxObservation.TotalSeconds;
        return seconds is >= 1 && seconds <= max
            ? TimeSpan.FromSeconds(seconds)
            : throw new RequestRejectedException($"観察する秒数は 1〜{max} 秒で指定してください: {seconds}");
    }

    string AppPath(string app)
        => settings.Apps.TryGetValue(app, out var path)
            ? path
            : throw new RequestRejectedException($"登録されていないアプリです: {app}(登録済み: {string.Join(", ", settings.Apps.Keys)})");

    // 断った理由は、MCP のエラー(isError)として Claude Code に返す。
    static async Task<string> Accept(Func<Task<string>> operation)
    {
        try
        {
            return await operation();
        }
        catch (RequestRejectedException rejected)
        {
            throw new McpException(rejected.Message, rejected);
        }
    }
}
