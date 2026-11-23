# SRS open questions (ilmi Phase 1, SRS v3.1)

Conflicts and gaps found while preparing the backend foundation. **None of these rules is implemented yet.**
Each needs a decision from the business / ilmi digital team before the affected module is built.

| # | Topic | What the SRS says | Why it blocks | Affects |
|---|-------|-------------------|---------------|---------|
| 1 | **RESOLVED** Age bands | Users table: Child 6-12, Teen 13-16, Adult 17+. GSR020: "when a child reaches 12, change to teen". UC013 allows dependents aged 6-16; UC002 EX003/ERR004 reject self-registration under 13. | Is a 12-year-old a Child or a Teen? Is "reaches 12" the 12th birthday or the 13th year? Teens 13-16 self-register, yet teens are also "dependents" linked to parents. | `UserType` rules, AgeTransition job, Family |
| 2 | Password policy references | GSR014 defines the password policy (12 chars). UC002 BR01, UC005 BR02, UC008 BR01 cite "GSR015", which is the network-retry rule. | Likely a typo for GSR014; confirm. | Identity |
| 3 | Login flow | UC003 main flow: email + password **then** an email OTP. UC003 ALT001 (Google/Apple) skips OTP. Quick Login and "stay logged in until manual logout" (UC003 BR06) conflict with NFR007 (15-minute inactivity timeout; a reviewer noted NFR007 "will not be included" in UC006). | Token/session lifetime and whether OTP is required on every login. | Identity |
| 4 | **RESOLVED** OTP lock semantics | GSR018: user is **deactivated** after 5 wrong OTPs. UC002 EX002/BR04, UC003 BR03: verification is **temporarily locked** for 10 minutes. | Permanent deactivation vs. temporary lock. | Identity, Users |
| 5 | Password generation | UC002: system generates the password and **emails it in plain text** (MSG001/MSG003). | Security-sensitive: an emailed credential is a known risk. Confirm whether a forced change on first login or a set-password link is acceptable. | Identity, Notifications |
| 6 | QR code format | GSR016: 10 characters, 0-9 and A-Z. Wireframes show `ILMI-8412-5573` and `ILMI-C-4471`. | Implemented per GSR016 (the written rule). Confirm the wireframes are only illustrative. | Users, Family |
| 7 | "CRM" vs "CMS" | GSR019: fetch content from the **CRM** every 24h. Everywhere else content comes from the **CMS**; no CRM/CMS interface is specified. Scope also lists "CMS Phase 1: Access Management, User Management, Exhibitions" - unclear whether ilmi builds the CMS or integrates an external one. | Build vs integrate, and the integration contract. | CMS/Explore, CrmContentSync job |
| 8 | Error/message codes | UC008 EX002 cites ERR017 for "same password", but ERR017 is "future date of birth" and ERR018 is the "same password" message. ERR023 is missing. INF011 (mobile updated) is referenced by no use case. Mobile-number change flow is undefined. | Code mapping for Identity. | Identity, Users |
| 9 | Mobile uniqueness | GSR013: mobile unique across Adult/Parent/Teen, may repeat for Child. UC015 requires an adult to have a "unique mobile number" to unlink. | A teen turning 17 may share the parent's number; how is it resolved? Not modelled in the DB yet. | Users, Family |
| 10 | Web QR scanning | UC014 lists the Web Application, but there is no scanning mechanism specified for web (reviewer comment RA10). Manual code entry exists (ALT002). | Camera scanning on web, or manual entry only? | Family |
| 11 | Adding a dependent | UC013 requires an email OTP to the **dependent's** email and lets "Parent and Adult" add; comment YK9 says repeated wrong attempts block the parent from adding for "a while". The block duration/threshold is not specified. | Lockout parameters. | Family |
| 12 | Actors vs. platforms | UC010 (News), UC011 (About), UC019 (Contact Us) are Web only; UC004 Quick Login is Mobile only; the Settings/Home UCs cover both. | Confirm which endpoints must be consumed by which client. | API surface |
| 13 | Notification channels | Scope: email, SMS and push. The notification UC and messages only define email/system text (MSG001-005). No SMS/push provider or templates. | Providers and content. | Notifications |
| 14 | Teen self-unlink | Use-case diagram shows "Self-Unlink" for Teenager; UC015 says the actor is an **Adult** (turned 17, GSR020). | Actor naming only; implemented as Adult once built. | Family |
| 15 | Section numbering | The SRS jumps from section 4 to "5 Lookup Values" / "6 Appendices", and the table of contents lists tables 10-13 inconsistently. | Documentation only. | - |

## Resolved

- **Item 1 (age bands):** a 12-year-old is a **Child**. Child 6-12, Teen 13-16, Adult 17+, under 6 has no account.
  To be implemented in the Users module (Domain). Consequence: GSR020 ("when a child reaches 12, change to teen") is read as the transition
  happening on the **13th birthday**. Please confirm that reading, and update GSR020's wording.
- **Item 4 (OTP):** wrong OTP attempts cause a **temporary lock** (default 10 minutes, configurable), **not** deactivation.
  GSR018's "user will be deactivated" wording should be corrected. Not implemented yet (belongs to Identity).

## Decisions taken in the skeleton (reversible)

- **Parent is a flag, not a user type.** UC013/UC015/UC016 describe "flagging" an adult as a parent and removing the flag.
  `UserType` is therefore `Child | Teen | Adult`, plus `User.IsParent`. Guest has no account.
- **No business logic exists yet**; the age bands and OTP lock above are decisions to implement later.
- **Jobs** (GSR019/GSR020) will live in the Worker host and are not implemented.



