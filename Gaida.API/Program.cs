using Gaida.API;
using Gaida.API.Multiplayer;
using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new ExpressionTemplate(
        "[{@t:HH:mm:ss} {@l:u3}" +
        "{#if SourceContext is not null} {Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}{#end}] {@m}\n{@x}",
        theme: TemplateTheme.Code))
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// The platform layer reads these as process environment variables; configuration supplies the defaults.
foreach (var key in (string[]) ["DOMAIN", "STORAGE", "ALBUM_COVERS", "YOUTUBE_CACHE_DB"])
    Environment.SetEnvironmentVariable(key, builder.Configuration[key]);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSerilog();
// Wide open on purpose: everything served here is public read-only audio and
// there are no cookies or credentials to protect, while an allowlist quietly
// costs us the Discord activity, every preview deploy and every LAN device.
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddSingleton(Log.Logger);
builder.Services.AddSingleton<ManagerService>();
builder.Services.AddSingleton<MultiplayerManager>();

var app = builder.Build();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseCors("Frontend");

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(1),
    KeepAliveTimeout = TimeSpan.FromSeconds(5)
});

// initialize required services here.
app.Services.GetRequiredService<ManagerService>();
app.Services.GetRequiredService<MultiplayerManager>();

app.UseAuthorization();
app.MapControllers();
app.Run();
