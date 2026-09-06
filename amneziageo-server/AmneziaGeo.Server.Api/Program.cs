var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => new
{
    status = "ok",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
    time = DateTimeOffset.UtcNow
});

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
