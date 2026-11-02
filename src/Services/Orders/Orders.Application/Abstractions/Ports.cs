using Orders.Domain;

namespace Orders.Application.Abstractions;

// Ports: the application layer defines what it needs, and Orders.Infrastructure implements it.

public interface IOrderRepository
{
    void Add(Order order);
    Task<Order?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Order>> ListForCustomerAsync(string customerId, int take, CancellationToken ct);
    Task<IReadOnlyList<Order>> ListRecentAsync(int take, CancellationToken ct);
}

public interface IUnitOfWork
{
    /// <summary>Commits the aggregate changes and any outbox messages in one database transaction.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Publishes integration events. Implemented over the transactional outbox.</summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync<T>(T message, CancellationToken ct) where T : class;
}

/// <summary>Read access to the restaurant menu that the Inventory service owns.</summary>
public interface IMenuCatalog
{
    Task<IReadOnlyDictionary<Guid, MenuItemPrice>> GetPricesAsync(IEnumerable<Guid> menuItemIds, CancellationToken ct);
}

public sealed record MenuItemPrice(Guid Id, string Name, decimal Price);
