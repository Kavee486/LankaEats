using System.Net.Http.Headers;
using LankaEats.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orders.Application.Abstractions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Orders.IntegrationTests;

/// <summary>
/// Runs the real Orders API against a real PostgreSQL and a real RabbitMQ in throwaway containers.
/// This covers EF mappings, migrations, the outbox, consumers and auth. Only the HTTP call to the
/// Inventory service is replaced with a fake.
/// </summary>
public sealed class OrdersApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid Kottu = Guid.Parse("0199a000-0000-7000-8000-000000000001");
    public static readonly Guid Watalappan = Guid.Parse("0199a000-0000-7000-8000-000000000007");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-alpine").Build();

    public FakeMenuCatalog Menu { get; } = new()
    {
        [Kottu] = new MenuItemPrice(Kottu, "Chicken Kottu", 1450m),
        [Watalappan] = new MenuItemPrice(Watalappan, "Watalappan", 550m),
    };

    public async Task InitializeAsync() => await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:orders", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:rabbitmq", _rabbit.GetConnectionString());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMenuCatalog>();
            services.AddSingleton<IMenuCatalog>(Menu);
        });
    }

    public HttpClient CreateClientFor(string userId, string role = LankaEatsRoles.Customer)
    {
        var client = CreateClient();
        var tokens = new JwtTokenFactory(Services.GetRequiredService<JwtOptions>(), TimeProvider.System);
        var token = tokens.CreateToken(userId, userId, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

public sealed class FakeMenuCatalog : Dictionary<Guid, MenuItemPrice>, IMenuCatalog
{
    public Task<IReadOnlyDictionary<Guid, MenuItemPrice>> GetPricesAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, MenuItemPrice>>(ids.Where(ContainsKey).ToDictionary(id => id, id => this[id]));
}
