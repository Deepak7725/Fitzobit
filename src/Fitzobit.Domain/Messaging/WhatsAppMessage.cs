namespace Fitzobit.Domain.Messaging;

/// <summary>
/// History of messages sent through the official WhatsApp Business (Cloud) API. A row is only written when the
/// API actually accepted or rejected a request; nothing is recorded as "sent" otherwise. Delivery/read status is
/// updated from the provider's status callbacks once those are connected.
/// </summary>
public class WhatsAppMessage
{
    public long Id { get; set; }
    public int? CustomerId { get; set; }
    public int? DeliveryGroupId { get; set; }

    public string ToPhoneNumber { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "en";
    public WhatsAppMessagePurpose Purpose { get; set; }

    /// <summary>Template parameters as sent (JSON array).</summary>
    public string? ParametersJson { get; set; }

    public WhatsAppMessageStatus Status { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public enum WhatsAppMessagePurpose
{
    CustomerNotification,
    DeliveryReminder,
    PlanReminder,
    SubscriptionReminder,
    DeliveryGroupNotification,
    Other
}

public enum WhatsAppMessageStatus
{
    /// <summary>Accepted by the API; not yet delivered.</summary>
    Sent,
    Delivered,
    Read,
    Failed
}
