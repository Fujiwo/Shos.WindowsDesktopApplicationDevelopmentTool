using System.Text;

namespace WinDevBridge;

/// <summary>Windows のコンソールアプリが出力したバイト列を文字列に読む。</summary>
public static class ConsoleText
{
    // 子プロセスは UTF-8 で書くもの(dotnet の多く)と、コンソールのコードページ 932 で書くもの(MSBuild.exe や cmd の一部)が混ざる。
    // 出力ごとにどちらかを決められないので、UTF-8 として正しいバイト列なら UTF-8、そうでなければ Shift-JIS として読む。
    static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    static readonly Encoding ShiftJis;

    static ConsoleText()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ShiftJis = Encoding.GetEncoding(932);
    }

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var body = bytes.StartsWith(Encoding.UTF8.Preamble) ? bytes[Encoding.UTF8.Preamble.Length..] : bytes;
        try
        {
            return StrictUtf8.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            return ShiftJis.GetString(body);
        }
    }
}
