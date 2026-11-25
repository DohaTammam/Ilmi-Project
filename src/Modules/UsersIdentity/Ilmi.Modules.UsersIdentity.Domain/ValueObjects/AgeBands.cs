using Ilmi.Modules.UsersIdentity.Domain.Enums;

namespace Ilmi.Modules.UsersIdentity.Domain.ValueObjects;

/// <summary>Age classification. Age is the number of completed years; no timezone handling (confirmed).</summary>
public static class AgeBands
{
    public const int MinimumAccountAge = 6;   // under 6: no account
    public const int TeenFromAge = 13;        // a 12-year-old is still a Child
    public const int AdultFromAge = 17;

    public static int AgeInYears(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age)) age--;
        return age;
    }

    /// <summary>Returns null when the person is younger than the minimum account age.</summary>
    public static UserType? Classify(DateOnly dateOfBirth, DateOnly today)
    {
        var age = AgeInYears(dateOfBirth, today);
        if (age < MinimumAccountAge) return null;
        if (age < TeenFromAge) return UserType.Child;
        return age < AdultFromAge ? UserType.Teen : UserType.Adult;
    }
}
