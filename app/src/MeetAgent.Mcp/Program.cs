using MeetAgent.Control;
using MeetAgent.Core;
using MeetAgent.Core.Ports;
using MeetAgent.Mcp;
using MeetAgent.Storage.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Servidor MCP por stdio: stdout es del protocolo, todo log va a stderr.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton<IAppControl>(_ => new ControlClient());
builder.Services.AddSingleton<Func<ITranscriptStore>>(_ => () => new SqliteTranscriptStore($"Data Source={AppPaths.Database}"));
builder.Services.AddSingleton<MeetingTools>();
builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "meetagent", Version = "0.1.0" })
    .WithStdioServerTransport()
    .WithTools<McpTools>();

await builder.Build().RunAsync();
