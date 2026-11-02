using MediatR;
using Orders.Application.Abstractions;
using Orders.Domain;

namespace Orders.Application.Orders;

// Commands that move an order through its lifecycle. The saga commands are sent by message
// consumers. AdvanceOrder is sent by the restaurant admin API.

public sealed record MarkStockReservedCommand(Guid OrderId) : IRequest;

public sealed record ConfirmPaymentCommand(Guid OrderId) : IRequest;

public sealed record RejectOrderCommand(Guid OrderId, string Reason) : IRequest;

public sealed record AdvanceOrderCommand(Guid OrderId) : IRequest<OrderDto?>;

internal sealed class OrderLifecycleHandlers(
    IOrderRepository orders,
    IIntegrationEventPublisher events,
    IUnitOfWork unitOfWork,
    TimeProvider clock) :
    IRequestHandler<MarkStockReservedCommand>,
    IRequestHandler<ConfirmPaymentCommand>,
    IRequestHandler<RejectOrderCommand>,
    IRequestHandler<AdvanceOrderCommand, OrderDto?>
{
    public Task Handle(MarkStockReservedCommand command, CancellationToken ct) =>
        ApplyAsync(command.OrderId, (order, now) => order.MarkStockReserved(now), ct);

    public Task Handle(ConfirmPaymentCommand command, CancellationToken ct) =>
        ApplyAsync(command.OrderId, (order, now) => order.Confirm(now), ct);

    public Task Handle(RejectOrderCommand command, CancellationToken ct) =>
        ApplyAsync(command.OrderId, (order, now) => order.Reject(command.Reason, now), ct);

    public async Task<OrderDto?> Handle(AdvanceOrderCommand command, CancellationToken ct)
    {
        var order = await ApplyAsync(command.OrderId, (order, now) =>
        {
            order.AdvanceFulfilment(now);
            return true;
        }, ct);
        return order is null ? null : OrderDto.From(order);
    }

    private async Task<Order?> ApplyAsync(Guid orderId, Func<Order, DateTimeOffset, bool> transition, CancellationToken ct)
    {
        var order = await orders.GetAsync(orderId, ct);
        if (order is null) return null;

        var now = clock.GetUtcNow();
        if (transition(order, now))
        {
            await events.PublishAsync(order.ToStatusChanged(now), ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        return order;
    }
}
