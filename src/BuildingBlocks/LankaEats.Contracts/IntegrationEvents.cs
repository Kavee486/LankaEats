namespace LankaEats.Contracts;

// Integration events are the public contract between services. Treat them as
// append-only: add optional fields, never rename or remove existing ones.

public sealed record OrderLineItem(Guid MenuItemId, string Name, int Quantity, decimal UnitPrice);

/// <summary>Published by Orders when a customer places an order.</summary>
public sealed record OrderSubmitted(
    Guid OrderId,
    string CustomerId,
    IReadOnlyList<OrderLineItem> Lines,
    decimal Total,
    string PaymentMethod,
    DateTimeOffset SubmittedAt);

/// <summary>Published by Inventory once every line of an order has been reserved.</summary>
public sealed record StockReserved(Guid OrderId, string CustomerId, decimal Total, string PaymentMethod);

/// <summary>Published by Inventory when an order cannot be fulfilled.</summary>
public sealed record StockReservationFailed(Guid OrderId, string CustomerId, string Reason);

/// <summary>Published by Payments when the charge is authorised.</summary>
public sealed record PaymentSucceeded(Guid OrderId, string CustomerId, Guid PaymentId, decimal Amount);

/// <summary>Published by Payments when the charge is declined. Inventory releases the stock.</summary>
public sealed record PaymentFailed(Guid OrderId, string CustomerId, string Reason);

/// <summary>Published by Orders on every status transition. Notifications pushes it to clients.</summary>
public sealed record OrderStatusChanged(
    Guid OrderId,
    string CustomerId,
    string Status,
    string? Reason,
    DateTimeOffset OccurredAt);

public static class PaymentMethods
{
    public const string Card = "card";
    public const string CashOnDelivery = "cash_on_delivery";

    /// <summary>Test card that is always declined, used to demo the compensation path.</summary>
    public const string TestDeclinedCard = "test_declined_card";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Card, CashOnDelivery, TestDeclinedCard };
}
