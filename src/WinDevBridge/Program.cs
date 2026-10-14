using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinDevBridge;

// 設定は Claude Code が書き換えられない場所(この実行ファイルの隣)からだけ読む。引数や環境変数では変えさせない。
var settings = BridgeSettings.Load(Path.Combine(AppContext.BaseDirectory, BridgeSettings.FileName));

var builder = Host.CreateApplicationBuilder();
// 標準出力は MCP の通信路なので、ログはすべて標準エラー出力へ出す。Windows で既定で入るイベントログへは書かない。
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(settings);
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<BridgeTools>();
await builder.Build().RunAsync();
