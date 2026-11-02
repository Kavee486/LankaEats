using Inventory.Api.Data;
using LankaEats.ServiceDefaults.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Inventory.Api.Features.Menu;

public sealed record MenuItemResponse(
    Guid Id, string Name, string Description, string Category, decimal Price, int AvailableQuantity, bool IsAvailable)
{
    public static MenuItemResponse From(MenuItem i) =>
        new(i.Id, i.Name, i.Description, i.Category, i.Price, i.AvailableQuantity, i.AvailableQuantity > 0);
}

public sealed record UpsertMenuItemRequest(string Name, string Description, string Category, decimal Price, int AvailableQuantity);

/// <summary>
/// The menu is read far more often than it changes, so it's a cache-aside read through
/// <see cref="HybridCache"/> (in-memory L1 + Redis L2, with stampede protection).
/// Writes invalidate the entry.
/// </summary>
public sealed class MenuCache(HybridCache cache, InventoryDbContext db)
{
    private const string Key = "inventory:menu:v1";

    private static readonly HybridCacheEntryOptions Options = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromSeconds(30),
    };

    public ValueTask<List<MenuItemResponse>> GetAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync(
            Key,
            async token => await db.MenuItems.AsNoTracking()
                .OrderBy(i => i.Category).ThenBy(i => i.Name)
                .Select(i => new MenuItemResponse(i.Id, i.Name, i.Description, i.Category, i.Price, i.AvailableQuantity, i.AvailableQuantity > 0))
                .ToListAsync(token),
            Options,
            cancellationToken: ct);

    public ValueTask InvalidateAsync(CancellationToken ct) => cache.RemoveAsync(Key, ct);
}

public static class MenuEndpoints
{
    public static IEndpointRouteBuilder MapMenuEndpoints(this IEndpointRouteBuilder app)
    {
        var menu = app.MapGroup("/api/menu").WithTags("Menu");

        menu.MapGet("/", async (MenuCache cache, CancellationToken ct) => TypedResults.Ok(await cache.GetAsync(ct)))
            .AllowAnonymous()
            .WithName("GetMenu");

        menu.MapGet("/{id:guid}", async Task<IResult> (Guid id, MenuCache cache, CancellationToken ct) =>
            {
                var item = (await cache.GetAsync(ct)).FirstOrDefault(i => i.Id == id);
                return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
            })
            .AllowAnonymous()
            .WithName("GetMenuItem");

        menu.MapPost("/", async Task<IResult> (UpsertMenuItemRequest request, InventoryDbContext db, MenuCache cache, CancellationToken ct) =>
            {
                if (Validate(request) is { } errors) return TypedResults.ValidationProblem(errors);

                var item = new MenuItem
                {
                    Id = Guid.CreateVersion7(),
                    Name = request.Name.Trim(),
                    Description = request.Description.Trim(),
                    Category = request.Category.Trim(),
                    Price = request.Price,
                    AvailableQuantity = request.AvailableQuantity,
                };
                db.MenuItems.Add(item);
                await db.SaveChangesAsync(ct);
                await cache.InvalidateAsync(ct);
                return TypedResults.Created($"/api/menu/{item.Id}", MenuItemResponse.From(item));
            })
            .RequireAuthorization(LankaEatsPolicies.Admin)
            .WithName("CreateMenuItem");

        menu.MapPut("/{id:guid}", async Task<IResult> (Guid id, UpsertMenuItemRequest request, InventoryDbContext db, MenuCache cache, CancellationToken ct) =>
            {
                if (Validate(request) is { } errors) return TypedResults.ValidationProblem(errors);

                var item = await db.MenuItems.FindAsync([id], ct);
                if (item is null) return TypedResults.NotFound();

                item.Name = request.Name.Trim();
                item.Description = request.Description.Trim();
                item.Category = request.Category.Trim();
                item.Price = request.Price;
                item.AvailableQuantity = request.AvailableQuantity;

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return TypedResults.Conflict("The item was modified concurrently (probably by an order). Please retry.");
                }

                await cache.InvalidateAsync(ct);
                return TypedResults.Ok(MenuItemResponse.From(item));
            })
            .RequireAuthorization(LankaEatsPolicies.Admin)
            .WithName("UpdateMenuItem");

        return app;
    }

    private static Dictionary<string, string[]>? Validate(UpsertMenuItemRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name)) errors["name"] = ["Name is required."];
        if (string.IsNullOrWhiteSpace(r.Category)) errors["category"] = ["Category is required."];
        if (r.Price <= 0) errors["price"] = ["Price must be positive."];
        if (r.AvailableQuantity < 0) errors["availableQuantity"] = ["Quantity cannot be negative."];
        return errors.Count == 0 ? null : errors;
    }
}
