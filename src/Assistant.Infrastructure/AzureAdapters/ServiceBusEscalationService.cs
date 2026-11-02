using System.Text.Json;
using Assistant.Application.Abstractions;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Assistant.Infrastructure.AzureAdapters;

/// <summary>
/// Publishes a handoff package to a Service Bus queue (FR-B8, FR-B9). A consumer outside this service turns it into a
/// ticket in the helpdesk (for example Dynamics 365 Customer Service) and reports progress using the same ticket id.
/// </summary>
public sealed class ServiceBusEscalationService(ServiceBusClient client, IOptions<AzureAdapterOptions> options, TimeProvider clock)
    : IEscalationService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ServiceBusSender _sender = client.CreateSender(options.Value.EscalationQueue);

    public async Task<string> EscalateAsync(HandoffPackage package, CancellationToken ct)
    {
        var ticketId = $"ESC-{Guid.NewGuid():N}";
        var payload = new { ticketId, createdAt = clock.GetUtcNow(), package };

        await _sender.SendMessageAsync(
            new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(payload, Json))
            {
                MessageId = ticketId,
                ContentType = "application/json",
                Subject = "escalation",
            },
            ct).ConfigureAwait(false);

        return ticketId;
    }
}
