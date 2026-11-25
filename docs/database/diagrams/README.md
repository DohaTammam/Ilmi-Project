# ilmi ERD diagrams

Generated from the approved design (`../erd.md` and `../database-design.md`). Design only: no database exists.
There are no SQL/DDL scripts in the repository (none may be created at this stage), so those two documents are the source of truth.
The only "DDL02" is the SRS lookup table of disability types.

| Deliverable | File |
|---|---|
| Complete PDF (7 pages: overview + six modules) | [ilmi-erd-all.pdf](ilmi-erd-all.pdf) |
| Editable draw.io (7 pages) | [ilmi-erd.drawio](ilmi-erd.drawio) |
| Overall ERD (keys only) | [svg](svg/ilmi-erd-00-overview.svg) · [png](png/ilmi-erd-00-overview.png) |
| Users & Identity (21 tables) | [svg](svg/ilmi-erd-01-users-identity.svg) · [png](png/ilmi-erd-01-users-identity.png) |
| Family (1 table) | [svg](svg/ilmi-erd-02-family.svg) · [png](png/ilmi-erd-02-family.png) |
| Notifications (3 tables) | [svg](svg/ilmi-erd-03-notifications.svg) · [png](png/ilmi-erd-03-notifications.png) |
| Platform (2 tables) | [svg](svg/ilmi-erd-04-platform.svg) · [png](png/ilmi-erd-04-platform.png) |
| Support (3 tables) | [svg](svg/ilmi-erd-05-support.svg) · [png](png/ilmi-erd-05-support.png) |
| CMS (9 tables) | [svg](svg/ilmi-erd-06-cms.svg) · [png](png/ilmi-erd-06-cms.png) |
| Automated validation result | [validation.txt](validation.txt) |

PNGs are rendered at 2x. Notation: PK / FK / UQ / LR badges, solid line = physical foreign key, dashed line = logical cross-module reference,
crow's foot cardinality, `NULL` marks nullable columns (blank = NOT NULL), one colour per module, dashed grey boxes are references to a table owned by another module.

## Regenerate

```powershell
python tools/erd/build_erd.py          # SVG, drawio, validation; PNG and PDF via a local Chrome or Edge
python tools/erd/build_erd.py --no-raster
```

The script parses the two design documents, so editing `erd.md` and re-running it is the only way the diagrams change. It exits with an error if a validation check fails.

## Validation (all automated checks pass)

- 39 tables, exactly the inventory in `database-design.md`; six modules with 21 / 1 / 3 / 2 / 3 / 9 tables; every table is in both the overview and its module diagram.
- Not present anywhere: `job_executions`, `accessibility_modes`, `user_accessibility_modes`, `accessibility_mode_id`, `user_credentials`, `external_logins`, `external_user_profiles`, any employee/department/job-title attribute.
- `users` has `user_kind`, `account_status`, `is_active` (IsActive) and `updated_at` (UpdatedOn); `user_settings` has no accessibility column.
- `disability_types` is drawn as a lookup with its four seed rows (1 Unable to Walk, 2 Unable to Walk Long Distances, 3 Visual Impairment / Blindness, 4 Hearing Loss / Deafness, with Arabic);
  `user_accessibility_needs` is the composite-PK junction between it and `external_user_profiles`.
- `notifications` to `notification_deliveries` is one-to-many (physical FK, nullable on the notification side).
- Social-only users: `user_credentials.password_hash` is NOT NULL and the relationship is 1 : 0..1.
- `user_sessions.expires_at` is nullable; `otp_challenges.pending_payload` is `varbinary(max)`; no passcode column in `quick_login_registrations`.
- Every relationship between different modules is a dashed logical reference; no solid line crosses a module boundary.

Validation also found and fixed two cardinality errors in `erd.md` (notification end of the deliveries link, and the parent end of the `exhibition_nodes` self-reference), both because the foreign key is nullable.

## Points for your decision (not assumed)

1. **Main users table:** `AspNetUsers` (ASP.NET Core Identity) is the main users table. `external_users` and `internal_users` each have a (composite) foreign key to it. Email is the login identifier and stays on `AspNetUsers`; the mobile number and `UserType` are on `AspNetUsers` too, so one unique index enforces mobile uniqueness across both kinds. Date of birth, QR code and consents are in the profile tables. Identity's own `PhoneNumber` column is not used.
2. **`internal_users`:** holds only optional mobile number and date of birth (the examples you named). Employee attributes are not defined by the requirements and are not invented.
3. **Identity roles:** `AspNetRoles` and related tables exist (Identity creates them) but no security roles are defined. They are separate from the business Rules.
4. **Mobile uniqueness** is global (Internal and External users, Children excepted): `ux_users_mobile` on `AspNetUsers` over a persisted computed key.
5. **Disability lookup columns:** SRS DDL02 defines only ID, English name and Arabic name; `code`, `sort_order` and `is_active` come from the shared lookup design.
6. **Overview scope:** the overall ERD shows key columns only; all attributes are in the six module diagrams.
7. Open business questions are listed in `../database-design.md` section 10 (Q15 and Q16 deferred).
