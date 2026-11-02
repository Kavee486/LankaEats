using Microsoft.EntityFrameworkCore;
using Orders.Application.Abstractions;
using Orders.Domain;

namespace Orders.Infrastructure.Persistence;

internal sealed class OrderRepository(OrdersDbContext db) : IOrderRepository
{
    public void Add(Order order) => db.Orders.Add(order);

    public Task<Order?> GetAsync(Guid id, CancellationToken ct) =>
        db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<Order>> ListForCustomerAsync(string customerId, int take, CancellationToken ct) =>
        await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> ListRecentAsync(int take, CancellationToken ct) =>
        await db.Orders.AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
}
