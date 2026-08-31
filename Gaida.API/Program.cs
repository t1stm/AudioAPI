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
foreach (var key in (string[]) ["DOMAIN", "STORAGE", "ALBUM_COVERS"])
    Environment.SetEnvironmentVariable(key, builder.Configuration[key]);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSerilog();
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .SetIsOriginAllowed(origin => IsFrontendOrLocalVite(origin, configuredOrigins))
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
    KeepAliveInterval = TimeSpan.FromSeconds(5)
});

// initialize required services here.
app.Services.GetRequiredService<ManagerService>();
app.Services.GetRequiredService<MultiplayerManager>();

app.UseAuthorization();
app.MapControllers();
app.Run();

static bool IsFrontendOrLocalVite(string origin, IReadOnlyCollection<string> configuredOrigins)
{
    if (configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;

    return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
           uri.Host.Equals("127.0.0.1", StringComparison.Ordinal) ||
           uri.Host.Equals("::1", StringComparison.Ordinal) ||
           uri.Host.Equals("gergov.bg", StringComparison.OrdinalIgnoreCase) ||
           uri.Host.EndsWith(".gergov.bg", StringComparison.OrdinalIgnoreCase);
}
