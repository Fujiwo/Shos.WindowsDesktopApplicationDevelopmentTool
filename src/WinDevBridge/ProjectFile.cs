namespace WinDevBridge;

/// <summary>ビルドするプロジェクトかソリューションのファイル。どのツールでビルドするかを答える。</summary>
public static class ProjectFile
{
    const string CppProjectExtension = ".vcxproj";

    /// <summary>
    /// C++ のプロジェクト、またはそれを含むソリューションなら true。
    /// dotnet build は .vcxproj をビルドできないので、Visual Studio の MSBuild.exe が要る。
    /// </summary>
    public static bool NeedsMSBuild(string path)
        => Path.GetExtension(path).Equals(CppProjectExtension, StringComparison.OrdinalIgnoreCase)
            || IsSolution(path) && File.ReadAllText(path).Contains(CppProjectExtension, StringComparison.OrdinalIgnoreCase);

    static bool IsSolution(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx";
}
