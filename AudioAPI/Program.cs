using AudioAPI;
using AudioAPI.Multiplayer;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<ManagerService>();
builder.Services.AddSingleton<MultiplayerManager>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(b => b
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowAnyOrigin()
);

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(5)
});

if (Environment.GetEnvironmentVariable("DOMAIN") == null)
    Environment.SetEnvironmentVariable("DOMAIN", "gergov.bg/");

if (Environment.GetEnvironmentVariable("STORAGE") == null)
    Environment.SetEnvironmentVariable("STORAGE", "./Music Database");

if (Environment.GetEnvironmentVariable("ALBUM_COVERS") == null)
    Environment.SetEnvironmentVariable("ALBUM_COVERS", "./Music Database/Album_Covers");

// initialize required service here.
app.Services.GetRequiredService<ManagerService>();
app.Services.GetRequiredService<MultiplayerManager>();

app.UseAuthorization();
app.MapControllers();
app.Run();