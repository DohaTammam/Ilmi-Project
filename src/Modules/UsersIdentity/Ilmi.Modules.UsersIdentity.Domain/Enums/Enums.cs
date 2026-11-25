namespace Ilmi.Modules.UsersIdentity.Domain.Enums;

/// <summary>How the account was created. Not an age classification and not a Rule.</summary>
public enum UserKind { External, Internal }

/// <summary>Age classification of External users: Child 6-12, Teen 13-16, Adult 17+. Never changed by a Rule.</summary>
public enum UserType { Child, Teen, Adult }

public enum Gender { Male, Female }

/// <summary>Business lifecycle of the account. Independent of <c>User.IsActive</c> (the administrative access gate).</summary>
public enum AccountStatus { Active, PendingActivation, Deactivated }

public enum ExternalProvider { Google, Apple }

public enum OtpPurpose { Registration, Login, PasswordReset, DependentVerification, EmailChange }

/// <summary>Email is the only OTP channel.</summary>
public enum OtpChannel { Email }

public enum ClientType { Web, Mobile }

public enum LoginMethod { PasswordOtp, Google, Apple, QuickLogin }

public enum LoginOutcome { Success, InvalidCredentials, InvalidOtp, AccountLocked, AccountInactive }

public enum SessionRevokedReason { UserLogout, SystemRevoked }

public enum DevicePlatform { iOS, Android }

public enum QuickLoginDisabledReason { UserDisabled, PasswordChanged, OtherUserEnabled, OtherUserLoggedIn, AccountDeactivated }

public enum RuleChangeReason { Registered, DependentAdded, LastDependentUnlinked, AgeTransition, OfficerAssigned, OfficerRemoved }

public enum Language { Ar, En }
