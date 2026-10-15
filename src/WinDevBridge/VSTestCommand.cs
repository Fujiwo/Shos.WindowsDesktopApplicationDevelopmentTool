namespace WinDevBridge;

/// <summary>
/// ビルド済みのテストの DLL を Visual Studio の vstest.console.exe で実行するコマンドライン。
/// C++ のネイティブのテスト(Microsoft C++ Unit Test Framework)は dotnet test では動かないので、こちらを使う。
/// </summary>
public static class VSTestCommand
{
    public static CommandLine Test(string vstestPath, string testDllPath, string? filter)
        => new(vstestPath, [testDllPath, .. string.IsNullOrWhiteSpace(filter) ? [] : new[] { $"/TestCaseFilter:{filter}" }]);
}
