using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Users;

public enum PasswordCheck
{
    Failed,
    Success,
    SuccessRehashNeeded
}

/// <summary>Salted, slow password hashing (implemented with ASP.NET Core Identity's hasher in Infrastructure).</summary>
public interface IPasswordHashing
{
    string Hash(AppUser user, string password);
    PasswordCheck Verify(AppUser user, string password);
}

public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    public static void Validate(string? password, string userName)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            throw new BusinessRuleException($"Password must be at least {MinLength} characters.");
        if (password.Length > MaxLength)
            throw new BusinessRuleException($"Password must be at most {MaxLength} characters.");
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new BusinessRuleException("Password must contain both letters and numbers.");
        if (password.Contains(userName, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Password must not contain the user name.");
    }
}

public sealed record UserDto(int Id, string UserName, string DisplayName, string Role, bool IsActive, DateTime? LastLoginAt, DateTime CreatedAt, bool IsLockedOut);

public sealed record SignInResult(bool Succeeded, AppUser? User, string? Error);

/// <summary>Sign-in, first-run setup and session validation.</summary>
public sealed class AuthService(IFitzobitDbContext db, IPasswordHashing hashing, IAuditLogger audit, IClock clock)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const string GenericError = "Invalid user name or password.";

    private static string? _dummyHash;

    public async Task<bool> SetupRequiredAsync(CancellationToken ct = default) => !await db.Users.AnyAsync(ct);

    /// <summary>Creates the first administrator. Only possible while no user exists.</summary>
    public async Task<AppUser> CreateFirstAdminAsync(string userName, string displayName, string password, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) throw new BusinessRuleException("Setup has already been completed.");
        var user = UserService.NewUser(userName, displayName, UserRole.Admin);
        PasswordPolicy.Validate(password, user.UserName);
        user.PasswordHash = hashing.Hash(user, password);
        user.LastLoginAt = clock.UtcNow;
        db.Users.Add(user);
        audit.RecordAs(user.UserName, AuditActions.UserCreated, nameof(AppUser), user.UserName, $"First administrator created: {user.UserName}",
            new { Role = user.Role.ToString() });
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<SignInResult> SignInAsync(string userName, string password, CancellationToken ct = default)
    {
        var name = (userName ?? "").Trim().ToLowerInvariant();
        var now = clock.UtcNow;
        var user = name.Length == 0 ? null : await db.Users.FirstOrDefaultAsync(u => u.UserName == name, ct);

        if (user is null || !user.IsActive)
        {
            // Verify against a throwaway hash so unknown and known user names take similar time.
            var dummy = new AppUser { UserName = "unknown" };
            dummy.PasswordHash = _dummyHash ??= hashing.Hash(dummy, Guid.NewGuid().ToString("N"));
            hashing.Verify(dummy, password ?? "");
            audit.Record(AuditActions.UserSignInFailed, nameof(AppUser), Truncate(name), "Sign-in failed (unknown or inactive user)");
            await db.SaveChangesAsync(ct);
            return new SignInResult(false, null, GenericError);
        }

        if (user.LockoutEndsAt is { } until && until > now)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
            return new SignInResult(false, null, $"Too many failed attempts. Try again in {minutes} minute(s).");
        }

        var check = hashing.Verify(user, password ?? "");
        if (check == PasswordCheck.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutEndsAt = now + LockoutDuration;
                user.FailedLoginCount = 0;
            }
            audit.Record(AuditActions.UserSignInFailed, nameof(AppUser), user.UserName, "Sign-in failed (wrong password)");
            await db.SaveChangesAsync(ct);
            return new SignInResult(false, null, GenericError);
        }

        if (check == PasswordCheck.SuccessRehashNeeded) user.PasswordHash = hashing.Hash(user, password!);
        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        user.LastLoginAt = now;
        audit.RecordAs(user.UserName, AuditActions.UserSignedIn, nameof(AppUser), user.UserName, $"{user.UserName} signed in");
        await db.SaveChangesAsync(ct);
        return new SignInResult(true, user, null);
    }

    /// <summary>Re-checks a signed-in session: the user must still exist, be active and have the same security stamp.</summary>
    public async Task<AppUser?> ValidateSessionAsync(int userId, string? securityStamp, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is { IsActive: true } && user.SecurityStamp == securityStamp ? user : null;
    }

    private static string Truncate(string value) => value.Length > 100 ? value[..100] : value;
}

/// <summary>User administration (Admin only) and self-service password change.</summary>
public sealed class UserService(IFitzobitDbContext db, IPasswordHashing hashing, IAuditLogger audit)
{
    public static AppUser NewUser(string userName, string displayName, UserRole role)
    {
        var name = (userName ?? "").Trim().ToLowerInvariant();
        if (name.Length is < 3 or > 50 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            throw new BusinessRuleException("User name must be 3–50 characters: letters, numbers, '.', '_' or '-'.");
        var display = string.Join(' ', (displayName ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (display.Length is < 2 or > 100) throw new BusinessRuleException("Display name must be 2–100 characters.");
        return new AppUser { UserName = name, DisplayName = display, Role = role, IsActive = true };
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(DateTime now, CancellationToken ct = default) =>
        (await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(ct))
        .Select(u => new UserDto(u.Id, u.UserName, u.DisplayName, u.Role.ToString(), u.IsActive, u.LastLoginAt, u.CreatedAt,
            u.LockoutEndsAt > now))
        .ToList();

    public async Task<UserDto> CreateAsync(string userName, string displayName, UserRole role, string password, CancellationToken ct = default)
    {
        var user = NewUser(userName, displayName, role);
        if (await db.Users.AnyAsync(u => u.UserName == user.UserName, ct))
            throw new BusinessRuleException($"The user name \"{user.UserName}\" is already taken.");
        PasswordPolicy.Validate(password, user.UserName);
        user.PasswordHash = hashing.Hash(user, password);
        db.Users.Add(user);
        audit.Record(AuditActions.UserCreated, nameof(AppUser), user.UserName, $"User created: {user.UserName} ({role})", new { Role = role.ToString() });
        await db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.UserName, user.DisplayName, user.Role.ToString(), true, null, user.CreatedAt, false);
    }

    public async Task UpdateAsync(int id, string displayName, UserRole role, bool isActive, int actingUserId, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct);
        var display = NewUser(user.UserName, displayName, role).DisplayName;
        if (id == actingUserId && (!isActive || role != user.Role))
            throw new BusinessRuleException("You cannot deactivate yourself or change your own role.");
        if (user.Role == UserRole.Admin && user.IsActive && (role != UserRole.Admin || !isActive)
            && await db.Users.CountAsync(u => u.Role == UserRole.Admin && u.IsActive, ct) <= 1)
            throw new BusinessRuleException("At least one active administrator is required.");

        var changes = new ChangeSet();
        changes.Track("DisplayName", user.DisplayName, display);
        changes.Track("Role", user.Role.ToString(), role.ToString());
        changes.Track("IsActive", user.IsActive, isActive);
        if (!changes.HasChanges) return;

        if (user.Role != role || user.IsActive != isActive) user.RotateSecurityStamp();
        user.DisplayName = display;
        user.Role = role;
        user.IsActive = isActive;
        audit.RecordChange(AuditActions.UserUpdated, nameof(AppUser), user.UserName, $"User updated: {user.UserName}", changes.Old, changes.New);
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetPasswordAsync(int id, string newPassword, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct);
        PasswordPolicy.Validate(newPassword, user.UserName);
        user.PasswordHash = hashing.Hash(user, newPassword);
        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        user.RotateSecurityStamp();
        audit.Record(AuditActions.UserPasswordReset, nameof(AppUser), user.UserName, $"Password reset for {user.UserName}");
        await db.SaveChangesAsync(ct);
    }

    /// <returns>The user with its new security stamp, so the caller can re-issue the current sign-in.</returns>
    public async Task<AppUser> ChangeOwnPasswordAsync(int id, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct);
        if (hashing.Verify(user, currentPassword ?? "") == PasswordCheck.Failed)
            throw new BusinessRuleException("The current password is not correct.");
        PasswordPolicy.Validate(newPassword, user.UserName);
        user.PasswordHash = hashing.Hash(user, newPassword);
        user.RotateSecurityStamp();
        audit.Record(AuditActions.UserPasswordChanged, nameof(AppUser), user.UserName, $"{user.UserName} changed their password");
        await db.SaveChangesAsync(ct);
        return user;
    }

    private async Task<AppUser> FindAsync(int id, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException($"User {id} was not found.");
}
