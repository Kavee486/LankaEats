using System.Security.Claims;
using LankaEats.ServiceDefaults.Auth;
using MediatR;
using Orders.Application.Orders;

namespace Orders.Api;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Orders").RequireAuthorization();

        orders.MapPost("/", async (PlaceOrderRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct) =>
            {
                var order = await sender.Send(new PlaceOrderCommand(
                    user.GetUserId(),
                    request.DeliveryAddress,
                    request.PaymentMethod,
                    request.Items.Select(i => new PlaceOrderLine(i.MenuItemId, i.Quantity)).ToList()), ct);

                return TypedResults.Created($"/api/orders/{order.Id}", order);
            })
            .WithName("PlaceOrder")
            .WithSummary("Place an order. The order starts as Pending and moves on as events arrive.");

        orders.MapGet("/{id:guid}", async Task<IResult> (Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct) =>
            {
                var order = await sender.Send(new GetOrderQuery(id, user.GetUserId(), user.IsAdmin()), ct);
                return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
            })
            .WithName("GetOrder");

        orders.MapGet("/", async (ClaimsPrincipal user, ISender sender, CancellationToken ct, int take = 50) =>
                TypedResults.Ok(await sender.Send(new ListOrdersQuery(user.GetUserId(), user.IsAdmin(), take), ct)))
            .WithName("ListOrders")
            .WithSummary("Customers see their own orders; admins see recent orders from all customers.");

        orders.MapPost("/{id:guid}/advance", async Task<IResult> (Guid id, ISender sender, CancellationToken ct) =>
            {
                var order = await sender.Send(new AdvanceOrderCommand(id), ct);
                return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
            })
            .RequireAuthorization(LankaEatsPolicies.Admin)
            .WithName("AdvanceOrder")
            .WithSummary("Restaurant moves a confirmed order along: Preparing → OutForDelivery → Delivered.");

        return app;
    }
}

public sealed record PlaceOrderRequest(string DeliveryAddress, string PaymentMethod, IReadOnlyList<PlaceOrderItem> Items);

public sealed record PlaceOrderItem(Guid MenuItemId, int Quantity);
