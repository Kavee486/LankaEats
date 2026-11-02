using LankaEats.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Notifications.Api.Hubs;

namespace Notifications.Api.Consumers;

/// <summary>
/// Pushes order status changes to browsers in real time. With the Redis backplane, the push reaches
/// clients connected to any Notifications replica, not only the one that consumed the message.
/// </summary>
public sealed class OrderStatusChangedConsumer(
    IHubContext<OrderStatusHub, IOrderStatusClient> hub,
    ILogger<OrderStatusChangedConsumer> logger) : IConsumer<OrderStatusChanged>
{
    public async Task Consume(ConsumeContext<OrderStatusChanged> context)
    {
        var m = context.Message;
        var notification = new OrderStatusNotification(m.OrderId, m.CustomerId, m.Status, m.Reason, m.OccurredAt);

        await Task.WhenAll(
            hub.Clients.User(m.CustomerId).OrderStatusChanged(notification),
            hub.Clients.Group(OrderStatusHub.AdminsGroup).OrderStatusChanged(notification));

        // An SMS (e.g. via a Dialog / Mobitel gateway) or email channel would plug in here.
        logger.LogInformation("Notified customer {CustomerId}: order {OrderId} is {Status}", m.CustomerId, m.OrderId, m.Status);
    }
}
