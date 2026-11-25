namespace Ilmi.Modules.UsersIdentity.Domain.Rules;

/// <summary>Codes of the five business Rules (seed data). A Rule is NOT a UserType and NOT a security role.</summary>
public static class RuleCodes
{
    public const string Child = "CHILD";
    public const string Teen = "TEEN";
    public const string Adult = "ADULT";
    public const string Parent = "PARENT";
    public const string RegistrationOfficer = "REGISTRATION_OFFICER";
}

/// <summary>Seed ids of the Rules lookup (stable, referenced by migrations).</summary>
public static class RuleIds
{
    public const short Child = 1, Teen = 2, Adult = 3, Parent = 4, RegistrationOfficer = 5;

    public static short ForUserType(Enums.UserType type) => type switch
    {
        Enums.UserType.Child => Child,
        Enums.UserType.Teen => Teen,
        _ => Adult
    };
}
