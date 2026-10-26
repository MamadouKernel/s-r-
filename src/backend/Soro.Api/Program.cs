using System.Text.Json.Serialization;
using Soro.Api;
using Soro.Api.Authentication;
using Soro.Api.Endpoints;
using Soro.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSoroAuthentication(builder.Configuration);
builder.Services.AddSoroRateLimiting(builder.Configuration);
builder.Services.AddSoroApplication(builder.Configuration, builder.Environment);
var connectionString = builder.Configuration.GetConnectionString("Soro")
    ?? throw new InvalidOperationException("La chaîne de connexion ConnectionStrings:Soro n'est pas configurée.");
builder.Services.AddSoroPersistence(connectionString);
builder.Services.AddHostedService(provider => new DatabaseRoleCheck(connectionString, provider.GetRequiredService<IConfiguration>()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.RespectNullableAnnotations = true;
});

var app = builder.Build();

if (ForwardedHeadersSetup.OptionsFrom(app.Configuration) is { } forwarded)
{
    app.UseForwardedHeaders(forwarded);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSecurityHeaders();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseTenantScope();
app.UseDeviceAccess();
app.UseSessionAccess();
app.UseSubscriptionAccess();
app.UseRateLimiter();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapSignupEndpoints();
app.MapDeviceEnrollment();

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapRegisterSessionEndpoints();
api.MapSaleEndpoints();
api.MapUserEndpoints();
api.MapProductEndpoints();
api.MapProductImportEndpoints();
api.MapReportingEndpoints();
api.MapDeviceEndpoints();
api.MapCustomerEndpoints();
api.MapSubscriptionEndpoints();
api.MapDataExportEndpoints();

app.Run();

// Rend le point d'entrée accessible aux tests d'intégration (WebApplicationFactory).
public partial class Program;
