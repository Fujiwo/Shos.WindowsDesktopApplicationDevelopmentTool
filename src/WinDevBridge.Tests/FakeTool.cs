namespace WinDevBridge.Tests;

/// <summary>
/// Visual Studio のツール(vswhere、MSBuild.exe、vstest.console.exe)の代わりに置くスクリプト。
/// Windows と Linux の両方でテストを回すため、OS ごとに書き分ける。
/// </summary>
static class FakeTool
{
    public static string Printing(string directory, string name, params string[] lines)
        => Create(directory, name, lines.Select(line => $"echo {line}"));

    // sh の echo はバックスラッシュを解釈する(\v など)ので、printf で引数をそのまま出す。
    public static string EchoingArguments(string directory, string name)
        => Create(directory, name, [OperatingSystem.IsWindows() ? "echo %*" : "printf '%s\\n' \"$*\""]);

    static string Create(string directory, string name, IEnumerable<string> commands)
    {
        var path = Path.Combine(directory, name + (OperatingSystem.IsWindows() ? ".cmd" : ".sh"));
        File.WriteAllLines(path, [OperatingSystem.IsWindows() ? "@echo off" : "#!/bin/sh", .. commands]);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
