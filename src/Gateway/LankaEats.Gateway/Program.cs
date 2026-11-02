using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using LankaEats.ServiceDefaults;
using LankaEats.ServiceDefaults.Auth;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("gateway");
builder.Services.AddSingleton<JwtTokenFactory>();

// Single entry point: routing, auth and rate limiting happen here, before any request reaches a service.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.PerClient, ctx =>
    {
        // Authenticated callers are limited per user; anonymous callers per IP address.
        var key = ctx.User.FindFirstValue(LankaEatsClaims.Subject)
                  ?? ctx.Connection.RemoteIpAddress?.ToString()
                  ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:PermitPerMinute", 120),
            Window = TimeSpan.FromMinutes(1),
        });
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Azure Container Apps / any load balancer terminates TLS in front of us.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapDefaultEndpoints();

if (app.Configuration.GetValue<bool>("Auth:EnableDemoLogin"))
{
    // DEMO ONLY: issues a token for any display name. In production, replace this with an identity
    // provider (Keycloak is free and self-hosted; Microsoft Entra External ID has a free tier).
    app.MapPost("/auth/demo-token", IResult (DemoLoginRequest request, JwtTokenFactory tokens) =>
        {
            var name = request.DisplayName?.Trim() ?? string.Empty;
            if (name.Length is < 2 or > 50)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["displayName"] = ["Use 2-50 characters."] });

            var role = request.Role == LankaEatsRoles.Admin ? LankaEatsRoles.Admin : LankaEatsRoles.Customer;
            // The user id is derived from the name, so logging in again with the same name shows the same order history.
            var userId = $"{role}-{Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-')}";

            return TypedResults.Ok(new { accessToken = tokens.CreateToken(userId, name, role), userId, displayName = name, role });
        })
        .AllowAnonymous()
        .RequireRateLimiting(RateLimitPolicies.PerClient)
        .WithTags("Auth");
}

app.MapReverseProxy();

await app.RunAsync();

internal sealed record DemoLoginRequest(string? DisplayName, string? Role);

internal static class RateLimitPolicies
{
    public const string PerClient = "per-client";
}

public partial class Program;
