namespace WinDevBridge;

/// <summary>ブリッジが許していない要求を断ったことを表す。メッセージはそのまま Claude Code に返す。</summary>
public sealed class RequestRejectedException(string message) : Exception(message);
