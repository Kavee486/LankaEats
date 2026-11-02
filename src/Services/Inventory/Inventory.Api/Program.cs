using Inventory.Api.Data;
using Inventory.Api.Features.Menu;
using Inventory.Api.Features.Reservations;
using LankaEats.ServiceDefaults;
using LankaEats.ServiceDefaults.Data;
using LankaEats.ServiceDefaults.Messaging;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("inventory");

builder.Services.AddDbContext<InventoryDbContext>(o =>
    o.UseLankaEatsNpgsql(builder.Configuration.GetRequiredConnectionString("inventory")));
builder.Services.AddHealthChecks().AddDbContextCheck<InventoryDbContext>("inventory-db");

// HybridCache uses Redis as its L2 cache automatically once IDistributedCache is registered.
builder.Services.AddStackExchangeRedisCache(o =>
{
    o.Configuration = builder.Configuration.GetRequiredConnectionString("redis");
    o.InstanceName = "lankaeats:";
});
builder.Services.AddHybridCache();
builder.Services.AddScoped<MenuCache>();

builder.AddLankaEatsMessaging<InventoryDbContext>(x =>
{
    x.AddConsumer<ReserveStockConsumer>().Endpoint(e => e.Name = "inventory-order-submitted");
    x.AddConsumer<ReleaseStockConsumer>().Endpoint(e => e.Name = "inventory-payment-failed");
    x.AddConsumer<CommitStockConsumer>().Endpoint(e => e.Name = "inventory-payment-succeeded");
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapMenuEndpoints();

await app.MigrateDatabaseAsync<InventoryDbContext>();
await app.RunAsync();

public partial class Program;
