using Ilmi.Modules.UsersIdentity.Domain.Enums;

namespace Ilmi.Modules.UsersIdentity.Domain.Entities;

/// <summary>
/// Profile of an External user (a visitor, parent, teen or child). One row per External user; primary key and foreign key
/// <c>UserId</c> point at the shared users table. The mobile number and the UserType are on the user itself.
/// </summary>
public sealed class ExternalUser
{
    private ExternalUser() { }

    public Guid UserId { get; private set; }
    /// <summary>Always External. Together with <see cref="UserId"/> it is a composite foreign key, so this profile can only belong to an External user.</summary>
    public UserKind UserKind { get; private set; } = UserKind.External;
    public DateOnly DateOfBirth { get; private set; }
    public Gender? Gender { get; private set; }
    public string QrCode { get; private set; } = "";
    public short? AvatarId { get; private set; }
    public bool ReceiveNewsletter { get; private set; }
    public bool NotifyIlmiSpecialNeeds { get; private set; }
    public DateTime TermsAcceptedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    internal static ExternalUser Create(Guid userId, DateOnly dateOfBirth, Gender? gender, string qrCode, short? avatarId,
        bool receiveNewsletter, bool notifyIlmiSpecialNeeds, DateTime termsAcceptedAt) => new()
    {
        UserId = userId, DateOfBirth = dateOfBirth, Gender = gender, QrCode = qrCode, AvatarId = avatarId,
        ReceiveNewsletter = receiveNewsletter, NotifyIlmiSpecialNeeds = notifyIlmiSpecialNeeds, TermsAcceptedAt = termsAcceptedAt
    };

    internal void UpdatePreferences(Gender? gender, short? avatarId, bool receiveNewsletter, bool notifyIlmiSpecialNeeds)
    {
        Gender = gender;
        AvatarId = avatarId;
        ReceiveNewsletter = receiveNewsletter;
        NotifyIlmiSpecialNeeds = notifyIlmiSpecialNeeds;
    }
}

/// <summary>
/// Profile of an Internal user (staff). One row per Internal user, with a foreign key to the shared users table. It holds only the
/// optional date of birth. Employee attributes (number, department, job title) are NOT defined by the requirements and are deliberately
/// absent. Internal users have no UserType and no age classification.
/// </summary>
public sealed class InternalUser
{
    private InternalUser() { }

    public Guid UserId { get; private set; }
    /// <summary>Always Internal (composite foreign key with <see cref="UserId"/>).</summary>
    public UserKind UserKind { get; private set; } = UserKind.Internal;
    public DateOnly? DateOfBirth { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    internal static InternalUser Create(Guid userId, DateOnly? dateOfBirth) => new() { UserId = userId, DateOfBirth = dateOfBirth };
}
