using Fitzobit.Application.Abstractions;
using Fitzobit.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitzobit.Application.Messaging;

/// <summary>
/// Official WhatsApp Business (Cloud) API settings. The access token is a secret: supply it through an environment
/// variable (WhatsApp__AccessToken) or user secrets, never in appsettings.json or source control.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";
    public const string NotConfiguredMessage = "WhatsApp integration not configured";

    public string ApiBaseUrl { get; set; } = "https://graph.facebook.com";
    public string ApiVersion { get; set; } = "v21.0";
    public string? PhoneNumberId { get; set; }
    public string? BusinessAccountId { get; set; }
    public string? AccessToken { get; set; }
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>Country calling code added to 10-digit numbers.</summary>
    public string CountryCode { get; set; } = "91";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PhoneNumberId) && !string.IsNullOrWhiteSpace(BusinessAccountId) && !string.IsNullOrWhiteSpace(AccessToken);
}

/// <summary>A pre-approved WhatsApp template message (business-initiated messages must use approved templates).</summary>
public sealed record WhatsAppTemplateMessage(
    string ToPhoneNumber,
    string TemplateName,
    string? LanguageCode,
    IReadOnlyList<string> BodyParameters,
    WhatsAppMessagePurpose Purpose,
    int? CustomerId = null,
    int? DeliveryGroupId = null);

public sealed record WhatsAppSendResult(bool Accepted, string? ProviderMessageId, string? Error);

/// <summary>
/// Sends messages through the official WhatsApp Business API only. Implementations must never automate WhatsApp Web,
/// store WhatsApp passwords or report a message as sent unless the API accepted it.
/// </summary>
public interface IWhatsAppService
{
    bool IsConfigured { get; }
    Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken = default);
}

public sealed record WhatsAppStatusDto(
    bool Configured,
    string Message,
    string ApiVersion,
    string? PhoneNumberIdMasked,
    IReadOnlyList<string> PlannedFeatures,
    int MessagesRecorded,
    IReadOnlyDictionary<string, int> MessagesByStatus);

public sealed class WhatsAppStatusService(IOptions<WhatsAppOptions> options, IFitzobitDbContext db)
{
    public async Task<WhatsAppStatusDto> GetAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        var byStatus = await db.WhatsAppMessages.AsNoTracking()
            .GroupBy(m => m.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return new WhatsAppStatusDto(
            o.IsConfigured,
            o.IsConfigured ? "WhatsApp Business API is configured" : WhatsAppOptions.NotConfiguredMessage,
            o.ApiVersion,
            o.PhoneNumberId is { Length: > 4 } id ? new string('•', id.Length - 4) + id[^4..] : null,
            ["Customer notifications", "Delivery reminders", "Plan reminders", "Subscription reminders", "Delivery-group notifications",
             "Message templates", "Message history", "Delivery and read status"],
            byStatus.Sum(x => x.Count),
            byStatus.ToDictionary(x => x.Key.ToString(), x => x.Count));
    }
}
