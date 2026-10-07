using Fitzobit.Application.Users;
using Fitzobit.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Fitzobit.Infrastructure.Identity;

/// <summary>Password hashing with ASP.NET Core Identity's PBKDF2 hasher (salted, high iteration count, versioned).</summary>
public sealed class IdentityPasswordHashing : IPasswordHashing
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    public string Hash(AppUser user, string password) => _hasher.HashPassword(user, password);

    public PasswordCheck Verify(AppUser user, string password)
    {
        if (string.IsNullOrEmpty(user.PasswordHash)) return PasswordCheck.Failed;
        try
        {
            return _hasher.VerifyHashedPassword(user, user.PasswordHash, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed
            };
        }
        catch (FormatException)
        {
            return PasswordCheck.Failed;
        }
    }
}
