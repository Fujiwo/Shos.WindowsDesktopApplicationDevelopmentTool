using System.Text;

namespace WinDevBridge.Tests;

public class ConsoleTextTests
{
    static readonly Encoding ShiftJis = CodePagesEncodingProvider.Instance.GetEncoding(932)!;

    [Fact]
    public void Utf8JapaneseIsDecodedAsUtf8()
        => Assert.Equal("ビルドに成功しました。", ConsoleText.Decode(Encoding.UTF8.GetBytes("ビルドに成功しました。")));

    [Fact]
    public void ShiftJisJapaneseIsDecodedAsShiftJis()
        => Assert.Equal("ビルドに失敗しました。", ConsoleText.Decode(ShiftJis.GetBytes("ビルドに失敗しました。")));

    [Fact]
    public void AsciiIsDecodedAsIs()
        => Assert.Equal("Build succeeded.", ConsoleText.Decode(Encoding.ASCII.GetBytes("Build succeeded.")));

    [Fact]
    public void EmptyBytesAreDecodedAsEmptyText()
        => Assert.Equal("", ConsoleText.Decode([]));

    [Fact]
    public void Utf8ByteOrderMarkIsRemoved()
        => Assert.Equal("成功", ConsoleText.Decode([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("成功")]));
}
