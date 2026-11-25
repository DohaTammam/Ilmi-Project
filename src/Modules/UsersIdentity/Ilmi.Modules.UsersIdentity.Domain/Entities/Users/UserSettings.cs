using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Domain.Entities;

/// <summary>Language and email-notification preference. Accessibility modes are a Frontend concern and are not stored.</summary>
public sealed class UserSettings : IAuditable
{
    private UserSettings() { }

    public Guid UserId { get; private set; }
    public Language Language { get; private set; }
    public bool EmailNotificationsEnabled { get; private set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime UpdatedOn { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[] RowVersion { get; private set; } = [];

    public static UserSettings CreateDefault(Guid userId, Language language, DateTime now) =>
        new() { UserId = userId, Language = language, EmailNotificationsEnabled = true, CreatedAt = now, UpdatedOn = now };

    public void Update(Language language, bool emailNotificationsEnabled)
    {
        Language = language;
        EmailNotificationsEnabled = emailNotificationsEnabled;
    }
}
