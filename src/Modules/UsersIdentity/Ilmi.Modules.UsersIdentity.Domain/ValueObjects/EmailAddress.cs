using System.Text.RegularExpressions;

namespace Ilmi.Modules.UsersIdentity.Domain.ValueObjects;

public static partial class EmailAddress
{
    public const int MaxLength = 250;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Trim().Length <= MaxLength && Pattern().IsMatch(email.Trim());

    /// <summary>Key used for uniqueness and for login lookups (login is by email only).</summary>
    public static string Normalize(string email) => email.Trim().ToUpperInvariant();
}
