using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Imports.Validation;
using Fitzobit.Application.Messaging;
using Fitzobit.Domain.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitzobit.Infrastructure.Messaging;

/// <summary>
/// WhatsApp Business Cloud API client (POST /{version}/{phone-number-id}/messages with a template).
/// Without credentials it does nothing and reports "WhatsApp integration not configured"; no history row is written.
/// When configured, a history row is written only with the API's actual outcome. The access token is never logged.
/// </summary>
public sealed class WhatsAppCloudApiService(
    HttpClient http,
    IOptions<WhatsAppOptions> options,
    IFitzobitDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<WhatsAppCloudApiService> logger) : IWhatsAppService
{
    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        if (!o.IsConfigured) return new WhatsAppSendResult(false, null, WhatsAppOptions.NotConfiguredMessage);

        var phone = PhoneNumberValidator.Validate(message.ToPhoneNumber);
        if (phone.Check != PhoneCheck.ValidMobile)
            return new WhatsAppSendResult(false, null, "The phone number is not a valid mobile number.");

        var language = message.LanguageCode ?? o.DefaultLanguage;
        var payload = new
        {
            messaging_product = "whatsapp",
            to = o.CountryCode + phone.Normalized,
            type = "template",
            template = new
            {
                name = message.TemplateName,
                language = new { code = language },
                components = message.BodyParameters.Count == 0
                    ? Array.Empty<object>()
                    : [new { type = "body", parameters = message.BodyParameters.Select(p => new { type = "text", text = p }).ToArray() }]
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{o.ApiBaseUrl.TrimEnd('/')}/{o.ApiVersion}/{o.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.AccessToken);

        var record = new WhatsAppMessage
        {
            CustomerId = message.CustomerId,
            DeliveryGroupId = message.DeliveryGroupId,
            ToPhoneNumber = phone.Normalized!,
            TemplateName = message.TemplateName,
            LanguageCode = language,
            Purpose = message.Purpose,
            ParametersJson = JsonSerializer.Serialize(message.BodyParameters),
            CreatedAt = clock.UtcNow,
            CreatedBy = currentUser.UserName
        };

        WhatsAppSendResult result;
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<CloudApiResponse>(cancellationToken: cancellationToken);
            var id = body?.Messages?.FirstOrDefault()?.Id;
            result = response.IsSuccessStatusCode && id is not null
                ? new WhatsAppSendResult(true, id, null)
                : new WhatsAppSendResult(false, null, body?.Error?.Message ?? $"WhatsApp API returned {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "WhatsApp API request failed");
            result = new WhatsAppSendResult(false, null, "The WhatsApp API could not be reached.");
        }

        record.Status = result.Accepted ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Failed;
        record.ProviderMessageId = result.ProviderMessageId;
        record.Error = result.Error;
        record.SentAt = result.Accepted ? clock.UtcNow : null;
        db.WhatsAppMessages.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private sealed class CloudApiResponse
    {
        [JsonPropertyName("messages")] public List<CloudApiMessage>? Messages { get; set; }
        [JsonPropertyName("error")] public CloudApiError? Error { get; set; }
    }

    private sealed class CloudApiMessage
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }

    private sealed class CloudApiError
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}
