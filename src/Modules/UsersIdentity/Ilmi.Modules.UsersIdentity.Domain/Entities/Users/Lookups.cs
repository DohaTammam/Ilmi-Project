namespace Ilmi.Modules.UsersIdentity.Domain.Entities;

/// <summary>Lookup of declared special needs (SRS DDL02). Not related to Frontend accessibility modes.</summary>
public sealed class DisabilityType
{
    private DisabilityType() { }

    public DisabilityType(short id, string code, string nameEn, string nameAr, byte sortOrder, bool isActive = true)
    {
        Id = id; Code = code; NameEn = nameEn; NameAr = nameAr; SortOrder = sortOrder; IsActive = isActive;
    }

    public short Id { get; private set; }
    public string Code { get; private set; } = "";
    public string NameEn { get; private set; } = "";
    public string NameAr { get; private set; } = "";
    public byte SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
}

public sealed class Avatar
{
    private Avatar() { }

    public short Id { get; private set; }
    public string Code { get; private set; } = "";
    public string NameEn { get; private set; } = "";
    /// <summary>Arabic avatar names are not provided by the requirements (open question).</summary>
    public string? NameAr { get; private set; }
    public string ImageKey { get; private set; } = "";
    public byte SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
}

/// <summary>The five business Rules: Child, Teen, Adult, Parent, Registration Officer. Not a UserType and not a security role.</summary>
public sealed class Rule
{
    private Rule() { }

    public Rule(short id, string code, string nameEn, string? nameAr, bool isAgeBased, bool isStaff, byte sortOrder, bool isActive = true)
    {
        Id = id; Code = code; NameEn = nameEn; NameAr = nameAr; IsAgeBased = isAgeBased; IsStaff = isStaff; SortOrder = sortOrder; IsActive = isActive;
    }

    public short Id { get; private set; }
    public string Code { get; private set; } = "";
    public string NameEn { get; private set; } = "";
    /// <summary>Arabic Rule names are not provided by the requirements (open question).</summary>
    public string? NameAr { get; private set; }
    public bool IsAgeBased { get; private set; }
    /// <summary>True for Registration Officer. Its permissions are undefined and are not modelled.</summary>
    public bool IsStaff { get; private set; }
    public byte SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
}
