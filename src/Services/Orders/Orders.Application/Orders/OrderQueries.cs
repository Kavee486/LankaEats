using MediatR;
using Orders.Application.Abstractions;

namespace Orders.Application.Orders;

/// <summary>Returns null when the order doesn't exist or the requester isn't allowed to see it.</summary>
public sealed record GetOrderQuery(Guid OrderId, string RequesterId, bool RequesterIsAdmin) : IRequest<OrderDto?>;

/// <summary>Customers see their own orders. Admins see the most recent orders across all customers.</summary>
public sealed record ListOrdersQuery(string RequesterId, bool RequesterIsAdmin, int Take = 50) : IRequest<IReadOnlyList<OrderDto>>;

internal sealed class OrderQueryHandlers(IOrderRepository orders) :
    IRequestHandler<GetOrderQuery, OrderDto?>,
    IRequestHandler<ListOrdersQuery, IReadOnlyList<OrderDto>>
{
    public async Task<OrderDto?> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var order = await orders.GetAsync(query.OrderId, ct);
        if (order is null) return null;

        // Return "not found" rather than "forbidden" so other customers' order ids aren't revealed.
        return query.RequesterIsAdmin || order.IsOwnedBy(query.RequesterId) ? OrderDto.From(order) : null;
    }

    public async Task<IReadOnlyList<OrderDto>> Handle(ListOrdersQuery query, CancellationToken ct)
    {
        var take = Math.Clamp(query.Take, 1, 200);
        var result = query.RequesterIsAdmin
            ? await orders.ListRecentAsync(take, ct)
            : await orders.ListForCustomerAsync(query.RequesterId, take, ct);
        return result.Select(OrderDto.From).ToList();
    }
}
