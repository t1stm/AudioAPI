var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (Environment.GetEnvironmentVariable("DOMAIN") == null) 
    Environment.SetEnvironmentVariable("DOMAIN", "gergov.bg/");

if (Environment.GetEnvironmentVariable("STORAGE") == null) 
    Environment.SetEnvironmentVariable("STORAGE", "./Music Database");

if (Environment.GetEnvironmentVariable("ALBUM_COVERS") == null) 
    Environment.SetEnvironmentVariable("ALBUM_COVERS", "./Music Database/Album_Covers");

app.UseAuthorization();
app.MapControllers();
app.Run();