namespace Fitzobit.Domain.Common;

/// <summary>Entities whose CreatedAt/UpdatedAt are stamped automatically on save.</summary>
public interface ITimestamped
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}

/// <summary>Entities that also record which user created/last changed them.</summary>
public interface IUserTracked : ITimestamped
{
    string? CreatedBy { get; set; }
    string? UpdatedBy { get; set; }
}

public enum RecordStatus
{
    Active,
    Inactive
}
