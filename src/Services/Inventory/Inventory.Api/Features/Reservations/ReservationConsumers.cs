using Inventory.Api.Data;
using Inventory.Api.Features.Menu;
using LankaEats.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Features.Reservations;

/// <summary>
/// Reserves stock for every line of the order, all or nothing.
/// Concurrent orders for the same dish are serialised by optimistic concurrency on <c>xmin</c>.
/// A conflict throws <see cref="DbUpdateConcurrencyException"/> and MassTransit's retry
/// policy runs the consumer again with fresh data.
/// </summary>
public sealed class ReserveStockConsumer(
    InventoryDbContext db,
    MenuCache menuCache,
    TimeProvider clock,
    ILogger<ReserveStockConsumer> logger) : IConsumer<OrderSubmitted>
{
    public async Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        var order = context.Message;
        var ct = context.CancellationToken;

        // Business-level idempotency, in addition to the MassTransit inbox.
        if (await db.Reservations.AnyAsync(r => r.OrderId == order.OrderId, ct)) return;

        var ids = order.Lines.Select(l => l.MenuItemId).ToList();
        var items = await db.MenuItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var now = clock.GetUtcNow();

        var shortages = order.Lines
            .Where(l => !items.TryGetValue(l.MenuItemId, out var item) || item.AvailableQuantity < l.Quantity)
            .Select(l => l.Name)
            .ToList();

        var reservation = new StockReservation
        {
            OrderId = order.OrderId,
            CreatedAt = now,
            UpdatedAt = now,
            Lines = order.Lines.Select(l => new ReservedLine { MenuItemId = l.MenuItemId, Quantity = l.Quantity }).ToList(),
        };

        if (shortages.Count > 0)
        {
            reservation.Status = ReservationStatus.Rejected;
            reservation.Reason = $"Sold out: {string.Join(", ", shortages)}";
            db.Reservations.Add(reservation);
            await context.Publish(new StockReservationFailed(order.OrderId, order.CustomerId, reservation.Reason), ct);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Rejected order {OrderId}: {Reason}", order.OrderId, reservation.Reason);
            return;
        }

        foreach (var line in order.Lines)
        {
            items[line.MenuItemId].AvailableQuantity -= line.Quantity;
        }

        reservation.Status = ReservationStatus.Reserved;
        db.Reservations.Add(reservation);
        await context.Publish(new StockReserved(order.OrderId, order.CustomerId, order.Total, order.PaymentMethod), ct);
        await db.SaveChangesAsync(ct);
        // The cached menu is only used for display. Reservations always read the database,
        // so a briefly stale quantity in the cache can never oversell.
        await menuCache.InvalidateAsync(ct);

        logger.LogInformation("Reserved stock for order {OrderId}", order.OrderId);
    }
}

/// <summary>Compensating action: payment failed, so put the reserved stock back.</summary>
public sealed class ReleaseStockConsumer(
    InventoryDbContext db,
    MenuCache menuCache,
    TimeProvider clock,
    ILogger<ReleaseStockConsumer> logger) : IConsumer<PaymentFailed>
{
    public async Task Consume(ConsumeContext<PaymentFailed> context)
    {
        var ct = context.CancellationToken;
        var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.OrderId == context.Message.OrderId, ct);
        if (reservation is not { Status: ReservationStatus.Reserved }) return;

        var ids = reservation.Lines.Select(l => l.MenuItemId).ToList();
        var items = await db.MenuItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        foreach (var line in reservation.Lines)
        {
            if (items.TryGetValue(line.MenuItemId, out var item)) item.AvailableQuantity += line.Quantity;
        }

        reservation.Status = ReservationStatus.Released;
        reservation.Reason = context.Message.Reason;
        reservation.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await menuCache.InvalidateAsync(ct);

        logger.LogInformation("Released stock for order {OrderId} after failed payment", reservation.OrderId);
    }
}

public sealed class CommitStockConsumer(InventoryDbContext db, TimeProvider clock) : IConsumer<PaymentSucceeded>
{
    public async Task Consume(ConsumeContext<PaymentSucceeded> context)
    {
        var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.OrderId == context.Message.OrderId, context.CancellationToken);
        if (reservation is not { Status: ReservationStatus.Reserved }) return;

        reservation.Status = ReservationStatus.Committed;
        reservation.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
