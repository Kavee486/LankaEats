using Orders.Domain;

namespace Orders.Application.Orders;

public sealed record OrderDto(
    Guid Id,
    string CustomerId,
    string Status,
    decimal Total,
    string PaymentMethod,
    string DeliveryAddress,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderLineDto> Lines)
{
    public static OrderDto From(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Status.ToString(),
        order.Total,
        order.PaymentMethod,
        order.DeliveryAddress,
        order.RejectionReason,
        order.CreatedAt,
        order.UpdatedAt,
        order.Lines.Select(l => new OrderLineDto(l.MenuItemId, l.Name, l.UnitPrice, l.Quantity, l.LineTotal)).ToList());
}

public sealed record OrderLineDto(Guid MenuItemId, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);
