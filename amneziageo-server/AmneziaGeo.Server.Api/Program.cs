using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Balancers;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Diagnostics;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Proxy;
using AmneziaGeo.Server.Api.Geo;
using AmneziaGeo.Server.Api.Outbounds;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Api.Status;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSystemd();
builder.AddJournal();
builder.AddListening();
builder.Services.AddControllers();
builder.Services.AddServerAuth(builder.Configuration);
builder.Services.AddOverview();
builder.Services.AddGeo(builder.Configuration);
builder.Services.AddOutbounds(builder.Configuration);
builder.Services.AddEndpoints();
builder.Services.AddClients(builder.Configuration);
builder.Services.AddResolver();
builder.Services.AddPanel();
builder.Services.AddProxies(builder.Configuration);
builder.Services.AddSubscriptions();
builder.Services.AddApiDescription();

var app = builder.Build();

app.ReportListening();
app.MigrateDatabase();
app.SeedPanel();
app.SettleProxies();
app.StartOverview();

app.UseSubscriptions();
app.UsePanel();
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
app.MapTokens();
app.MapConfigs();
app.MapClients();
app.MapTemplates();
app.MapGeo();
app.MapGeoEntries();
app.MapOutbounds();
app.MapBalancers();
app.MapRules();
app.MapResolver();
app.MapPanel();
app.MapSubscriptions();
app.MapProxies();
app.MapDiagnostics();
app.MapApiDescription();
app.MapControllers();
app.MapPanelPage();

app.Run();
