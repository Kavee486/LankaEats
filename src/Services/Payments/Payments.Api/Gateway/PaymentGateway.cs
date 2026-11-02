using LankaEats.Contracts;

namespace Payments.Api.Gateway;

public sealed record ChargeRequest(Guid OrderId, decimal Amount, string Method);

public sealed record ChargeResult(bool Succeeded, string? ProviderReference, string? FailureReason);

/// <summary>
/// Abstraction over a payment provider. To go live, implement it with PayHere (Sri Lanka) or Stripe.
/// Both have free sandbox / test modes. Pass <see cref="ChargeRequest.OrderId"/> as the provider's
/// idempotency key so a retried charge is never captured twice.
/// </summary>
public interface IPaymentGateway
{
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct);
}

/// <summary>Deterministic simulator, so both the happy path and the compensation path can be demoed.</summary>
public sealed class SimulatedPaymentGateway(IConfiguration configuration) : IPaymentGateway
{
    private readonly decimal _cardLimit = configuration.GetValue("Payments:SimulatedCardLimit", 25_000m);
    private readonly TimeSpan _latency = TimeSpan.FromMilliseconds(configuration.GetValue("Payments:SimulatedLatencyMs", 1500));

    public async Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct)
    {
        await Task.Delay(_latency, ct); // makes the "Pending → Confirmed" transition visible in the UI

        return request.Method switch
        {
            PaymentMethods.CashOnDelivery => Approved("COD"),
            PaymentMethods.TestDeclinedCard => new ChargeResult(false, null, "Card declined by issuer (test card)."),
            PaymentMethods.Card when request.Amount > _cardLimit =>
                new ChargeResult(false, null, $"Amount exceeds the card limit of LKR {_cardLimit:N0}."),
            PaymentMethods.Card => Approved("SIM"),
            _ => new ChargeResult(false, null, $"Unsupported payment method '{request.Method}'."),
        };
    }

    private static ChargeResult Approved(string prefix) =>
        new(true, $"{prefix}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(), null);
}
