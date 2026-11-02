namespace Inventory.Api.Data;

public sealed class MenuItem
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Category { get; set; }
    public decimal Price { get; set; }
    public int AvailableQuantity { get; set; }

    /// <summary>Maps to PostgreSQL's <c>xmin</c> system column for optimistic concurrency.</summary>
    public uint Version { get; set; }
}

public enum ReservationStatus
{
    Reserved,
    Committed,
    Released,
    Rejected,
}

/// <summary>
/// One row per order. It makes reservation idempotent and records what to put back if payment fails.
/// </summary>
public sealed class StockReservation
{
    public Guid OrderId { get; set; }
    public ReservationStatus Status { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ReservedLine> Lines { get; set; } = [];
}

public sealed class ReservedLine
{
    public Guid MenuItemId { get; set; }
    public int Quantity { get; set; }
}
