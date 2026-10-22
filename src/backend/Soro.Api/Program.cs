using System.Text.Json.Serialization;
using Soro.Api;
using Soro.Api.Authentication;
using Soro.Api.Endpoints;
using Soro.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSoroAuthentication(builder.Configuration);
builder.Services.AddSoroRateLimiting(builder.Configuration);
builder.Services.AddSoroApplication(builder.Configuration);
builder.Services.AddSoroPersistence(
    builder.Configuration.GetConnectionString("Soro")
    ?? throw new InvalidOperationException("La chaîne de connexion ConnectionStrings:Soro n'est pas configurée."));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.RespectNullableAnnotations = true;
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseTenantScope();
app.UseRateLimiter();
app.UseAuthorization();

app.MapAuthEndpoints();

var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapRegisterSessionEndpoints();
api.MapSaleEndpoints();
api.MapUserEndpoints();
api.MapProductEndpoints();
api.MapReportingEndpoints();

app.Run();

// Rend le point d'entrée accessible aux tests d'intégration (WebApplicationFactory).
public partial class Program;
