using LankaEats.ServiceDefaults;
using LankaEats.ServiceDefaults.Messaging;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Notifications.Api.Consumers;
using Notifications.Api.Hubs;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("notifications");

builder.Services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();
builder.Services.AddSignalR()
    .AddStackExchangeRedis(builder.Configuration.GetRequiredConnectionString("redis"), o =>
        o.Configuration.ChannelPrefix = RedisChannel.Literal("lankaeats-signalr"));

// Stateless service: no database, so no outbox. A dropped push is acceptable, because clients
// re-fetch order state when they reconnect.
builder.AddLankaEatsMessaging(x =>
    x.AddConsumer<OrderStatusChangedConsumer>().Endpoint(e => e.Name = "notifications-order-status-changed"));

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapHub<OrderStatusHub>("/hubs/orders");

await app.RunAsync();

public partial class Program;
