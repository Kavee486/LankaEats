using System.Net.Http.Json;
using Orders.Application.Abstractions;

namespace Orders.Infrastructure.Menu;

/// <summary>
/// Synchronous call to the Inventory service for current prices. The HttpClient uses the standard
/// resilience pipeline (retry, circuit breaker, timeouts) and Inventory serves the menu from its
/// Redis-backed cache.
/// Trade-off: Orders cannot accept orders while Inventory is down. The alternative is a local
/// price read model kept up to date by MenuItemChanged events.
/// </summary>
internal sealed class MenuCatalogClient(HttpClient http) : IMenuCatalog
{
    public async Task<IReadOnlyDictionary<Guid, MenuItemPrice>> GetPricesAsync(IEnumerable<Guid> menuItemIds, CancellationToken ct)
    {
        var wanted = menuItemIds.ToHashSet();
        var menu = await http.GetFromJsonAsync<List<MenuItemResponse>>("/api/menu", ct) ?? [];

        return menu
            .Where(m => wanted.Contains(m.Id) && m.IsAvailable)
            .ToDictionary(m => m.Id, m => new MenuItemPrice(m.Id, m.Name, m.Price));
    }

    private sealed record MenuItemResponse(Guid Id, string Name, decimal Price, bool IsAvailable);
}
