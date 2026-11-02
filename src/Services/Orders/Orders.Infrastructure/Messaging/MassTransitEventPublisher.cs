using MassTransit;
using Orders.Application.Abstractions;

namespace Orders.Infrastructure.Messaging;

/// <summary>
/// The scoped <see cref="IPublishEndpoint"/> is the EF bus outbox inside an HTTP request, and the
/// consumer outbox inside a message consumer. In both cases the message is only sent after
/// <c>SaveChangesAsync</c> commits.
/// </summary>
internal sealed class MassTransitEventPublisher(IPublishEndpoint publishEndpoint) : IIntegrationEventPublisher
{
    public Task PublishAsync<T>(T message, CancellationToken ct) where T : class =>
        publishEndpoint.Publish(message, ct);
}
