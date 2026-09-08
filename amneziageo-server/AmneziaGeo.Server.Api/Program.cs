using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Balancers;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Geo;
using AmneziaGeo.Server.Api.Outbounds;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Api.Status;
using AmneziaGeo.Server.Api.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddListening();
builder.Services.AddControllers();
builder.Services.AddServerAuth(builder.Configuration);
builder.Services.AddOverview();
builder.Services.AddGeo(builder.Configuration);
builder.Services.AddOutbounds(builder.Configuration);
builder.Services.AddClients(builder.Configuration);
builder.Services.AddResolver();

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
app.MapConfigs();
app.MapClients();
app.MapGeo();
app.MapOutbounds();
app.MapBalancers();
app.MapRules();
app.MapResolver();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
