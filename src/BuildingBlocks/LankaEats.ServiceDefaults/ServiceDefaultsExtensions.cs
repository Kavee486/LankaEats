using LankaEats.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

namespace LankaEats.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    private const string LiveTag = "live";

    /// <summary>
    /// Registers what every LankaEats service needs: OpenTelemetry, health checks, ProblemDetails,
    /// OpenAPI and JWT authentication.
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder, string serviceName)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();

        builder.AddLankaEatsTelemetry(serviceName);
        builder.AddLankaEatsAuthentication();

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        return builder;
    }

    public static IHostApplicationBuilder AddLankaEatsTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(o =>
                    o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                // MassTransit emits an Activity per publish/consume, so one trace spans every service.
                .AddSource("MassTransit")
                .AddSource("Yarp.ReverseProxy"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("MassTransit"));

        // Export only when a collector is configured (Aspire dashboard, Jaeger, Grafana Cloud, Azure Monitor...)
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        return builder;
    }

    public static IHostApplicationBuilder AddLankaEatsAuthentication(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<JwtOptions>()
            .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<JwtOptions>>().Value);

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtOptions>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = JwtTokenFactory.CreateKey(jwt),
                    NameClaimType = LankaEatsClaims.Name,
                    RoleClaimType = LankaEatsClaims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                bearer.Events = new JwtBearerEvents
                {
                    // Browsers cannot set headers on WebSocket requests, so SignalR sends the token in the query string.
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            ctx.Token = token;
                        }
                        return Task.CompletedTask;
                    },
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(LankaEatsPolicies.Admin, p => p.RequireRole(LankaEatsRoles.Admin));

        return builder;
    }

    /// <summary>Maps health probes, plus the OpenAPI document and Scalar UI in development.</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Liveness: is the process up? Readiness: are the dependencies (DB, broker) reachable?
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains(LiveTag) });
        app.MapHealthChecks("/health/ready");

        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("EnableApiDocs"))
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        return app;
    }

    public static string GetRequiredConnectionString(this IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"Connection string '{name}' is not configured.");
}
