using LankaEats.Contracts;
using MassTransit;
using MediatR;
using Orders.Application.Orders;

namespace Orders.Infrastructure.Messaging;

// Thin adapters: translate integration events into application commands.
// Idempotency comes from the MassTransit inbox and from the aggregate's no-op transitions.

public sealed class StockReservedConsumer(ISender sender) : IConsumer<StockReserved>
{
    public Task Consume(ConsumeContext<StockReserved> context) =>
        sender.Send(new MarkStockReservedCommand(context.Message.OrderId), context.CancellationToken);
}

public sealed class StockReservationFailedConsumer(ISender sender) : IConsumer<StockReservationFailed>
{
    public Task Consume(ConsumeContext<StockReservationFailed> context) =>
        sender.Send(new RejectOrderCommand(context.Message.OrderId, context.Message.Reason), context.CancellationToken);
}

public sealed class PaymentSucceededConsumer(ISender sender) : IConsumer<PaymentSucceeded>
{
    public Task Consume(ConsumeContext<PaymentSucceeded> context) =>
        sender.Send(new ConfirmPaymentCommand(context.Message.OrderId), context.CancellationToken);
}

public sealed class PaymentFailedConsumer(ISender sender) : IConsumer<PaymentFailed>
{
    public Task Consume(ConsumeContext<PaymentFailed> context) =>
        sender.Send(new RejectOrderCommand(context.Message.OrderId, context.Message.Reason), context.CancellationToken);
}
