namespace WinDevBridge;

/// <summary>シェルを通さずに起動するプログラムと、その引数の並び。</summary>
public sealed record CommandLine(string FileName, IReadOnlyList<string> Arguments);
