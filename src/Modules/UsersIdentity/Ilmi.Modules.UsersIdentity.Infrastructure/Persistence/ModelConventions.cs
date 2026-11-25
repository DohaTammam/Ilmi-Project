using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ilmi.Modules.UsersIdentity.Infrastructure.Persistence;

/// <summary>Values are stored as UTC and come back with Kind = Utc.</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v.HasValue && v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v,
    v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

internal static class SqlChecks
{
    /// <summary>"[column] IN ('A','B')" for the names of an enum, used for CHECK constraints on string-stored enums.</summary>
    public static string In<TEnum>(string column, bool nullable = false) where TEnum : struct, Enum
    {
        var list = string.Join(",", Enum.GetNames<TEnum>().Select(n => $"'{n}'"));
        return nullable ? $"[{column}] IS NULL OR [{column}] IN ({list})" : $"[{column}] IN ({list})";
    }
}
