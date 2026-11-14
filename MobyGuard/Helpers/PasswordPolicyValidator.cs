using System.Collections.Frozen;
using MobyGuard.Classes;

namespace MobyGuard.Helpers;

public static class PasswordPolicyValidator
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    private static readonly FrozenSet<string> BlacklistedPasswords = new[]
    {
        "password",
        "password1",
        "password1!",
        "password123",
        "password123!",
        "password@123",
        "p@ssw0rd",
        "p@ssw0rd!",
        "p@ssword123",
        "p@ssword123!",
        "passw0rd!",
        "passw0rd123!",
        "admin",
        "admin123",
        "admin123!",
        "admin@123",
        "administrator1!",
        "root123!",
        "mobyguard",
        "mobyguard1!",
        "mobyguard123",
        "mobyguard123!",
        "docker123!",
        "registry123!",
        "welcome1!",
        "welcome123",
        "welcome123!",
        "qwerty123",
        "qwerty123!",
        "qwerty@123",
        "letmein1!",
        "letmein123!",
        "changeme1!",
        "changeme123!",
        "abcd1234!",
        "12345678",
        "12345678a!",
        "12345678aa!",
        "iloveyou1!",
        "sunshine1!",
        "princess1!",
        "football1!",
        "baseball1!",
        "trustno1!",
        "dragon123!",
        "master123!",
        "monkey123!",
        "shadow123!",
        "superman1!",
        "michael123!",
        "mustang123!",
        "summer2025!",
        "summer2026!",
        "winter2025!",
        "winter2026!",
        "company123!"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static (bool IsValid, string ErrorMessage) Validate(string? password, string? username = null)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, ResponseMessages.PasswordPolicyViolation);
        }

        var trimmedPassword = password.Trim();

        if (BlacklistedPasswords.Contains(trimmedPassword))
        {
            return (false, ResponseMessages.PasswordBlacklisted);
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            var cleanUser = username.Trim();
            if (cleanUser.Length >= 3
                && (string.Equals(trimmedPassword, cleanUser, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trimmedPassword, $"{cleanUser}123!", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trimmedPassword, $"{cleanUser}@123", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trimmedPassword, $"{cleanUser}1!", StringComparison.OrdinalIgnoreCase)))
            {
                return (false, ResponseMessages.PasswordBlacklisted);
            }
        }

        if (password.Length < MinLength || password.Length > MaxLength)
        {
            return (false, ResponseMessages.PasswordPolicyViolation);
        }

        var hasUpper = false;
        var hasLower = false;
        var hasDigit = false;
        var hasSpecial = false;

        foreach (var ch in password)
        {
            if (char.IsWhiteSpace(ch))
            {
                return (false, ResponseMessages.PasswordPolicyViolation);
            }

            if (char.IsUpper(ch))
            {
                hasUpper = true;
            }
            else if (char.IsLower(ch))
            {
                hasLower = true;
            }
            else if (char.IsDigit(ch))
            {
                hasDigit = true;
            }
            else
            {
                hasSpecial = true;
            }
        }

        if (!hasUpper || !hasLower || !hasDigit || !hasSpecial)
        {
            return (false, ResponseMessages.PasswordPolicyViolation);
        }

        return (true, string.Empty);
    }
}
