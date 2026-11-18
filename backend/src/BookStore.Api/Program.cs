using System.Text.Json;
using System.Text.Json.Serialization;
using BookStore.Api.Auth;
using BookStore.Api.Infrastructure;
using BookStore.Infrastructure;
using BookStore.Infrastructure.Persistence;
using BookStore.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

const string FrontendCorsPolicy = "Frontend";

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddServices()
    .AddInfrastructure(builder.Configuration);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => ConfigureJson(o.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(o => ConfigureJson(o.SerializerOptions));

builder.Services.AddJwtAuthentication();
builder.Services.AddRateLimiter(RateLimits.Add);

builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks().AddDbContextCheck<BookStoreDbContext>();

builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    // The guest session cookie must be sent with cross-origin requests from the SPA.
    .AllowCredentials()));

// Behind the frontend's nginx, the client's address comes in X-Forwarded-For. It is trusted only from the
// configured proxy networks (ReverseProxy:TrustedNetworks), so a client can't fake its address to escape
// the per-IP rate limits. Loopback proxies (e.g. the Angular dev server) are trusted by default.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var network in builder.Configuration.GetSection("ReverseProxy:TrustedNetworks").Get<string[]>() ?? [])
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

var app = builder.Build();

app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);
app.UseGuestSession();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Enums as strings. Numbers strictly as JSON numbers: the web default also accepts them as strings, which the
// OpenAPI document then describes as "integer or string" (and the frontend's generated types as number | string).
static void ConfigureJson(JsonSerializerOptions options)
{
    options.Converters.Add(new JsonStringEnumConverter());
    options.NumberHandling = JsonNumberHandling.Strict;
}

// Exposed for integration tests (WebApplicationFactory).
public partial class Program;
