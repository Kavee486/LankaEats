using LankaEats.ServiceDefaults;
using LankaEats.ServiceDefaults.Data;
using LankaEats.ServiceDefaults.Messaging;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orders.Application.Abstractions;
using Orders.Infrastructure.Menu;
using Orders.Infrastructure.Messaging;
using Orders.Infrastructure.Persistence;

namespace Orders.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddOrdersInfrastructure(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddDbContext<OrdersDbContext>(o =>
            o.UseLankaEatsNpgsql(builder.Configuration.GetRequiredConnectionString("orders")));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrdersDbContext>());
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IIntegrationEventPublisher, MassTransitEventPublisher>();
        services.AddHealthChecks().AddDbContextCheck<OrdersDbContext>("orders-db");

        services.AddHttpClient<IMenuCatalog, MenuCatalogClient>(client =>
                client.BaseAddress = new Uri(builder.Configuration["Services:Inventory"]
                    ?? throw new InvalidOperationException("Services:Inventory is not configured.")))
            .AddStandardResilienceHandler();

        builder.AddLankaEatsMessaging<OrdersDbContext>(x =>
        {
            // Explicit queue names: stable, readable, and easy to reference from KEDA scale rules.
            x.AddConsumer<StockReservedConsumer>().Endpoint(e => e.Name = "orders-stock-reserved");
            x.AddConsumer<StockReservationFailedConsumer>().Endpoint(e => e.Name = "orders-stock-reservation-failed");
            x.AddConsumer<PaymentSucceededConsumer>().Endpoint(e => e.Name = "orders-payment-succeeded");
            x.AddConsumer<PaymentFailedConsumer>().Endpoint(e => e.Name = "orders-payment-failed");
        });

        return builder;
    }
}
