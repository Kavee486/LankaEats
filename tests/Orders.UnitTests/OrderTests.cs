using Orders.Domain;

namespace Orders.UnitTests;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly Guid Kottu = Guid.NewGuid();
    private static readonly Guid Watalappan = Guid.NewGuid();

    private static Order NewOrder() => Order.Place(
        "customer-nimal", "42 Galle Road, Colombo 03", "card",
        [new(Kottu, "Chicken Kottu", 1450m, 2), new(Watalappan, "Watalappan", 550m, 1)],
        Now);

    [Fact]
    public void Place_calculates_total_and_starts_pending()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(3450m, order.Total);
        Assert.Equal(2, order.Lines.Count);
    }

    [Fact]
    public void Place_merges_duplicate_menu_items()
    {
        var order = Order.Place("c", "addr", "card",
            [new(Kottu, "Chicken Kottu", 1450m, 1), new(Kottu, "Chicken Kottu", 1450m, 2)], Now);

        var line = Assert.Single(order.Lines);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(4350m, order.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Order.MaxQuantityPerLine + 1)]
    public void Place_rejects_out_of_range_quantities(int quantity)
    {
        Assert.Throws<DomainException>(() =>
            Order.Place("c", "addr", "card", [new(Kottu, "Chicken Kottu", 1450m, quantity)], Now));
    }

    [Fact]
    public void Place_requires_at_least_one_line() =>
        Assert.Throws<DomainException>(() => Order.Place("c", "addr", "card", [], Now));

    [Fact]
    public void Happy_path_moves_through_every_status()
    {
        var order = NewOrder();

        Assert.True(order.MarkStockReserved(Now));
        Assert.True(order.Confirm(Now));
        order.AdvanceFulfilment(Now);
        Assert.Equal(OrderStatus.Preparing, order.Status);
        order.AdvanceFulfilment(Now);
        Assert.Equal(OrderStatus.OutForDelivery, order.Status);
        order.AdvanceFulfilment(Now);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Throws<DomainException>(() => order.AdvanceFulfilment(Now));
    }

    [Fact]
    public void Payment_confirmation_arriving_before_stock_reserved_is_accepted()
    {
        // Payments and Orders both consume StockReserved, so PaymentSucceeded can win the race.
        var order = NewOrder();

        Assert.True(order.Confirm(Now));
        Assert.False(order.MarkStockReserved(Now)); // late event is a no-op
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Duplicate_events_are_no_ops()
    {
        var order = NewOrder();
        order.MarkStockReserved(Now);
        order.Confirm(Now);

        Assert.False(order.Confirm(Now));
        Assert.False(order.Reject("late failure", Now));
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Reject_records_reason_and_blocks_confirmation()
    {
        var order = NewOrder();

        Assert.True(order.Reject("Sold out: Lamprais", Now));
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal("Sold out: Lamprais", order.RejectionReason);
        Assert.Throws<DomainException>(() => order.Confirm(Now));
    }

    [Fact]
    public void Cannot_advance_before_confirmation() =>
        Assert.Throws<DomainException>(() => NewOrder().AdvanceFulfilment(Now));
}
