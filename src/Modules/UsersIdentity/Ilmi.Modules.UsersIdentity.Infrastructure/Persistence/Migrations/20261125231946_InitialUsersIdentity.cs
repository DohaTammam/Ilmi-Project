using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ilmi.Modules.UsersIdentity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialUsersIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "users");

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                schema: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserKind = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    MobileCountryCode = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: true),
                    MobileNationalNumber = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    UserType = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: true),
                    AccountStatus = table.Column<string>(type: "varchar(17)", unicode: false, maxLength: 17, nullable: false),
                    PendingActivationExpiresAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    MobileUniquenessKey = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: true, computedColumnSql: "CASE WHEN [UserType] = 'Child' THEN CONVERT([varchar](36), [Id]) ELSE '' END", stored: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.UniqueConstraint("AK_AspNetUsers_Id_UserKind", x => new { x.Id, x.UserKind });
                    table.CheckConstraint("ck_users_account_status", "[AccountStatus] IN ('Active','PendingActivation','Deactivated')");
                    table.CheckConstraint("ck_users_kind_profile", "([UserKind] = 'External' AND [UserType] IS NOT NULL AND [MobileCountryCode] IS NOT NULL AND [MobileNationalNumber] IS NOT NULL) OR ([UserKind] = 'Internal' AND [UserType] IS NULL AND (([MobileCountryCode] IS NULL AND [MobileNationalNumber] IS NULL) OR ([MobileCountryCode] IS NOT NULL AND [MobileNationalNumber] IS NOT NULL)))");
                    table.CheckConstraint("ck_users_pending_activation_expiry", "[AccountStatus] <> 'PendingActivation' OR [PendingActivationExpiresAt] IS NOT NULL");
                    table.CheckConstraint("ck_users_user_kind", "[UserKind] IN ('External','Internal')");
                    table.CheckConstraint("ck_users_user_type", "[UserType] IS NULL OR [UserType] IN ('Child','Teen','Adult')");
                    table.ForeignKey(
                        name: "FK_AspNetUsers_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_AspNetUsers_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "avatars",
                schema: "users",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    name_en = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    image_key = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    sort_order = table.Column<byte>(type: "tinyint", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_avatars", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "disability_types",
                schema: "users",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    name_en = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<byte>(type: "tinyint", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_disability_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rules",
                schema: "users",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    name_en = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    is_age_based = table.Column<bool>(type: "bit", nullable: false),
                    is_staff = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<byte>(type: "tinyint", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "verification_lockouts",
                schema: "users",
                columns: table => new
                {
                    purpose = table.Column<string>(type: "varchar(25)", unicode: false, maxLength: 25, nullable: false),
                    scope_key = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    failed_count = table.Column<byte>(type: "tinyint", nullable: false),
                    last_failed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    locked_until = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_lockouts", x => new { x.purpose, x.scope_key });
                    table.CheckConstraint("ck_verification_lockouts_purpose", "[purpose] IN ('Registration','Login','PasswordReset','DependentVerification','EmailChange')");
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "users",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "users",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "users",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "users",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "users",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "internal_users",
                schema: "users",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_kind = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_internal_users", x => x.user_id);
                    table.CheckConstraint("ck_internal_users_user_kind", "[user_kind] = 'Internal'");
                    table.ForeignKey(
                        name: "FK_internal_users_AspNetUsers_user_id_user_kind",
                        columns: x => new { x.user_id, x.user_kind },
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "UserKind" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "login_audit",
                schema: "users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    login_method = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    outcome = table.Column<string>(type: "varchar(25)", unicode: false, maxLength: 25, nullable: false),
                    device_id = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_audit", x => x.id);
                    table.CheckConstraint("ck_login_audit_method", "[login_method] IN ('PasswordOtp','Google','Apple','QuickLogin')");
                    table.CheckConstraint("ck_login_audit_outcome", "[outcome] IN ('Success','InvalidCredentials','InvalidOtp','AccountLocked','AccountInactive')");
                    table.ForeignKey(
                        name: "FK_login_audit_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "otp_challenges",
                schema: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purpose = table.Column<string>(type: "varchar(25)", unicode: false, maxLength: 25, nullable: false),
                    channel = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    target_email = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    initiated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    code_hash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    attempt_count = table.Column<byte>(type: "tinyint", nullable: false),
                    resend_count = table.Column<byte>(type: "tinyint", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    pending_payload = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_challenges", x => x.id);
                    table.CheckConstraint("ck_otp_challenges_channel", "[channel] IN ('Email')");
                    table.CheckConstraint("ck_otp_challenges_purpose", "[purpose] IN ('Registration','Login','PasswordReset','DependentVerification','EmailChange')");
                    table.ForeignKey(
                        name: "FK_otp_challenges_AspNetUsers_initiated_by_user_id",
                        column: x => x.initiated_by_user_id,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_otp_challenges_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quick_login_registrations",
                schema: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_id = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    platform = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    public_key = table.Column<byte[]>(type: "varbinary(512)", nullable: false),
                    key_algorithm = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    key_fingerprint = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    challenge_nonce = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    challenge_expires_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    enabled_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    disabled_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    disabled_reason = table.Column<string>(type: "varchar(25)", unicode: false, maxLength: 25, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quick_login_registrations", x => x.id);
                    table.CheckConstraint("ck_quick_login_disabled_reason", "[disabled_reason] IS NULL OR [disabled_reason] IN ('UserDisabled','PasswordChanged','OtherUserEnabled','OtherUserLoggedIn','AccountDeactivated')");
                    table.CheckConstraint("ck_quick_login_platform", "[platform] IN ('iOS','Android')");
                    table.CheckConstraint("ck_quick_login_state", "([is_enabled] = 1 AND [disabled_at] IS NULL) OR ([is_enabled] = 0 AND [disabled_at] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_quick_login_registrations_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                schema: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    client_type = table.Column<string>(type: "varchar(6)", unicode: false, maxLength: 6, nullable: false),
                    device_id = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    refresh_token_hash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    issued_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    expires_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    revoked_reason = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sessions", x => x.id);
                    table.CheckConstraint("ck_user_sessions_client_type", "[client_type] IN ('Web','Mobile')");
                    table.CheckConstraint("ck_user_sessions_revoked_reason", "[revoked_reason] IS NULL OR [revoked_reason] IN ('UserLogout','SystemRevoked')");
                    table.ForeignKey(
                        name: "FK_user_sessions_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                schema: "users",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    language = table.Column<string>(type: "char(2)", nullable: false),
                    email_notifications_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_settings", x => x.user_id);
                    table.CheckConstraint("ck_user_settings_language", "[language] IN ('ar','en')");
                    table.ForeignKey(
                        name: "FK_user_settings_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "external_users",
                schema: "users",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_kind = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    gender = table.Column<string>(type: "varchar(6)", unicode: false, maxLength: 6, nullable: true),
                    qr_code = table.Column<string>(type: "char(10)", nullable: false, collation: "Latin1_General_BIN"),
                    avatar_id = table.Column<short>(type: "smallint", nullable: true),
                    receive_newsletter = table.Column<bool>(type: "bit", nullable: false),
                    notify_ilmi_special_needs = table.Column<bool>(type: "bit", nullable: false),
                    terms_accepted_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_users", x => x.user_id);
                    table.CheckConstraint("ck_external_users_gender", "[gender] IS NULL OR [gender] IN ('Male','Female')");
                    table.CheckConstraint("ck_external_users_qr_code", "LEN([qr_code]) = 10 AND [qr_code] NOT LIKE '%[^0-9A-Z]%' COLLATE Latin1_General_BIN");
                    table.CheckConstraint("ck_external_users_user_kind", "[user_kind] = 'External'");
                    table.ForeignKey(
                        name: "FK_external_users_AspNetUsers_user_id_user_kind",
                        columns: x => new { x.user_id, x.user_kind },
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumns: new[] { "Id", "UserKind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_users_avatars_avatar_id",
                        column: x => x.avatar_id,
                        principalSchema: "users",
                        principalTable: "avatars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_accessibility_needs",
                schema: "users",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    disability_type_id = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_accessibility_needs", x => new { x.user_id, x.disability_type_id });
                    table.ForeignKey(
                        name: "FK_user_accessibility_needs_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_accessibility_needs_disability_types_disability_type_id",
                        column: x => x.disability_type_id,
                        principalSchema: "users",
                        principalTable: "disability_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_rule_assignments",
                schema: "users",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rule_id = table.Column<short>(type: "smallint", nullable: false),
                    assigned_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_rule_assignments", x => new { x.user_id, x.rule_id });
                    table.ForeignKey(
                        name: "FK_user_rule_assignments_AspNetUsers_assigned_by",
                        column: x => x.assigned_by,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_rule_assignments_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_rule_assignments_rules_rule_id",
                        column: x => x.rule_id,
                        principalSchema: "users",
                        principalTable: "rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_rule_history",
                schema: "users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_rule_id = table.Column<short>(type: "smallint", nullable: true),
                    to_rule_id = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    related_family_link_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    changed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    changed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_rule_history", x => x.id);
                    table.CheckConstraint("ck_user_rule_history_reason", "[reason] IN ('Registered','DependentAdded','LastDependentUnlinked','AgeTransition','OfficerAssigned','OfficerRemoved')");
                    table.ForeignKey(
                        name: "FK_user_rule_history_AspNetUsers_changed_by",
                        column: x => x.changed_by,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_rule_history_AspNetUsers_user_id",
                        column: x => x.user_id,
                        principalSchema: "users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_rule_history_rules_from_rule_id",
                        column: x => x.from_rule_id,
                        principalSchema: "users",
                        principalTable: "rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_rule_history_rules_to_rule_id",
                        column: x => x.to_rule_id,
                        principalSchema: "users",
                        principalTable: "rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "users",
                table: "disability_types",
                columns: new[] { "id", "code", "is_active", "name_ar", "name_en", "sort_order" },
                values: new object[,]
                {
                    { (short)1, "UNABLE_TO_WALK", true, "عدم القدرة على المشي", "Unable to Walk", (byte)1 },
                    { (short)2, "UNABLE_TO_WALK_LONG_DISTANCES", true, "عدم القدرة على المشي لمسافات طويلة", "Unable to Walk Long Distances", (byte)2 },
                    { (short)3, "VISUAL_IMPAIRMENT_BLINDNESS", true, "ضعف البصر / العمى", "Visual Impairment / Blindness", (byte)3 },
                    { (short)4, "HEARING_LOSS_DEAFNESS", true, "فقد حاسة السمع", "Hearing Loss / Deafness", (byte)4 }
                });

            migrationBuilder.InsertData(
                schema: "users",
                table: "rules",
                columns: new[] { "id", "code", "is_active", "is_age_based", "is_staff", "name_ar", "name_en", "sort_order" },
                values: new object[,]
                {
                    { (short)1, "CHILD", true, true, false, null, "Child", (byte)1 },
                    { (short)2, "TEEN", true, true, false, null, "Teen", (byte)2 },
                    { (short)3, "ADULT", true, true, false, null, "Adult", (byte)3 },
                    { (short)4, "PARENT", true, false, false, null, "Parent", (byte)4 },
                    { (short)5, "REGISTRATION_OFFICER", true, false, true, null, "Registration Officer", (byte)5 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "users",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "users",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "users",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ux_AspNetUserLogins_user_provider",
                schema: "users",
                table: "AspNetUserLogins",
                columns: new[] { "UserId", "LoginProvider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "users",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CreatedBy",
                schema: "users",
                table: "AspNetUsers",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_UpdatedBy",
                schema: "users",
                table: "AspNetUsers",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "users",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_users_mobile",
                schema: "users",
                table: "AspNetUsers",
                columns: new[] { "MobileCountryCode", "MobileNationalNumber", "MobileUniquenessKey" },
                unique: true,
                filter: "[MobileNationalNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_email",
                schema: "users",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_avatars_code",
                schema: "users",
                table: "avatars",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_disability_types_code",
                schema: "users",
                table: "disability_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_users_avatar_id",
                schema: "users",
                table: "external_users",
                column: "avatar_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_users_user_id_user_kind",
                schema: "users",
                table: "external_users",
                columns: new[] { "user_id", "user_kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_external_users_qr_code",
                schema: "users",
                table: "external_users",
                column: "qr_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_internal_users_user_id_user_kind",
                schema: "users",
                table: "internal_users",
                columns: new[] { "user_id", "user_kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_login_audit_user_occurred",
                schema: "users",
                table: "login_audit",
                columns: new[] { "user_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_expires_at",
                schema: "users",
                table: "otp_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenges_initiated_by_user_id",
                schema: "users",
                table: "otp_challenges",
                column: "initiated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_target_purpose_created",
                schema: "users",
                table: "otp_challenges",
                columns: new[] { "target_email", "purpose", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenges_user_id",
                schema: "users",
                table: "otp_challenges",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_quick_login_registrations_user_id",
                schema: "users",
                table: "quick_login_registrations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_quick_login_device_enabled",
                schema: "users",
                table: "quick_login_registrations",
                column: "device_id",
                unique: true,
                filter: "[is_enabled] = 1");

            migrationBuilder.CreateIndex(
                name: "ux_quick_login_key_fingerprint",
                schema: "users",
                table: "quick_login_registrations",
                column: "key_fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rules_code",
                schema: "users",
                table: "rules",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_accessibility_needs_disability_type_id",
                schema: "users",
                table: "user_accessibility_needs",
                column: "disability_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_rule_assignments_assigned_by",
                schema: "users",
                table: "user_rule_assignments",
                column: "assigned_by");

            migrationBuilder.CreateIndex(
                name: "ix_user_rule_assignments_rule_id",
                schema: "users",
                table: "user_rule_assignments",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_rule_history_changed_by",
                schema: "users",
                table: "user_rule_history",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "IX_user_rule_history_from_rule_id",
                schema: "users",
                table: "user_rule_history",
                column: "from_rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_rule_history_to_rule_id",
                schema: "users",
                table: "user_rule_history",
                column: "to_rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_rule_history_user_changed",
                schema: "users",
                table: "user_rule_history",
                columns: new[] { "user_id", "changed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_active",
                schema: "users",
                table: "user_sessions",
                column: "user_id",
                filter: "[revoked_at] IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_user_sessions_token_hash",
                schema: "users",
                table: "user_sessions",
                column: "refresh_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "users");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "users");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "users");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "users");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "users");

            migrationBuilder.DropTable(
                name: "external_users",
                schema: "users");

            migrationBuilder.DropTable(
                name: "internal_users",
                schema: "users");

            migrationBuilder.DropTable(
                name: "login_audit",
                schema: "users");

            migrationBuilder.DropTable(
                name: "otp_challenges",
                schema: "users");

            migrationBuilder.DropTable(
                name: "quick_login_registrations",
                schema: "users");

            migrationBuilder.DropTable(
                name: "user_accessibility_needs",
                schema: "users");

            migrationBuilder.DropTable(
                name: "user_rule_assignments",
                schema: "users");

            migrationBuilder.DropTable(
                name: "user_rule_history",
                schema: "users");

            migrationBuilder.DropTable(
                name: "user_sessions",
                schema: "users");

            migrationBuilder.DropTable(
                name: "user_settings",
                schema: "users");

            migrationBuilder.DropTable(
                name: "verification_lockouts",
                schema: "users");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "users");

            migrationBuilder.DropTable(
                name: "avatars",
                schema: "users");

            migrationBuilder.DropTable(
                name: "disability_types",
                schema: "users");

            migrationBuilder.DropTable(
                name: "rules",
                schema: "users");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "users");
        }
    }
}
