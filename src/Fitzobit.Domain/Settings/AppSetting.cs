using Fitzobit.Domain.Common;

namespace Fitzobit.Domain.Settings;

/// <summary>An application setting changed from the Settings screen (e.g. the delivery service city).</summary>
public class AppSetting : ITimestamped
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
