using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;

namespace Fitzobit.Domain.DeliveryGroups;

/// <summary>
/// A manager-defined delivery group, known to users by its number ("Group 3"). Not the same thing as a location:
/// one group can span many streets, areas and pincodes; groups are formed for delivery efficiency.
/// </summary>
public class DeliveryGroup : IUserTracked
{
    public int Id { get; set; }

    /// <summary>The user-facing identifier (1, 2, 3, …). Unique; never reused while the group exists.</summary>
    public int GroupNumber { get; set; }

    /// <summary>Optional notes (route, timing, areas).</summary>
    public string? Description { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <remarks>A set, so EF Core adds thousands of assignments in constant time each (a list makes relationship fix-up quadratic).</remarks>
    public ICollection<CustomerDeliveryGroupAssignment> Assignments { get; set; } = new HashSet<CustomerDeliveryGroupAssignment>();

    /// <summary>"Group 3".</summary>
    public static string Label(int groupNumber) => $"Group {groupNumber}";
}

/// <summary>Links a customer to a delivery group. A customer has at most one active assignment.</summary>
public class CustomerDeliveryGroupAssignment
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int DeliveryGroupId { get; set; }
    public DeliveryGroup? DeliveryGroup { get; set; }

    public DateTime AssignedAt { get; set; }
    public string? AssignedBy { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CustomerDeliveryGroupAssignmentHistory
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? PreviousGroupId { get; set; }
    public DeliveryGroup? PreviousGroup { get; set; }

    public int? NewGroupId { get; set; }
    public DeliveryGroup? NewGroup { get; set; }

    public string? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Reason { get; set; }
}
