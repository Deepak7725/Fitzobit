using Fitzobit.Domain.Common;

namespace Fitzobit.Domain.Users;

/// <summary>A person who can sign in. Passwords are stored only as salted hashes.</summary>
public class AppUser : ITimestamped
{
    public int Id { get; set; }

    /// <summary>Sign-in name, stored lower-case. Unique.</summary>
    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Manager;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Changes whenever the password, role or active state changes; existing sign-ins are then invalidated.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndsAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public void RotateSecurityStamp() => SecurityStamp = Guid.NewGuid().ToString("N");
}

public enum UserRole
{
    /// <summary>Everything, including users and administrative settings.</summary>
    Admin,

    /// <summary>Day-to-day operations: upload, customers, groups, export, catalogue (view).</summary>
    Manager
}

public static class Roles
{
    public const string Admin = nameof(UserRole.Admin);
    public const string Manager = nameof(UserRole.Manager);
}
