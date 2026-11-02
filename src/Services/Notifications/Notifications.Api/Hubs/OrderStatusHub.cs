using LankaEats.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Notifications.Api.Hubs;

/// <summary>Methods the server can call on connected clients (strongly typed SignalR).</summary>
public interface IOrderStatusClient
{
    Task OrderStatusChanged(OrderStatusNotification notification);
}

public sealed record OrderStatusNotification(Guid OrderId, string CustomerId, string Status, string? Reason, DateTimeOffset OccurredAt);

/// <summary>
/// Customers receive updates for their own orders (addressed by user id). Admins also join the
/// "admins" group, so the restaurant dashboard sees every order.
/// </summary>
[Authorize]
public sealed class OrderStatusHub : Hub<IOrderStatusClient>
{
    public const string AdminsGroup = "admins";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsAdmin() == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }
        await base.OnConnectedAsync();
    }
}

/// <summary>Uses the JWT <c>sub</c> claim as the SignalR user id, so we can call <c>Clients.User(customerId)</c>.</summary>
public sealed class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(LankaEatsClaims.Subject)?.Value;
}
