using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LankaEats.ServiceDefaults.Messaging;

public static class MessagingExtensions
{
    public const string RabbitMqConnectionName = "rabbitmq";

    /// <summary>
    /// Configures MassTransit on RabbitMQ with the transactional outbox and inbox.
    /// <list type="bullet">
    /// <item>Bus outbox: messages published from an API request are saved to the service's own
    /// database in the same transaction as the business data, then delivered by a background
    /// service. This prevents the dual-write problem.</item>
    /// <item>Consumer outbox/inbox: each consumed message id is recorded, so redeliveries are ignored
    /// (idempotency), and messages published while consuming are only sent once the transaction commits.</item>
    /// </list>
    /// </summary>
    public static IHostApplicationBuilder AddLankaEatsMessaging<TDbContext>(
        this IHostApplicationBuilder builder,
        Action<IBusRegistrationConfigurator> configureConsumers)
        where TDbContext : DbContext
    {
        builder.Services.AddMassTransit(x =>
        {
            configureConsumers(x);

            x.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(1);
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            });

            x.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                // Retry transient failures (e.g. optimistic concurrency conflicts) before the message goes to the _error queue.
                endpoint.UseMessageRetry(r => r.Intervals(200, 1_000, 3_000, 5_000));
                endpoint.UseEntityFrameworkOutbox<TDbContext>(context);
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                ConfigureHost(cfg, builder.Configuration);
                cfg.ConfigureEndpoints(context);
            });
        });

        return builder;
    }

    /// <summary>Configures MassTransit for a stateless service that has no database, so there is no outbox.</summary>
    public static IHostApplicationBuilder AddLankaEatsMessaging(
        this IHostApplicationBuilder builder,
        Action<IBusRegistrationConfigurator> configureConsumers)
    {
        builder.Services.AddMassTransit(x =>
        {
            configureConsumers(x);

            x.AddConfigureEndpointsCallback((_, _, endpoint) =>
                endpoint.UseMessageRetry(r => r.Intervals(200, 1_000, 3_000)));

            x.UsingRabbitMq((context, cfg) =>
            {
                ConfigureHost(cfg, builder.Configuration);
                cfg.ConfigureEndpoints(context);
            });
        });

        return builder;
    }

    /// <summary>
    /// Accepts a URI connection string (e.g. <c>amqp://guest:guest@localhost:5672</c> or a CloudAMQP
    /// <c>amqps://user:pass@host/vhost</c> URL).
    /// </summary>
    private static void ConfigureHost(IRabbitMqBusFactoryConfigurator cfg, IConfiguration configuration)
    {
        var uri = new Uri(configuration.GetRequiredConnectionString(RabbitMqConnectionName));
        var userInfo = uri.UserInfo.Split(':', 2);

        cfg.Host(uri, h =>
        {
            if (!string.IsNullOrEmpty(userInfo[0]))
            {
                h.Username(Uri.UnescapeDataString(userInfo[0]));
                h.Password(userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty);
            }
        });
    }
}
