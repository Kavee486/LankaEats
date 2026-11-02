using LankaEats.ServiceDefaults;
using LankaEats.ServiceDefaults.Data;
using Orders.Api;
using Orders.Application;
using Orders.Infrastructure;
using Orders.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("orders");
builder.Services.AddOrdersApplication();
builder.AddOrdersInfrastructure();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapOrderEndpoints();

await app.MigrateDatabaseAsync<OrdersDbContext>();
await app.RunAsync();

public partial class Program;
