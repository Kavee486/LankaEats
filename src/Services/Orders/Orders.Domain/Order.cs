namespace Orders.Domain;

public enum OrderStatus
{
    Pending,
    StockReserved,
    Confirmed,
    Preparing,
    OutForDelivery,
    Delivered,
    Rejected,
}

/// <summary>
/// The order aggregate. All state changes go through its methods, so invariants are enforced in one place.
/// Lifecycle (driven by integration events, then by the restaurant):
/// <c>Pending → StockReserved → Confirmed → Preparing → OutForDelivery → Delivered</c>,
/// with <c>Rejected</c> reachable before confirmation.
/// </summary>
public sealed class Order
{
    public const int MaxQuantityPerLine = 20;

    private readonly List<OrderLine> _lines = [];

    private Order() { } // EF Core

    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public string DeliveryAddress { get; private set; } = string.Empty;
    public string PaymentMethod { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public decimal Total { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public static Order Place(
        string customerId,
        string deliveryAddress,
        string paymentMethod,
        IEnumerable<OrderLineRequest> lines,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(customerId)) throw new DomainException("Customer is required.");
        if (string.IsNullOrWhiteSpace(deliveryAddress)) throw new DomainException("Delivery address is required.");

        var order = new Order
        {
            Id = Guid.CreateVersion7(now),
            CustomerId = customerId,
            DeliveryAddress = deliveryAddress.Trim(),
            PaymentMethod = paymentMethod,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Merge duplicate menu items into a single line.
        foreach (var group in lines.GroupBy(l => l.MenuItemId))
        {
            var first = group.First();
            var quantity = group.Sum(l => l.Quantity);
            if (quantity is <= 0 or > MaxQuantityPerLine)
                throw new DomainException($"Quantity for '{first.Name}' must be between 1 and {MaxQuantityPerLine}.");
            if (first.UnitPrice <= 0)
                throw new DomainException($"'{first.Name}' has an invalid price.");

            order._lines.Add(new OrderLine(first.MenuItemId, first.Name, first.UnitPrice, quantity));
        }

        if (order._lines.Count == 0) throw new DomainException("An order needs at least one item.");

        order.Total = order._lines.Sum(l => l.LineTotal);
        return order;
    }

    /// <summary>Inventory has reserved the stock. Returns false if this is a duplicate or arrives late (no-op).</summary>
    public bool MarkStockReserved(DateTimeOffset now)
    {
        // Payments may confirm before Orders handles StockReserved (both consume the same event), so
        // events can arrive out of order. Ignore it if the order has already moved past this point.
        if (Status != OrderStatus.Pending) return false;
        return TransitionTo(OrderStatus.StockReserved, now);
    }

    /// <summary>Payment succeeded. Accepted from Pending too, because events can arrive out of order.</summary>
    public bool Confirm(DateTimeOffset now)
    {
        if (Status is OrderStatus.Pending or OrderStatus.StockReserved)
            return TransitionTo(OrderStatus.Confirmed, now);
        if (Status == OrderStatus.Rejected)
            throw new DomainException("A rejected order cannot be confirmed.");
        return false; // already confirmed or further along
    }

    public bool Reject(string reason, DateTimeOffset now)
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.StockReserved)) return false;
        RejectionReason = reason;
        return TransitionTo(OrderStatus.Rejected, now);
    }

    /// <summary>Restaurant-side progress: Confirmed → Preparing → OutForDelivery → Delivered.</summary>
    public void AdvanceFulfilment(DateTimeOffset now)
    {
        var next = Status switch
        {
            OrderStatus.Confirmed => OrderStatus.Preparing,
            OrderStatus.Preparing => OrderStatus.OutForDelivery,
            OrderStatus.OutForDelivery => OrderStatus.Delivered,
            _ => throw new DomainException($"An order in status '{Status}' cannot be advanced."),
        };
        TransitionTo(next, now);
    }

    public bool IsOwnedBy(string customerId) => CustomerId == customerId;

    private bool TransitionTo(OrderStatus next, DateTimeOffset now)
    {
        Status = next;
        UpdatedAt = now;
        return true;
    }
}

public sealed class OrderLine
{
    private OrderLine() { } // EF Core

    internal OrderLine(Guid menuItemId, string name, decimal unitPrice, int quantity)
    {
        MenuItemId = menuItemId;
        Name = name;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public Guid MenuItemId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public sealed record OrderLineRequest(Guid MenuItemId, string Name, decimal UnitPrice, int Quantity);

public sealed class DomainException(string message) : Exception(message);
