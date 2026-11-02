using LankaEats.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payments.Api.Data;
using Payments.Api.Gateway;

namespace Payments.Api.Features;

/// <summary>Charges the customer once Inventory has reserved the stock.</summary>
public sealed class ProcessPaymentConsumer(
    PaymentsDbContext db,
    IPaymentGateway gateway,
    TimeProvider clock,
    ILogger<ProcessPaymentConsumer> logger) : IConsumer<StockReserved>
{
    public async Task Consume(ConsumeContext<StockReserved> context)
    {
        var message = context.Message;
        var ct = context.CancellationToken;

        if (await db.Payments.AnyAsync(p => p.OrderId == message.OrderId, ct))
        {
            logger.LogInformation("Payment for order {OrderId} already processed, skipping", message.OrderId);
            return;
        }

        var result = await gateway.ChargeAsync(new ChargeRequest(message.OrderId, message.Total, message.PaymentMethod), ct);

        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            OrderId = message.OrderId,
            CustomerId = message.CustomerId,
            Amount = message.Total,
            Method = message.PaymentMethod,
            Status = result.Succeeded ? PaymentStatus.Succeeded : PaymentStatus.Failed,
            ProviderReference = result.ProviderReference,
            FailureReason = result.FailureReason,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Payments.Add(payment);

        if (result.Succeeded)
        {
            await context.Publish(new PaymentSucceeded(message.OrderId, message.CustomerId, payment.Id, payment.Amount), ct);
        }
        else
        {
            await context.Publish(new PaymentFailed(message.OrderId, message.CustomerId, result.FailureReason ?? "Payment failed."), ct);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Payment {Status} for order {OrderId}", payment.Status, message.OrderId);
    }
}
