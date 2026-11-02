using FluentValidation;
using LankaEats.Contracts;
using MediatR;
using Orders.Application.Abstractions;
using Orders.Domain;

namespace Orders.Application.Orders;

public sealed record PlaceOrderCommand(
    string CustomerId,
    string DeliveryAddress,
    string PaymentMethod,
    IReadOnlyList<PlaceOrderLine> Lines) : IRequest<OrderDto>;

public sealed record PlaceOrderLine(Guid MenuItemId, int Quantity);

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.DeliveryAddress).NotEmpty().MaximumLength(300);
        RuleFor(c => c.PaymentMethod)
            .Must(PaymentMethods.All.Contains)
            .WithMessage($"Payment method must be one of: {string.Join(", ", PaymentMethods.All)}.");
        RuleFor(c => c.Lines).NotEmpty().Must(l => l.Count <= 30).WithMessage("An order can contain at most 30 lines.");
        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.MenuItemId).NotEmpty();
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, Order.MaxQuantityPerLine);
        });
    }
}

internal sealed class PlaceOrderHandler(
    IMenuCatalog menu,
    IOrderRepository orders,
    IIntegrationEventPublisher events,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<PlaceOrderCommand, OrderDto>
{
    public async Task<OrderDto> Handle(PlaceOrderCommand command, CancellationToken ct)
    {
        // Prices come from the menu owner (Inventory), never from the client.
        var prices = await menu.GetPricesAsync(command.Lines.Select(l => l.MenuItemId).Distinct(), ct);
        var unknown = command.Lines.Where(l => !prices.ContainsKey(l.MenuItemId)).Select(l => l.MenuItemId).ToList();
        if (unknown.Count > 0) throw new UnknownMenuItemsException(unknown);

        var now = clock.GetUtcNow();
        var order = Order.Place(
            command.CustomerId,
            command.DeliveryAddress,
            command.PaymentMethod,
            command.Lines.Select(l =>
            {
                var item = prices[l.MenuItemId];
                return new OrderLineRequest(item.Id, item.Name, item.Price, l.Quantity);
            }),
            now);

        orders.Add(order);

        // The order row and the OrderSubmitted outbox row are saved in one transaction,
        // so the event is never lost and never sent for an order that doesn't exist.
        await events.PublishAsync(new OrderSubmitted(
            order.Id,
            order.CustomerId,
            order.Lines.Select(l => new OrderLineItem(l.MenuItemId, l.Name, l.Quantity, l.UnitPrice)).ToList(),
            order.Total,
            order.PaymentMethod,
            now), ct);
        await events.PublishAsync(order.ToStatusChanged(now), ct);

        await unitOfWork.SaveChangesAsync(ct);
        return OrderDto.From(order);
    }
}

public sealed class UnknownMenuItemsException(IReadOnlyList<Guid> ids)
    : Exception($"Unknown menu items: {string.Join(", ", ids)}")
{
    public IReadOnlyList<Guid> MenuItemIds { get; } = ids;
}

internal static class OrderEventMapping
{
    public static OrderStatusChanged ToStatusChanged(this Order order, DateTimeOffset now) =>
        new(order.Id, order.CustomerId, order.Status.ToString(), order.RejectionReason, now);
}
