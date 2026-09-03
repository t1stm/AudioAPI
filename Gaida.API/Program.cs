using Gaida.API;
using Gaida.Core;
using Serilog;
using Serilog.Core;
using Serilog.Templates;
using Serilog.Templates.Themes;

// `dotnet run -- selftest` runs the pure-logic check below without needing a listening host —
// mirrors Gaida.Pods.MusicDatabase/Program.cs's RunSelfCheck.
if (args.Contains("--self-check"))
{
    RunSelfCheck();
    return;
}

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new ExpressionTemplate(
        "[{@t:HH:mm:ss} {@l:u3}" +
        "{#if SourceContext is not null} {Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}{#end}] {@m}\n{@x}",
        theme: TemplateTheme.Code))
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSerilog();
builder.Services.AddHttpClient();
// Wide open on purpose: everything served here is public read-only audio and
// there are no cookies or credentials to protect, while an allowlist quietly
// costs us the Discord activity, every preview deploy and every LAN device.
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddSingleton(Log.Logger);
builder.Services.AddSingleton<ManagerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseCors("Frontend");

// Builds the platform pod list from config; nothing to warm up otherwise, since Gaida.API holds no cache.
app.Services.GetRequiredService<ManagerService>();

app.UseAuthorization();
app.MapControllers();
app.Run();
return;

// The one runnable check: `dotnet run --project Gaida.API -- selftest`. Exercises the classify
// fan-out's fallback rule (nobody claims → keyword search) without a listening host or any pods up.
static void RunSelfCheck()
{
    var manager = new AudioManager(Logger.None);
    var claim = manager.ClassifyAsync("some random text").GetAwaiter().GetResult();
    Assert(claim is { Kind: QueryType.Keywords, Error: null, Query: "some random text" },
        "classify: no platform pod claiming the query falls back to a keyword search");

    Console.WriteLine("selftest OK");
    return;

    void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception($"selftest failed: {message}");
    }
}