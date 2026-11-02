using LankaEats.Contracts;
using Orders.Application.Abstractions;
using Orders.Application.Orders;
using Orders.Domain;

namespace Orders.UnitTests;

public class PlaceOrderHandlerTests
{
    private static readonly Guid Kottu = Guid.NewGuid();
    private readonly FakeMenu _menu = new() { [Kottu] = new MenuItemPrice(Kottu, "Chicken Kottu", 1450m) };
    private readonly FakeRepository _orders = new();
    private readonly FakePublisher _events = new();
    private readonly FakeUnitOfWork _uow;

    public PlaceOrderHandlerTests() => _uow = new FakeUnitOfWork(_events);

    private PlaceOrderHandler CreateHandler() => new(_menu, _orders, _events, _uow, TimeProvider.System);

    [Fact]
    public async Task Uses_server_side_prices_and_publishes_OrderSubmitted_before_saving()
    {
        var command = new PlaceOrderCommand("customer-1", "Colombo", PaymentMethods.Card, [new(Kottu, 2)]);

        var dto = await CreateHandler().Handle(command, CancellationToken.None);

        Assert.Equal(2900m, dto.Total);
        Assert.Single(_orders.Added);
        var submitted = Assert.Single(_events.Published.OfType<OrderSubmitted>());
        Assert.Equal(dto.Id, submitted.OrderId);
        Assert.Equal(2900m, submitted.Total);
        Assert.Contains(_events.Published, e => e is OrderStatusChanged { Status: "Pending" });
        Assert.Equal(1, _uow.SaveCount);
        // Events must be staged before the commit, so they share the order's outbox transaction.
        Assert.Equal(_events.Published.Count, _uow.PublishedCountAtSave);
    }

    [Fact]
    public async Task Unknown_menu_items_are_rejected_without_saving()
    {
        var command = new PlaceOrderCommand("customer-1", "Colombo", PaymentMethods.Card, [new(Guid.NewGuid(), 1)]);

        await Assert.ThrowsAsync<UnknownMenuItemsException>(() => CreateHandler().Handle(command, CancellationToken.None));
        Assert.Empty(_events.Published);
        Assert.Equal(0, _uow.SaveCount);
    }

    [Theory]
    [InlineData("", "card", 1)]
    [InlineData("Colombo", "bitcoin", 1)]
    [InlineData("Colombo", "card", 0)]
    public void Validator_rejects_bad_input(string address, string paymentMethod, int quantity)
    {
        var result = new PlaceOrderValidator().Validate(
            new PlaceOrderCommand("customer-1", address, paymentMethod, [new(Kottu, quantity)]));

        Assert.False(result.IsValid);
    }

    private sealed class FakeMenu : Dictionary<Guid, MenuItemPrice>, IMenuCatalog
    {
        public Task<IReadOnlyDictionary<Guid, MenuItemPrice>> GetPricesAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, MenuItemPrice>>(
                ids.Where(ContainsKey).ToDictionary(id => id, id => this[id]));
    }

    private sealed class FakeRepository : IOrderRepository
    {
        public List<Order> Added { get; } = [];
        public void Add(Order order) => Added.Add(order);
        public Task<Order?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(Added.FirstOrDefault(o => o.Id == id));
        public Task<IReadOnlyList<Order>> ListForCustomerAsync(string c, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<Order>>(Added);
        public Task<IReadOnlyList<Order>> ListRecentAsync(int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<Order>>(Added);
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];
        public Task PublishAsync<T>(T message, CancellationToken ct) where T : class
        {
            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork(FakePublisher publisher) : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public int PublishedCountAtSave { get; private set; } = -1;
        public Task SaveChangesAsync(CancellationToken ct)
        {
            SaveCount++;
            PublishedCountAtSave = publisher.Published.Count;
            return Task.CompletedTask;
        }
    }
}
