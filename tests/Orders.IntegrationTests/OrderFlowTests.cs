using System.Net;
using System.Net.Http.Json;
using LankaEats.Contracts;
using LankaEats.ServiceDefaults.Auth;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Orders.Api;
using Orders.Application.Orders;

namespace Orders.IntegrationTests;

public sealed class OrderFlowTests(OrdersApiFactory factory) : IClassFixture<OrdersApiFactory>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private IBus Bus => factory.Services.GetRequiredService<IBus>();

    [Fact]
    public async Task Placing_an_order_saves_it_and_delivers_OrderSubmitted_through_the_outbox()
    {
        var received = new TaskCompletionSource<OrderSubmitted>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = factory.CreateClientFor("customer-outbox");

        // A temporary queue stands in for the Inventory service.
        var handle = Bus.ConnectReceiveEndpoint("test-inventory-order-submitted", e =>
            e.Handler<OrderSubmitted>(ctx =>
            {
                if (ctx.Message.CustomerId == "customer-outbox") received.TrySetResult(ctx.Message);
                return Task.CompletedTask;
            }));
        await handle.Ready;

        try
        {
            var order = await PlaceOrderAsync(client, PaymentMethods.Card, (OrdersApiFactory.Kottu, 2), (OrdersApiFactory.Watalappan, 1));

            Assert.Equal("Pending", order.Status);
            Assert.Equal(3450m, order.Total); // server-side prices: 2 × 1450 + 550

            var submitted = await received.Task.WaitAsync(Timeout);
            Assert.Equal(order.Id, submitted.OrderId);
            Assert.Equal(2, submitted.Lines.Count);
        }
        finally
        {
            await handle.StopAsync();
        }
    }

    [Fact]
    public async Task Order_is_confirmed_after_stock_is_reserved_and_payment_succeeds()
    {
        var client = factory.CreateClientFor("customer-happy");
        var order = await PlaceOrderAsync(client, PaymentMethods.Card, (OrdersApiFactory.Kottu, 1));

        await Bus.Publish(new StockReserved(order.Id, "customer-happy", order.Total, order.PaymentMethod));
        await WaitForStatusAsync(client, order.Id, "StockReserved");

        await Bus.Publish(new PaymentSucceeded(order.Id, "customer-happy", Guid.NewGuid(), order.Total));
        await WaitForStatusAsync(client, order.Id, "Confirmed");

        // The restaurant takes it from here.
        var admin = factory.CreateClientFor("admin-1", LankaEatsRoles.Admin);
        var advanced = await admin.PostAsync($"/api/orders/{order.Id}/advance", null);
        advanced.EnsureSuccessStatusCode();
        Assert.Equal("Preparing", (await advanced.Content.ReadFromJsonAsync<OrderDto>())!.Status);
    }

    [Fact]
    public async Task Order_is_rejected_with_a_reason_when_payment_fails()
    {
        var client = factory.CreateClientFor("customer-declined");
        var order = await PlaceOrderAsync(client, PaymentMethods.TestDeclinedCard, (OrdersApiFactory.Kottu, 1));

        await Bus.Publish(new StockReserved(order.Id, "customer-declined", order.Total, order.PaymentMethod));
        await Bus.Publish(new PaymentFailed(order.Id, "customer-declined", "Card declined"));

        var rejected = await WaitForStatusAsync(client, order.Id, "Rejected");
        Assert.Equal("Card declined", rejected.RejectionReason);
    }

    [Fact]
    public async Task Duplicate_events_do_not_change_a_confirmed_order()
    {
        var client = factory.CreateClientFor("customer-dupes");
        var order = await PlaceOrderAsync(client, PaymentMethods.Card, (OrdersApiFactory.Kottu, 1));

        await Bus.Publish(new PaymentSucceeded(order.Id, "customer-dupes", Guid.NewGuid(), order.Total));
        await WaitForStatusAsync(client, order.Id, "Confirmed");

        // Late or replayed events must not move the order backwards.
        await Bus.Publish(new StockReserved(order.Id, "customer-dupes", order.Total, order.PaymentMethod));
        await Bus.Publish(new PaymentFailed(order.Id, "customer-dupes", "late failure"));
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Equal("Confirmed", (await GetOrderAsync(client, order.Id))!.Status);
    }

    [Fact]
    public async Task Unknown_menu_items_return_400()
    {
        var client = factory.CreateClientFor("customer-unknown");
        var response = await client.PostAsJsonAsync("/api/orders",
            new PlaceOrderRequest("Kandy", PaymentMethods.Card, [new PlaceOrderItem(Guid.NewGuid(), 1)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_payment_method_returns_validation_problem()
    {
        var client = factory.CreateClientFor("customer-invalid");
        var response = await client.PostAsJsonAsync("/api/orders",
            new PlaceOrderRequest("Kandy", "bitcoin", [new PlaceOrderItem(OrdersApiFactory.Kottu, 1)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PaymentMethod", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Requests_without_a_token_are_rejected()
    {
        var response = await factory.CreateClient().GetAsync("/api/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Customers_cannot_see_other_customers_orders_or_advance_orders()
    {
        var owner = factory.CreateClientFor("customer-owner");
        var order = await PlaceOrderAsync(owner, PaymentMethods.CashOnDelivery, (OrdersApiFactory.Kottu, 1));
        var stranger = factory.CreateClientFor("customer-stranger");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"/api/orders/{order.Id}/advance", null)).StatusCode);
    }

    private static async Task<OrderDto> PlaceOrderAsync(HttpClient client, string paymentMethod, params (Guid Id, int Qty)[] items)
    {
        var response = await client.PostAsJsonAsync("/api/orders", new PlaceOrderRequest(
            "42 Galle Road, Colombo 03", paymentMethod, items.Select(i => new PlaceOrderItem(i.Id, i.Qty)).ToList()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    private static async Task<OrderDto?> GetOrderAsync(HttpClient client, Guid id) =>
        await client.GetFromJsonAsync<OrderDto>($"/api/orders/{id}");

    private static async Task<OrderDto> WaitForStatusAsync(HttpClient client, Guid id, string status)
    {
        var deadline = DateTime.UtcNow + Timeout;
        OrderDto? order = null;
        while (DateTime.UtcNow < deadline)
        {
            order = await GetOrderAsync(client, id);
            if (order?.Status == status) return order;
            await Task.Delay(250);
        }
        throw new TimeoutException($"Order {id} did not reach '{status}' (last status: {order?.Status}).");
    }
}
