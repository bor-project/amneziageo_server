using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Status;
using AmneziaGeo.Server.Api.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddListening();
builder.Services.AddControllers();
builder.Services.AddServerAuth(builder.Configuration);
builder.Services.AddOverview();

var app = builder.Build();

app.ReportListening();
app.MigrateDatabase();
app.StartOverview();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseBearer();

app.MapGet("/api/health", () => new
{
    status = "ok",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
    time = DateTimeOffset.UtcNow
});

app.MapOverview();
app.MapAuth();
app.MapUsers();
app.MapRoles();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
