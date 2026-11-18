using System.Collections.Frozen;
using MobyGuard.Classes;

namespace MobyGuard.Helpers;

public static class PasswordPolicyValidator
{
    public const int MinLength = 8;
    public const int MaxLength = 64;

    private static readonly FrozenSet<string> BlacklistedPasswords = LoadBlacklistedPasswords();

    private static FrozenSet<string> LoadBlacklistedPasswords()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "PwnedPasswordsTop100k.txt");

        if (!File.Exists(path))
        {
            return FrozenSet<string>.Empty;
        }

        return File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBlacklisted(string trimmedPassword)
    {
        if (BlacklistedPasswords.Contains(trimmedPassword))
        {
            return true;
        }

        var end = trimmedPassword.Length;
        while (end > 0 && !char.IsAsciiLetter(trimmedPassword[end - 1]))
        {
            end--;
        }

        if (end >= 4)
        {
            var rootWord = trimmedPassword[..end];
            if (BlacklistedPasswords.Contains(rootWord)
                || string.Equals(rootWord, "mobyguard", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static (bool IsValid, string ErrorMessage) Validate(string? password, string? username = null)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, ResponseMessages.PasswordPolicyViolation);
        }

        if (password.Length < MinLength || password.Length > MaxLength)
        {
            return (false, ResponseMessages.PasswordPolicyViolation);
        }

        var trimmedPassword = password.Trim();

        if (IsBlacklisted(trimmedPassword))
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
