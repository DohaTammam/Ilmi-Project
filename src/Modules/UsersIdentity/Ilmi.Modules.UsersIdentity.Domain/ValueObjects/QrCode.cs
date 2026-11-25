using System.Security.Cryptography;

namespace Ilmi.Modules.UsersIdentity.Domain.ValueObjects;

/// <summary>GSR016: 10 characters, digits 0-9 and letters A-Z, unique per user.</summary>
public static class QrCode
{
    public const int Length = 10;
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    public static bool IsValid(string? value) =>
        value is { Length: Length } && value.All(c => Alphabet.Contains(c));
}
