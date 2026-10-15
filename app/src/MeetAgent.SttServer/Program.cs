using MeetAgent.Core;
using MeetAgent.SttServer;
using MeetAgent.Stt.Whisper;

// meetagent-stt [--model small-q5_1] [--urls http://127.0.0.1:8083]
// Token opcional en MEETAGENT_STT_TOKEN (Authorization: Bearer …).
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = SttEndpoints.MaxUploadBytes);
if (builder.Configuration["urls"] is null) builder.WebHost.UseUrls("http://127.0.0.1:8083");

var model = AppPaths.Model(builder.Configuration["model"] ?? "small-q5_1");
await using var transcriber = new WhisperTranscriber(model, WhisperTranscriber.DefaultThreads, adaptiveContext: true);
var app = builder.Build();
app.MapStt(transcriber, Environment.GetEnvironmentVariable("MEETAGENT_STT_TOKEN"),
    dumpDir: Environment.GetEnvironmentVariable("MEETAGENT_STT_DUMP_DIR") is { Length: > 0 } d ? d : null);
app.Logger.LogInformation("meetagent-stt · {Model}", transcriber.Name);
await app.RunAsync();
