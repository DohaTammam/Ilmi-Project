#!/usr/bin/env python3
"""Builds the ilmi ERD deliverables (SVG, PNG, PDF, draw.io) from the approved design documents.

Source of truth (parsed, never retyped):
  docs/database/erd.md              entities, columns, types, keys, nullability, relationships
  docs/database/database-design.md  table inventory (name, schema, status)

Usage:  python tools/erd/build_erd.py [--no-raster]
Needs Python 3 only. PNG and PDF are produced with a locally installed Chrome or Edge (headless).
"""
import html, math, os, re, subprocess, sys, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "docs" / "database"
OUT = DOCS / "diagrams"

# ----------------------------------------------------------------------------- style
MODULES = {
    "UsersIdentity": dict(title="Users & Identity", schema="users", color="#1d6fb8", light="#e9f2fb"),
    "Family": dict(title="Family", schema="family", color="#2a9d6f", light="#e7f6ef"),
    "Notifications": dict(title="Notifications", schema="notifications", color="#d9831f", light="#fdf2e3"),
    "Platform": dict(title="Platform", schema="platform", color="#7a52c7", light="#f1ebfb"),
    "Support": dict(title="Support", schema="support", color="#cf4668", light="#fbebf0"),
    "Cms": dict(title="CMS", schema="cms", color="#14a0a6", light="#e3f5f6"),
}
SCHEMA_TO_MODULE = {m["schema"]: k for k, m in MODULES.items()}
FONT_MONO = "Consolas, Menlo, 'DejaVu Sans Mono', monospace"
FONT_UI = "'Segoe UI', Roboto, Helvetica, Arial, sans-serif"
CW = 7.0          # estimated width of one monospace character at 12px
UW = 5.9          # estimated width of one UI character at 10.5px
ROW_H, HEAD_H = 21, 44
INK = "#1f2937"
LINE = "#334155"
STUB_COLOR = "#64748b"

STUB_MODULE = {"UsersIdentity_users": ("AspNetUsers", "UsersIdentity"), "Family_family_links": ("family_links", "Family")}
LOOKUPS = {"avatars", "disability_types", "rules", "ticket_types", "country_calling_codes"}
JUNCTIONS = {"user_accessibility_needs"}
EXTRA_UQ = {  # columns that take part in composite / filtered unique constraints (single-column UK comes from the ERD)
    "AspNetUsers": {"MobileCountryCode", "MobileNationalNumber", "MobileUniquenessKey"},
    "AspNetUserLogins": {"UserId"},
    "quick_login_registrations": {"device_id"},
    "family_links": {"guardian_user_id", "dependent_user_id"},
    "push_device_tokens": {"user_id", "device_id"},
    "ticket_attachments": {"ticket_id", "attachment_no", "sha256"},
}
CONSTRAINTS = {  # transcribed from database-design.md section 3 (unique constraints and key CHECKs)
    "AspNetUsers": ["ASP.NET Core Identity users table (main users table); shared by the Identity user and the Domain user",
                    "UQ (NormalizedEmail): the login identifier; UQ (NormalizedUserName): Identity UserNameIndex",
                    "UQ (MobileCountryCode, MobileNationalNumber, MobileUniquenessKey) WHERE mobile is set: unique across Internal and External users; a Child's key is its own Id, so Children may share a number",
                    "UQ (Id, UserKind): target of the composite FKs from the two profile tables",
                    "CHECK UserKind IN (External, Internal); AccountStatus IN (Active, PendingActivation, Deactivated)",
                    "CHECK External => UserType and mobile NOT NULL; Internal => UserType NULL",
                    "CHECK PendingActivation => PendingActivationExpiresAt NOT NULL",
                    "PasswordHash is NULL for social-only users; lockout uses LockoutEnd / AccessFailedCount"],
    "external_users": ["Composite FK (user_id, user_kind) -> AspNetUsers (Id, UserKind); user_kind = 'External'",
                       "UQ (qr_code), CHECK 10 characters 0-9 A-Z", "CHECK gender IN (Male, Female) or NULL"],
    "internal_users": ["Composite FK (user_id, user_kind) -> AspNetUsers (Id, UserKind); user_kind = 'Internal'",
                       "No employee attributes: none are defined by the requirements"],
    "AspNetRoles": ["Security roles. Not the business Rules; none defined yet"],
    "AspNetUserLogins": ["Google / Apple logins. PK (LoginProvider, ProviderKey); UQ (UserId, LoginProvider)"],
    "rules": ["Seed: Child, Teen, Adult, Parent, RegistrationOfficer", "No permissions columns (undefined)"],
    "user_rule_assignments": ["Composite PK (user_id, rule_id): the same Rule cannot be assigned twice",
                              "No unique constraint on user_id: one or several Rules is an open decision (Q17)"],
    "user_rule_history": ["Append-only",
                          "CHECK reason IN (Registered, DependentAdded, LastDependentUnlinked, AgeTransition, OfficerAssigned, OfficerRemoved)"],
    "user_accessibility_needs": ["Junction table: Composite PK (user_id, disability_type_id)",
                                 "FK user_id -> AspNetUsers.Id ON DELETE CASCADE"],
    "user_settings": ["CHECK language IN (ar, en)", "No accessibility mode column (frontend only)"],
    "otp_challenges": ["CHECK purpose IN (Registration, Login, PasswordReset, DependentVerification, EmailChange)",
                       "CHECK channel = Email (the only channel)", "pending_payload = ciphertext bytes, cleared on use"],
    "verification_lockouts": ["Composite PK (purpose, scope_key)", "No FK by design (key is a hash or a Parent id)"],
    "user_sessions": ["expires_at NULL = active until explicit logout"],
    "quick_login_registrations": ["UQ (device_id) WHERE is_enabled = 1", "CHECK is_enabled = 1 XOR disabled_at set",
                                  "Stores a public key only, never a Device PIN"],
    "family_links": ["UQ (guardian_user_id, dependent_user_id) WHERE status = 'Active'",
                     "CHECK guardian_user_id <> dependent_user_id", "CHECK status IN (Active, Unlinked)",
                     "CHECK link_method IN (Created, QrScan, ManualCode)", "No limit on guardians or dependents"],
    "notification_deliveries": ["FK notification_id ON DELETE SET NULL (1 notification : N deliveries)"],
    "push_device_tokens": ["UQ (user_id, device_id)", "Conditional: push provider not defined"],
    "country_calling_codes": ["calling_code is not unique (+1 is shared)"],
    "support_tickets": ["Numbering, status, title, guest data: Deferred (Q15)"],
    "ticket_attachments": ["UQ (ticket_id, attachment_no), CHECK 1-5", "UQ (ticket_id, sha256)", "CHECK size_bytes <= 3 MB"],
    "exhibition_nodes": ["Hierarchy is provisional (Open)"],
    "exhibitions": ["Category and age filter are provisional (Open)"],
    "content_pages": ["Composite PK (page_key, locale)", "CHECK ISJSON(payload)"],
    "onboarding_completions": ["Application data, not part of the CMS cache"],
}
DISABILITY_SEED = [(1, "Unable to Walk", "عدم القدرة على المشي"),
                   (2, "Unable to Walk Long Distances", "عدم القدرة على المشي لمسافات طويلة"),
                   (3, "Visual Impairment / Blindness", "ضعف البصر / العمى"),
                   (4, "Hearing Loss / Deafness", "فقد حاسة السمع")]
EXCLUDED = {"job_executions", "accessibility_modes", "user_accessibility_modes", "external_user_profiles", "internal_user_profiles", "user_credentials", "external_logins"}
EXCLUDED_COLUMNS = {"accessibility_mode_id", "rule_id"}  # rule_id is on assignments/history only, never on users


# ----------------------------------------------------------------------------- parsing
class Attr:
    def __init__(self, name, typ, keys, comment):
        self.name, self.type, self.comment = name, typ, comment or ""
        self.pk = "PK" in keys
        self.fk = "FK" in keys or bool(re.search(r"\bFK\b", self.comment))
        self.uq = "UK" in keys
        self.lr = "logical" in self.comment.lower() and "->" in self.comment
        self.null = bool(re.match(r"null\b", self.comment.strip()))

    @property
    def keyed(self):
        return self.pk or self.fk or self.uq or self.lr


class Entity:
    def __init__(self, name):
        self.name, self.attrs = name, []


class Rel:
    def __init__(self, a, tl, op, tr, b, label):
        self.a, self.tl, self.logical, self.tr, self.b, self.label = a, tl, op == "..", tr, b, label


CARD_L = {"||": "one", "|o": "zero_one", "}o": "zero_many", "}|": "one_many"}
CARD_R = {"||": "one", "o|": "zero_one", "o{": "zero_many", "|{": "one_many"}
ATTR_RE = re.compile(r'^\s*(\S+)\s+(\S+)(?:\s+((?:PK|FK|UK)(?:\s*,\s*(?:PK|FK|UK))*))?(?:\s+"(.*)")?\s*$')
REL_RE = re.compile(r'^\s*(\w+)\s+(\|\||\|o|\}o|\}\|)(--|\.\.)(\|\||o\||o\{|\|\{)\s+(\w+)\s*:\s*"(.*)"\s*$')


def parse_erd(path):
    text = path.read_text(encoding="utf-8")
    blocks = re.findall(r"```mermaid\n(.*?)```", text, flags=re.S)
    entities, rels = {}, []
    for blk in blocks:
        cur = None
        for raw in blk.splitlines():
            line = raw.rstrip()
            if not line.strip() or line.strip().startswith("%%") or line.strip() == "erDiagram":
                continue
            m = re.match(r"^\s*(\w+)\s*\{\s*$", line)
            if m:
                cur = Entity(m.group(1)); continue
            if line.strip() == "}":
                if cur and (cur.name not in entities or len(cur.attrs) > len(entities[cur.name].attrs)):
                    entities[cur.name] = cur
                cur = None; continue
            if cur is not None:
                m = ATTR_RE.match(line)
                if not m:
                    raise ValueError(f"unparsed attribute line: {line}")
                keys = [k.strip() for k in (m.group(3) or "").split(",") if k.strip()]
                cur.attrs.append(Attr(m.group(2), m.group(1), keys, m.group(4)))
                continue
            m = REL_RE.match(line)
            if m:
                rels.append(Rel(m.group(1), CARD_L[m.group(2)], m.group(3), CARD_R[m.group(4)], m.group(5), m.group(6)))
            else:
                raise ValueError(f"unparsed line: {line}")
    return entities, rels


def parse_inventory(path):
    inv = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        m = re.match(r"^\|\s*(\d+)\s*\|\s*(\w+)\s*\|\s*(\w+)\s*\|\s*([^|]+?)\s*\|", line)
        if m and m.group(3) in SCHEMA_TO_MODULE:
            inv[m.group(2)] = dict(schema=m.group(3), module=SCHEMA_TO_MODULE[m.group(3)], status=m.group(4))
    return inv


def norm(name):
    return STUB_MODULE.get(name, (name, None))[0]


# ----------------------------------------------------------------------------- boxes
class Box:
    def __init__(self, key, ent, module, inv, compact, stub=False):
        self.key, self.name, self.module, self.stub, self.compact = key, ent.name if not stub else key.split(":", 1)[1], module, stub, compact
        self.attrs = [a for a in ent.attrs if (a.keyed or not compact)] if not stub else ent.attrs
        if not self.attrs:
            self.attrs = []
        for a in self.attrs:
            if a.name in EXTRA_UQ.get(self.name, ()):
                a.uq = True
        self.status = inv.get(self.name, {}).get("status", "")
        self.footer = [] if (compact or stub) else CONSTRAINTS.get(self.name, [])
        self.seed = (self.name == "disability_types" and not compact)
        self.x = self.y = 0.0
        self.build_metrics()

    def stereotype(self):
        tags = []
        if self.stub: return "reference (logical, no FK)"
        if self.name in LOOKUPS: tags.append("lookup")
        if self.name in JUNCTIONS: tags.append("junction")
        if self.module == "Cms" and self.name != "onboarding_completions": tags.append("cache")
        s = self.status.lower()
        if self.name.startswith("AspNet"): tags.insert(0, "ASP.NET Identity")
        if "conditional" in s: tags.append("conditional")
        if "deferred" in s: tags.append("details deferred")
        if "provisional" in s: tags.append("provisional")
        return " · ".join(tags)

    def badges(self, a):
        b = []
        if a.pk: b.append("PK")
        if a.fk: b.append("FK")
        if a.uq: b.append("UQ")
        if a.lr: b.append("LR")
        return b

    def build_metrics(self):
        nb = max([len(self.badges(a)) for a in self.attrs] + [1])
        self.badge_w = nb * 23 + 4
        nl = max([len(a.name) for a in self.attrs] + [4]); tl = max([len(a.type) for a in self.attrs] + [4])
        self.name_w, self.type_w = nl * CW, tl * CW
        w = 10 + self.badge_w + 6 + self.name_w + 16 + self.type_w + 8 + 34 + 8
        w = max(w, len(self.name) * 8.6 + 28, 230 if not self.compact else 200)
        if self.seed: w = max(w, 470)
        self.w = math.ceil(w)
        self.foot_lines = []
        for f in self.footer:
            self.foot_lines += wrap(f, self.w - 24)
        h = HEAD_H + max(len(self.attrs), 1) * ROW_H + 6
        if self.seed: h += 24 + len(DISABILITY_SEED) * 22 + 6
        if self.foot_lines: h += 10 + len(self.foot_lines) * 14 + 4
        if self.stub: h = max(h, 96)
        self.h = h

    # geometry helpers
    @property
    def r(self): return self.x + self.w
    @property
    def b(self): return self.y + self.h
    @property
    def cx(self): return self.x + self.w / 2
    @property
    def cy(self): return self.y + self.h / 2


def wrap(text, width):
    per = max(10, int(width / UW)); words, lines, cur = text.split(" "), [], ""
    for w in words:
        if len(cur) + len(w) + 1 > per and cur:
            lines.append(cur); cur = "   " + w if lines or cur else w
        else:
            cur = (cur + " " + w).strip() if cur else w
    if cur: lines.append(cur)
    return lines


# ----------------------------------------------------------------------------- layouts
# grid rows: ("grid", {col: key}) ; ("flow", [keys], center_on_key, gap_before)
def detail_layouts():
    S = lambda t: f"stub:{t}"
    return {
        "UsersIdentity": dict(rows=[
            ("grid", {1: "internal_users", 2: "user_settings", 3: "AspNetUserLogins", 4: "AspNetUserTokens"}, 0),
            ("grid", {0: "avatars", 1: "external_users", 2: "AspNetUsers", 3: "user_rule_assignments", 4: "rules"}, 90),
            ("grid", {0: "disability_types", 1: "user_accessibility_needs", 3: "user_rule_history", 4: S("family_links")}, 90),
            ("flow", ["AspNetUserClaims", "AspNetUserRoles", "otp_challenges", "user_sessions", "quick_login_registrations", "login_audit", "verification_lockouts"], "AspNetUsers", 190),
            ("flow", ["AspNetRoles", "AspNetRoleClaims"], "AspNetUserRoles", 110),
        ], gapx=120),
        "Family": dict(rows=[("grid", {0: S("AspNetUsers"), 1: "family_links"}, 0)], gapx=260),
        "Notifications": dict(rows=[("grid", {1: S("AspNetUsers")}, 0),
                                    ("grid", {0: "notifications", 1: "notification_deliveries", 2: "push_device_tokens"}, 150)], gapx=110),
        "Platform": dict(rows=[("grid", {0: "system_settings", 1: "country_calling_codes"}, 0)], gapx=110),
        "Support": dict(rows=[("grid", {1: S("AspNetUsers")}, 0),
                              ("grid", {0: "ticket_types", 1: "support_tickets", 2: "ticket_attachments"}, 120)], gapx=110),
        "Cms": dict(rows=[
            ("grid", {0: "exhibition_node_translations", 1: "exhibition_nodes", 2: "exhibitions", 3: "exhibition_translations"}, 0),
            ("grid", {0: "content_pages", 1: "update_categories", 2: "updates", 3: "update_translations"}, 100),
            ("grid", {2: "onboarding_completions", 3: S("AspNetUsers")}, 100)], gapx=110),
    }


def overview_layouts():
    return {
        "UsersIdentity": dict(rows=[
            ("grid", {0: "disability_types", 1: "user_accessibility_needs", 2: "external_users", 3: "AspNetUsers", 4: "user_settings", 5: "internal_users"}, 60),
            ("flow", ["AspNetUserLogins", "AspNetUserTokens", "AspNetUserClaims", "AspNetUserRoles", "otp_challenges", "user_sessions", "avatars", "user_rule_assignments",
                      "rules", "user_rule_history", "quick_login_registrations", "login_audit", "verification_lockouts"], "AspNetUsers", 150),
            ("flow", ["AspNetRoles", "AspNetRoleClaims"], "AspNetUserRoles", 90),
        ], gapx=46),
        "Family": dict(rows=[("grid", {0: "family_links"}, 0)], gapx=46),
        "Notifications": dict(rows=[("grid", {0: "notifications", 1: "notification_deliveries", 2: "push_device_tokens"}, 0)], gapx=46),
        "Platform": dict(rows=[("grid", {0: "system_settings", 1: "country_calling_codes"}, 0)], gapx=46),
        "Support": dict(rows=[("grid", {0: "ticket_types", 1: "support_tickets", 2: "ticket_attachments"}, 0)], gapx=46),
        "Cms": dict(rows=[
            ("grid", {0: "exhibition_node_translations", 1: "exhibition_nodes", 2: "exhibitions", 3: "exhibition_translations"}, 0),
            ("grid", {0: "content_pages", 1: "update_categories", 2: "updates", 3: "update_translations"}, 60),
            ("grid", {2: "onboarding_completions"}, 60)], gapx=46),
    }


def place(boxes, spec):
    """Positions boxes (dict key->Box) in local coordinates according to a layout spec. Returns (w, h)."""
    gapx = spec["gapx"]; rows = spec["rows"]
    ncol = max([max(r[1].keys()) + 1 for r in rows if r[0] == "grid"] + [1])
    colw = [0] * ncol
    for r in rows:
        if r[0] == "grid":
            for c, k in r[1].items(): colw[c] = max(colw[c], boxes[k].w)
    colx = [sum(colw[:c]) + c * gapx for c in range(ncol)]
    y = 0.0
    for r in rows:
        keys = list(r[1].values()) if r[0] == "grid" else r[1]
        y += r[-1] if r is not rows[0] else 0
        if r[0] == "grid":
            for c, k in r[1].items():
                bx = boxes[k]; bx.x = colx[c] + (colw[c] - bx.w) / 2; bx.y = y
        else:
            total = sum(boxes[k].w for k in keys) + gapx * 0.55 * (len(keys) - 1)
            x = boxes[r[2]].cx - total / 2
            for k in keys:
                boxes[k].x = x; boxes[k].y = y; x += boxes[k].w + gapx * 0.55
        y += max(boxes[k].h for k in keys)
    minx = min(b.x for b in boxes.values())
    for b in boxes.values(): b.x -= minx
    return max(b.r for b in boxes.values()), max(b.b for b in boxes.values())


# ----------------------------------------------------------------------------- edges / routing
class Edge:
    def __init__(self, a, b, tl, tr, logical, label):
        self.a, self.b, self.tl, self.tr, self.logical, self.label = a, b, tl, tr, logical, label
        self.sa = self.sb = None; self.pa = self.pb = None; self.pts = []; self.way = None


def short_label(rel):
    t = rel.label
    m = re.search(r"logical:\s*([a-z_]+)", t)
    if m: return m.group(1)
    m = re.search(r"physical FK\s+([a-z_]+)\s*$", t)
    parts = []
    if m: parts.append(m.group(1))
    if "SET NULL" in t.upper(): parts.append("SET NULL")
    elif "cascade" in t.lower(): parts.append("CASCADE")
    return " · ".join(parts)


SIDE_VEC = {"right": (1, 0), "left": (-1, 0), "top": (0, -1), "bottom": (0, 1)}


def choose_sides(A, B):
    overlap = min(A.b, B.b) - max(A.y, B.y)
    if overlap > 20:                      # side by side: connect through the facing vertical sides
        if B.x >= A.r + 8: return "right", "left"
        if B.r <= A.x - 8: return "left", "right"
    if B.y >= A.b: return "bottom", "top"
    if B.b <= A.y: return "top", "bottom"
    if B.x >= A.r + 8: return "right", "left"
    if B.r <= A.x - 8: return "left", "right"
    raise ValueError(f"overlapping boxes {A.key} {B.key}")


def ref_row(boxes, e):
    """If the edge label names a FK / logical-reference column, return (box_key, y of that attribute row)."""
    if not e.label: return None
    for k in (e.a, e.b):
        bx = boxes[k]
        for i, a in enumerate(bx.attrs):
            if a.name == e.label and (a.fk or a.lr) and not bx.stub:
                return k, bx.y + HEAD_H + i * ROW_H + ROW_H / 2
    return None


def route(boxes, edges):
    for e in edges:
        A, B = boxes[e.a], boxes[e.b]
        if e.a == e.b: e.sa = e.sb = "top"
        elif e.way is None: e.sa, e.sb = choose_sides(A, B)
    att = {}
    for i, e in enumerate(edges):
        att.setdefault((e.a, e.sa), []).append((e, "a"))
        att.setdefault((e.b, e.sb), []).append((e, "b"))

    def other(e, end):
        o = boxes[e.b if end == "a" else e.a]
        return o

    def desired_y(e, end):
        rr = ref_row(boxes, e); ok = e.b if end == "a" else e.a
        return rr[1] if rr and rr[0] == ok else other(e, end).cy
    # assign anchor positions
    for (key, side), lst in att.items():
        bx = boxes[key]
        if side in ("left", "right"):
            lst.sort(key=lambda t: desired_y(*t))
            lo, hi = bx.y + HEAD_H / 2 + 4, bx.b - 8
        else:
            lst.sort(key=lambda t: other(*t).cx)
            lo, hi = bx.x + 14, bx.r - 14
        n = len(lst)
        for i, (e, end) in enumerate(lst):
            pos = (lo + hi) / 2 if n == 1 else lo + (hi - lo) * (i + 1) / (n + 1)
            if side == "right": pt = (bx.r, pos)
            elif side == "left": pt = (bx.x, pos)
            elif side == "top": pt = (pos, bx.y)
            else: pt = (pos, bx.b)
            if e.a == e.b and end == "b": pt = (bx.x + bx.w * 0.8, bx.y)
            if e.a == e.b and end == "a": pt = (bx.x + bx.w * 0.55, bx.y)
            if end == "a": e.pa = pt
            else: e.pb = pt
    fixed = set()
    for e in edges:
        rr = ref_row(boxes, e)
        if rr and e.a != e.b:
            k, y = rr
            if k == e.a and e.sa in ("left", "right"): e.pa = (e.pa[0], y); fixed.add(id(e))
            elif k == e.b and e.sb in ("left", "right"): e.pb = (e.pb[0], y); fixed.add(id(e))
    # alignment pass: make single-edge pairs straight when the boxes overlap
    for e in edges:
        if e.a == e.b or id(e) in fixed: continue
        A, B = boxes[e.a], boxes[e.b]
        if len(att[(e.a, e.sa)]) == 1 and len(att[(e.b, e.sb)]) == 1:
            if e.sa in ("right", "left"):
                lo = max(A.y + HEAD_H, B.y + HEAD_H); hi = min(A.b - 8, B.b - 8)
                if hi - lo >= 6:
                    y = (lo + hi) / 2; e.pa = (e.pa[0], y); e.pb = (e.pb[0], y)
            else:
                lo = max(A.x + 14, B.x + 14); hi = min(A.r - 14, B.r - 14)
                if hi - lo >= 6:
                    x = (lo + hi) / 2; e.pa = (x, e.pa[1]); e.pb = (x, e.pb[1])
    # channels for vertical (bottom->top / top->bottom) edges: group by the busier end
    vgroups = {}
    for e in edges:
        if e.a == e.b or e.way is not None or e.sa not in ("top", "bottom"): continue
        ga, gb = len(att[(e.a, e.sa)]), len(att[(e.b, e.sb)])
        key = (e.a, e.sa, "a") if ga >= gb else (e.b, e.sb, "b")
        vgroups.setdefault(key, []).append(e)
    chan = {}
    for (key, side, end), lst in vgroups.items():
        crooked = [e for e in lst if abs(e.pa[0] - e.pb[0]) > 1]
        single_x = boxes[key].cx
        crooked.sort(key=lambda e: abs((e.pa if end == "a" else e.pb)[0] - (e.pb if end == "a" else e.pa)[0]))
        # closest first
        for rank, e in enumerate(crooked):
            A, B = boxes[e.a], boxes[e.b]
            top, bottom = (A.b, B.y) if e.sa == "bottom" else (B.b, A.y)
            if end == "a":     # fan-out from the single source: closest goes deepest (nearest the many targets)
                base = (bottom if e.sa == "bottom" else top) - 26
                y = base - rank * 9 if e.sa == "bottom" else base + 52 + rank * 9
                y = base - rank * 9 if e.sa == "bottom" else top + 26 + rank * 9
            else:              # fan-in to the single target: closest goes nearest the many sources
                if e.sa == "bottom": y = top + 26 + rank * 9
                else: y = bottom - 26 - rank * 9
            lo, hi = min(top, bottom) + 6, max(top, bottom) - 6
            chan[id(e)] = min(max(y, lo), hi)
    # horizontal channels: offset per source attachment index
    hoff = {}
    multi = set()
    pair_count = {}
    for e in edges:
        if e.sa in ("left", "right") and e.a != e.b and e.way is None:
            pair_count.setdefault((e.a, e.b), []).append(e)
    for lst in pair_count.values():
        lst.sort(key=lambda e: e.pa[1])
        down = sum(e.pb[1] - e.pa[1] for e in lst) >= 0
        n = len(lst)
        for i, e in enumerate(lst):
            rank = (n - 1 - i) if down else i      # downward elbows: the upper edge takes the channel nearest the target
            sign = 1 if e.sa == "right" else -1
            hoff[id(e)] = sign * (rank - (n - 1) / 2) * 14
            if n > 1: multi.add(id(e))
    srcidx = {}
    for (key, side), lst in att.items():
        for i, (e, end) in enumerate(lst):
            if end == "a" and side in ("left", "right"): srcidx[id(e)] = (i - (len(lst) - 1) / 2) * 10
    for e in edges:
        if e.way is not None:
            e.pts = e.way; continue
        (ax, ay), (bx_, by) = e.pa, e.pb
        if e.a == e.b:
            e.pts = [(ax, ay), (ax, ay - 30), (bx_, ay - 30), (bx_, by)]
        elif e.sa in ("left", "right"):
            if abs(ay - by) < 1: e.pts = [(ax, ay), (bx_, by)]
            else:
                cx = (ax + bx_) / 2 + hoff.get(id(e), 0) + (0 if id(e) in multi else srcidx.get(id(e), 0))
                e.pts = [(ax, ay), (cx, ay), (cx, by), (bx_, by)]
        else:
            if abs(ax - bx_) < 1: e.pts = [(ax, ay), (bx_, by)]
            else:
                cy = chan.get(id(e), (ay + by) / 2)
                e.pts = [(ax, ay), (ax, cy), (bx_, cy), (bx_, by)]


# ----------------------------------------------------------------------------- SVG
def esc(s): return html.escape(str(s), quote=True)


def end_symbol(card, p, d, stroke):
    """Crow's foot symbol at point p; d = unit vector pointing away from the table."""
    (px, py), (dx, dy) = p, d; nx, ny = -dy, dx
    out = []
    def pt(t, s=0): return (px + dx * t + nx * s, py + dy * t + ny * s)
    def seg(a, b): out.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="{stroke}" stroke-width="1.8"/>')
    def bar(t): seg(pt(t, -7), pt(t, 7))
    def circle(t): c = pt(t); out.append(f'<circle cx="{c[0]:.1f}" cy="{c[1]:.1f}" r="4.6" fill="#fff" stroke="{stroke}" stroke-width="1.8"/>')
    def foot():
        tip = pt(15)
        seg(tip, pt(0, -7)); seg(tip, pt(0, 7)); seg(tip, pt(0))
    if card == "one": bar(9); bar(15)
    elif card == "zero_one": bar(9); circle(19)
    elif card == "one_many": foot(); bar(20)
    elif card == "zero_many": foot(); circle(24)
    return "".join(out)


def draw_box(bx, color, light):
    stub = bx.stub
    hc = STUB_COLOR if stub else color
    x, y, w, h = bx.x, bx.y, bx.w, bx.h
    o = [f'<g id="t_{esc(bx.key.replace(":", "_"))}">']
    dash = ' stroke-dasharray="6 4"' if stub else ""
    o.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{w}" height="{h:.1f}" rx="7" fill="#fff" stroke="{hc}" stroke-width="1.8"{dash}/>')
    o.append(f'<path d="M{x:.1f},{y+HEAD_H:.1f} V{y+7:.1f} a7,7 0 0 1 7,-7 H{x+w-7:.1f} a7,7 0 0 1 7,7 V{y+HEAD_H:.1f} Z" fill="{hc if not stub else "#e2e8f0"}"/>')
    tcol = "#fff" if not stub else "#334155"
    title = bx.name if not stub else f"{bx.name}"
    o.append(f'<text x="{x+12:.1f}" y="{y+19:.1f}" font-family="{FONT_UI}" font-size="15" font-weight="700" fill="{tcol}">{esc(title)}</text>')
    sub = (f"{MODULES[bx.module]['schema']}" if not stub else f"{MODULES[bx.module]['title']} module") + (f" · {bx.stereotype()}" if bx.stereotype() else "")
    o.append(f'<text x="{x+12:.1f}" y="{y+35:.1f}" font-family="{FONT_UI}" font-size="10.5" fill="{"#e8eef7" if not stub else "#475569"}" font-style="italic">{esc(sub)}</text>')
    ry = y + HEAD_H
    if not bx.attrs:
        o.append(f'<text x="{x+12:.1f}" y="{ry+15:.1f}" font-family="{FONT_UI}" font-size="11" fill="#94a3b8">(no key columns)</text>')
    for i, a in enumerate(bx.attrs):
        yy = ry + i * ROW_H
        if i % 2 == 1: o.append(f'<rect x="{x+1:.1f}" y="{yy:.1f}" width="{w-2}" height="{ROW_H}" fill="{light}" opacity="0.75"/>')
        bxp = x + 8
        for bname in bx.badges(a):
            if bname == "PK": fill, stroke, txt = "#f4c84f", "#b8860b", "#3a2e00"
            elif bname == "FK": fill, stroke, txt = "#9cc2ee", "#2f6db0", "#0c2a4d"
            elif bname == "UQ": fill, stroke, txt = "#d3b8f3", "#7a52c7", "#2a1050"
            else: fill, stroke, txt = "#ffffff", "#64748b", "#475569"
            da = ' stroke-dasharray="3 2"' if bname == "LR" else ""
            o.append(f'<rect x="{bxp:.1f}" y="{yy+3.5:.1f}" width="21" height="14" rx="3" fill="{fill}" stroke="{stroke}" stroke-width="1"{da}/>')
            o.append(f'<text x="{bxp+10.5:.1f}" y="{yy+14:.1f}" text-anchor="middle" font-family="{FONT_UI}" font-size="9" font-weight="700" fill="{txt}">{bname}</text>')
            bxp += 23
        nx = x + 8 + bx.badge_w + 6
        tip = f"<title>{esc(a.name)} : {esc(a.type)}{' — ' + esc(a.comment) if a.comment else ''}</title>"
        o.append(f'<text x="{nx:.1f}" y="{yy+15:.1f}" font-family="{FONT_MONO}" font-size="12" font-weight="{700 if a.pk else 400}" fill="{INK}">{tip}{esc(a.name)}</text>')
        o.append(f'<text x="{nx+bx.name_w+16:.1f}" y="{yy+15:.1f}" font-family="{FONT_MONO}" font-size="12" fill="#64748b">{esc(a.type)}</text>')
        if a.null:
            o.append(f'<text x="{x+w-10:.1f}" y="{yy+14:.1f}" text-anchor="end" font-family="{FONT_UI}" font-size="9" font-weight="700" fill="#b45309">NULL</text>')
    cy = ry + max(len(bx.attrs), 1) * ROW_H + 6
    if bx.seed:
        o.append(f'<line x1="{x+8:.1f}" y1="{cy:.1f}" x2="{x+w-8:.1f}" y2="{cy:.1f}" stroke="#cbd5e1"/>')
        o.append(f'<text x="{x+12:.1f}" y="{cy+16:.1f}" font-family="{FONT_UI}" font-size="10.5" font-weight="700" fill="#334155">Lookup seed data (SRS DDL02)</text>')
        for i, (sid, en, ar) in enumerate(DISABILITY_SEED):
            yy = cy + 24 + i * 22
            o.append(f'<rect x="{x+8:.1f}" y="{yy:.1f}" width="{w-16}" height="20" fill="{light if i % 2 == 0 else "#fff"}"/>')
            o.append(f'<text x="{x+14:.1f}" y="{yy+14:.1f}" font-family="{FONT_MONO}" font-size="11" font-weight="700" fill="{INK}">{sid}</text>')
            o.append(f'<text x="{x+36:.1f}" y="{yy+14:.1f}" font-family="{FONT_UI}" font-size="11" fill="{INK}">{esc(en)}</text>')
            o.append(f'<text x="{x+w-14:.1f}" y="{yy+14:.1f}" text-anchor="start" direction="rtl" unicode-bidi="plaintext" font-family="Segoe UI, Tahoma, Arial, sans-serif" font-size="12" fill="{INK}">{esc(ar)}</text>')
        cy += 24 + len(DISABILITY_SEED) * 22 + 6
    if bx.foot_lines:
        o.append(f'<line x1="{x+8:.1f}" y1="{cy:.1f}" x2="{x+w-8:.1f}" y2="{cy:.1f}" stroke="#cbd5e1"/>')
        for i, ln in enumerate(bx.foot_lines):
            o.append(f'<text x="{x+12:.1f}" y="{cy+16+i*14:.1f}" font-family="{FONT_UI}" font-size="10.5" fill="#475569" xml:space="preserve">{esc(ln)}</text>')
    o.append("</g>")
    return "".join(o)


def draw_edge(e, show_label=True):
    pts = e.pts
    d = "M" + " L".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    dash = ' stroke-dasharray="8 5"' if e.logical else ""
    o = [f'<path d="{d}" fill="none" stroke="{LINE}" stroke-width="1.7"{dash}/>']
    def outward(p, q):
        dx, dy = q[0] - p[0], q[1] - p[1]; n = math.hypot(dx, dy) or 1
        return (dx / n, dy / n)
    o.append(end_symbol(e.tl, pts[0], outward(pts[0], pts[1]), LINE))
    o.append(end_symbol(e.tr, pts[-1], outward(pts[-1], pts[-2]), LINE))
    if show_label and e.label:
        best = max(range(len(pts) - 1), key=lambda i: math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]))
        (x1, y1), (x2, y2) = pts[best], pts[best + 1]
        mx, my = (x1 + x2) / 2, (y1 + y2) / 2
        horiz = abs(x2 - x1) >= abs(y2 - y1)
        tx, ty = (mx, my - 6) if horiz else (mx + 7, my)
        anchor = "middle" if horiz else "start"
        o.append(f'<text x="{tx:.1f}" y="{ty:.1f}" text-anchor="{anchor}" font-family="{FONT_UI}" font-size="10.5" fill="#475569" '
                 f'paint-order="stroke" stroke="#ffffff" stroke-width="4" stroke-linejoin="round">{esc(e.label)}</text>')
    return "".join(o)


def legend(x, y):
    o = [f'<g transform="translate({x},{y})" font-family="{FONT_UI}" font-size="11" fill="{INK}">']
    o.append('<text x="0" y="0" font-weight="700" font-size="12">Legend</text>')
    cx = 0
    def badge(label, fill, stroke, txt, dash=""):
        nonlocal cx
        o.append(f'<rect x="{cx}" y="12" width="21" height="14" rx="3" fill="{fill}" stroke="{stroke}"{dash}/>'
                 f'<text x="{cx+10.5}" y="22.5" text-anchor="middle" font-size="9" font-weight="700" fill="{txt}">{label}</text>')
        cx += 26
    badge("PK", "#f4c84f", "#b8860b", "#3a2e00"); badge("FK", "#9cc2ee", "#2f6db0", "#0c2a4d")
    badge("UQ", "#d3b8f3", "#7a52c7", "#2a1050"); badge("LR", "#fff", "#64748b", "#475569", ' stroke-dasharray="3 2"')
    o.append(f'<text x="{cx+4}" y="23">primary / foreign / unique / logical reference column.   <tspan font-weight="700" fill="#b45309">NULL</tspan> = nullable, blank = NOT NULL</text>')
    ly = 52
    o.append(f'<line x1="0" y1="{ly}" x2="60" y2="{ly}" stroke="{LINE}" stroke-width="1.7"/><text x="68" y="{ly+4}">physical foreign key (same module)</text>')
    o.append(f'<line x1="300" y1="{ly}" x2="360" y2="{ly}" stroke="{LINE}" stroke-width="1.7" stroke-dasharray="8 5"/><text x="368" y="{ly+4}">logical cross-module reference (no constraint)</text>')
    sx = 0; sy = 80
    for card, label in (("one", "exactly one"), ("zero_one", "zero or one"), ("one_many", "one or many"), ("zero_many", "zero or many")):
        o.append(f'<line x1="{sx}" y1="{sy}" x2="{sx+54}" y2="{sy}" stroke="{LINE}" stroke-width="1.7"/>')
        o.append(end_symbol(card, (sx + 54, sy), (-1, 0), LINE))
        o.append(f'<text x="{sx+62}" y="{sy+4}">{label}</text>')
        sx += 175
    o.append("</g>")
    return "".join(o)


LEGEND_H = 120


def svg_wrap(w, h, body, title, desc):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w:.0f}" height="{h:.0f}" viewBox="0 0 {w:.0f} {h:.0f}">'
            f'<title>{esc(title)}</title><desc>{esc(desc)}</desc><rect width="100%" height="100%" fill="#ffffff"/>{body}</svg>')


# ----------------------------------------------------------------------------- build diagrams
class Diagram:
    def __init__(self, key, title, subtitle):
        self.key, self.title, self.subtitle = key, title, subtitle
        self.boxes, self.edges, self.panels, self.w, self.h = {}, [], [], 0, 0
        self.module = None


def make_boxes(mod_tables, entities, inv, compact, stubs=()):
    boxes = {}
    for t in mod_tables:
        boxes[t] = Box(t, entities[t], inv[t]["module"], inv, compact)
    for s in stubs:
        t = s.split(":", 1)[1]
        ent = Entity(t); ent.attrs = [Attr("id", "uniqueidentifier", ["PK"], "")]
        boxes[s] = Box(s, ent, inv[t]["module"], inv, compact, stub=True)
    return boxes


def build_detail(mod, entities, rels, inv):
    spec = detail_layouts()[mod]
    tables = [t for t, i in inv.items() if i["module"] == mod]
    stubs = [k for r in spec["rows"] for k in (r[1].values() if r[0] == "grid" else r[1]) if k.startswith("stub:")]
    d = Diagram(mod, f"{MODULES[mod]['title']} — detailed ERD", f"Schema {MODULES[mod]['schema']} · {len(tables)} table{"s" if len(tables) != 1 else ""} · one SQL Server database, one schema per module")
    d.module = mod
    d.boxes = make_boxes(tables, entities, inv, False, stubs)
    w, h = place(d.boxes, spec)
    for r in rels:
        a, b = norm(r.a), norm(r.b)
        ma, mb = inv[a]["module"], inv[b]["module"]
        if mod not in (ma, mb): continue
        if ma != mb:  # logical reference: draw it only on the module holding the referencing (many) side
            ref_mod = ma if r.tl in ("zero_many", "one_many") else mb
            if ref_mod != mod: continue
        ka = a if ma == mod else f"stub:{a}"; kb = b if mb == mod else f"stub:{b}"
        if ka not in d.boxes or kb not in d.boxes: continue
        d.edges.append(Edge(ka, kb, r.tl, r.tr, r.logical, short_label(r)))
    route(d.boxes, d.edges)
    d.w, d.h = w, h
    return d


def build_overview(entities, rels, inv):
    specs = overview_layouts()
    d = Diagram("overview", "ilmi database — overall ERD", "Six modules · 39 tables · key columns only (all attributes are in the six detailed module diagrams)")
    local = {}
    for mod, spec in specs.items():
        tables = [t for t, i in inv.items() if i["module"] == mod]
        boxes = make_boxes(tables, entities, inv, True)
        w, h = place(boxes, spec); local[mod] = (boxes, w, h)
    PADX, PADT, PADB, GAP = 36, 62, 30, 90
    top_order = ["Family", "Notifications", "Support", "Cms", "Platform"]
    uw = local["UsersIdentity"][1] + 2 * PADX
    top_w = sum(local[m][1] + 2 * PADX for m in top_order) + GAP * (len(top_order) - 1)
    W = max(uw, top_w) + 220
    left = 120
    top_h = max(local[m][2] + PADT + PADB for m in top_order)
    x = left + (W - 220 - top_w) / 2
    panel = {}
    for m in top_order:
        boxes, w, h = local[m]
        panel[m] = (x, 0.0, w + 2 * PADX, top_h)
        for b in boxes.values(): b.x += x + PADX; b.y += PADT
        x += w + 2 * PADX + GAP
    uy = top_h + 300
    boxes, w, h = local["UsersIdentity"]
    ux = left + (W - 220 - uw) / 2
    panel["UsersIdentity"] = (ux, uy, uw, h + PADT + PADB)
    for b in boxes.values(): b.x += ux + PADX; b.y += uy + PADT
    for m in specs: d.boxes.update(local[m][0])
    d.panels = [(m, *panel[m]) for m in specs]
    seen = {}
    for r in rels:
        a, b = norm(r.a), norm(r.b)
        if a not in d.boxes or b not in d.boxes: continue
        if r.logical:
            k = (a, b); seen[k] = seen.get(k, 0) + 1
            if seen[k] > 1: continue
            d.edges.append(Edge(a, b, r.tl, r.tr, True, ""))
        else:
            d.edges.append(Edge(a, b, r.tl, r.tr, False, ""))
    # the single logical reference from Users & Identity back to Family goes around the left side
    for e in d.edges:
        if e.logical and e.a == "user_rule_history" and e.b == "family_links":
            A, B = d.boxes[e.a], d.boxes[e.b]
            e.sa, e.sb = "bottom", "left"
            uby = panel["UsersIdentity"][1] + panel["UsersIdentity"][3]
            ax = A.cx; ay = A.b
            ex, ey = B.x, B.cy
            lane = 60
            e.way = [(ax, ay), (ax, uby + 22), (lane, uby + 22), (lane, ey), (ex, ey)]
            e.pa, e.pb = e.way[0], e.way[-1]
    route(d.boxes, d.edges)
    d.panels_xy = panel
    d.w = W - 40 + 40
    d.h = max(b.b for b in d.boxes.values()) + 90
    d.w = max(b.r for b in d.boxes.values()) + 90
    return d


def render_svg(d, with_legend=True):
    head = 96
    body = []
    dx, dy = 40, head + LEGEND_H + (48 if any(e.a == e.b for e in d.edges) else 0)
    body.append(f'<text x="40" y="46" font-family="{FONT_UI}" font-size="28" font-weight="700" fill="{INK}">{esc(d.title)}</text>')
    body.append(f'<text x="40" y="72" font-family="{FONT_UI}" font-size="14" fill="#475569">{esc(d.subtitle)}</text>')
    body.append(legend(40, head + 18))
    g = [f'<g transform="translate({dx},{dy})">']
    if d.key == "overview":
        for m, x, y, w, h in d.panels:
            c = MODULES[m]
            g.append(f'<rect x="{x-14:.1f}" y="{y:.1f}" width="{w+28:.1f}" height="{h:.1f}" rx="16" fill="{c["light"]}" stroke="{c["color"]}" stroke-width="2.4"/>')
            g.append(f'<text x="{x+8:.1f}" y="{y+34:.1f}" font-family="{FONT_UI}" font-size="22" font-weight="700" fill="{c["color"]}">{esc(c["title"])}</text>')
            g.append(f'<text x="{x+8+len(c["title"])*13+14:.1f}" y="{y+34:.1f}" font-family="{FONT_UI}" font-size="13" fill="#475569">schema {c["schema"]}</text>')
    else:
        c = MODULES[d.module]
    for e in d.edges: g.append(draw_edge(e, show_label=(d.key != "overview")))
    for b in d.boxes.values():
        c = MODULES[b.module]; g.append(draw_box(b, c["color"], c["light"]))
    g.append("</g>")
    body += g
    w = d.w + 2 * dx + 20; h = d.h + dy + 50
    return svg_wrap(w, h, "".join(body), d.title, d.subtitle), w, h


# ----------------------------------------------------------------------------- draw.io
def drawio(diagrams):
    ER = {"one": "ERmandOne", "zero_one": "ERzeroToOne", "one_many": "ERoneToMany", "zero_many": "ERzeroToMany"}
    pages = []
    for d in diagrams:
        cells = ['<mxCell id="0"/>', '<mxCell id="1" parent="0"/>']
        oy = 60; ox = 40
        cells.append(f'<mxCell id="title" value="{esc(d.title)}" style="text;html=1;fontSize=24;fontStyle=1;align=left;" vertex="1" parent="1"><mxGeometry x="{ox}" y="10" width="900" height="36" as="geometry"/></mxCell>')
        if d.key == "overview":
            for i, (m, x, y, w, h) in enumerate(d.panels):
                c = MODULES[m]
                cells.append(f'<mxCell id="panel_{m}" value="{esc(c["title"])} (schema {c["schema"]})" style="rounded=1;arcSize=3;fillColor={c["light"]};strokeColor={c["color"]};strokeWidth=2;verticalAlign=top;align=left;spacingLeft=12;fontSize=18;fontStyle=1;fontColor={c["color"]};" vertex="1" parent="1"><mxGeometry x="{x-14+ox:.0f}" y="{y+oy:.0f}" width="{w+28:.0f}" height="{h:.0f}" as="geometry"/></mxCell>')
        ids = {}
        for k, b in d.boxes.items():
            c = MODULES[b.module]; tid = "t_" + re.sub(r"\W", "_", k); ids[k] = tid
            col = STUB_COLOR if b.stub else c["color"]
            sub = b.stereotype()
            val = f"{b.name}" + (f"  ({sub})" if sub else "")
            dashed = "dashed=1;" if b.stub else ""
            cells.append(f'<mxCell id="{tid}" value="{esc(val)}" style="swimlane;fontStyle=1;childLayout=stackLayout;horizontal=1;startSize={HEAD_H};horizontalStack=0;resizeParent=1;resizeParentMax=0;resizeLast=0;collapsible=0;marginBottom=0;fillColor={col};fontColor=#ffffff;strokeColor={col};swimlaneFillColor=#ffffff;{dashed}fontSize=14;align=left;spacingLeft=8;" vertex="1" parent="1"><mxGeometry x="{b.x+ox:.0f}" y="{b.y+oy:.0f}" width="{b.w}" height="{b.h:.0f}" as="geometry"/></mxCell>')
            for i, a in enumerate(b.attrs):
                bd = " ".join(f"[{x}]" for x in b.badges(a)) or ""
                txt = f"{bd}  {a.name} : {a.type}" + ("   NULL" if a.null else "")
                st = "text;strokeColor=none;fillColor=none;align=left;verticalAlign=middle;spacingLeft=6;overflow=hidden;fontFamily=Consolas;fontSize=11;" + ("fontStyle=1;" if a.pk else "")
                cells.append(f'<mxCell id="{tid}_a{i}" value="{esc(txt)}" style="{st}" vertex="1" parent="{tid}"><mxGeometry y="{HEAD_H + i*ROW_H}" width="{b.w}" height="{ROW_H}" as="geometry"/></mxCell>')
            yy = HEAD_H + max(len(b.attrs), 1) * ROW_H
            if b.seed:
                for i, (sid, en, ar) in enumerate(DISABILITY_SEED):
                    cells.append(f'<mxCell id="{tid}_s{i}" value="{esc(f"seed {sid} | {en} | {ar}")}" style="text;strokeColor=none;fillColor=#f1f5f9;align=left;verticalAlign=middle;spacingLeft=6;fontSize=11;" vertex="1" parent="{tid}"><mxGeometry y="{yy + i*22}" width="{b.w}" height="22" as="geometry"/></mxCell>')
                yy += len(DISABILITY_SEED) * 22
            if b.foot_lines:
                txt = "&#xa;".join(esc(f) for f in b.footer)
                cells.append(f'<mxCell id="{tid}_c" value="{txt}" style="text;strokeColor=none;fillColor=none;align=left;verticalAlign=top;spacingLeft=6;whiteSpace=wrap;html=1;fontSize=10;fontColor=#475569;" vertex="1" parent="{tid}"><mxGeometry y="{yy}" width="{b.w}" height="{b.h - yy:.0f}" as="geometry"/></mxCell>')
        for i, e in enumerate(d.edges):
            dash = "dashed=1;dashPattern=8 5;" if e.logical else ""
            st = f"edgeStyle=orthogonalEdgeStyle;rounded=0;html=1;endFill=0;startFill=0;startArrow={ER[e.tl]};endArrow={ER[e.tr]};strokeWidth=1.7;strokeColor={LINE};{dash}"
            cells.append(f'<mxCell id="e{i}" value="{esc(e.label)}" style="{st}" edge="1" parent="1" source="{ids[e.a]}" target="{ids[e.b]}"><mxGeometry relative="1" as="geometry"/></mxCell>')
        pages.append(f'<diagram id="{d.key}" name="{esc(d.title)}"><mxGraphModel dx="1400" dy="900" grid="1" gridSize="10" guides="1" page="0" pageWidth="{d.w+200:.0f}" pageHeight="{d.h+200:.0f}"><root>{"".join(cells)}</root></mxGraphModel></diagram>')
    return '<?xml version="1.0" encoding="UTF-8"?>\n<mxfile host="app.diagrams.net" agent="ilmi-erd-generator">' + "".join(pages) + "</mxfile>"


# ----------------------------------------------------------------------------- validation
def validate(entities, rels, inv, diagrams):
    res = []
    def check(name, ok, detail=""):
        res.append((name, bool(ok), detail))
    tables = set(inv)
    check("39 tables in the inventory", len(tables) == 39, f"{len(tables)}")
    per = {}
    for t, i in inv.items(): per.setdefault(i["module"], []).append(t)
    check("six modules", set(per) == set(MODULES), ", ".join(f"{MODULES[m]['title']}={len(per[m])}" for m in MODULES))
    check("module table counts 21/1/3/2/3/9", [len(per[m]) for m in ("UsersIdentity", "Family", "Notifications", "Platform", "Support", "Cms")] == [21, 1, 3, 2, 3, 9])
    real = {n for n in entities if n not in STUB_MODULE}
    check("every ERD entity is in the inventory", real == tables, f"extra={sorted(real - tables)} missing={sorted(tables - real)}")
    drawn = {b.name for d in diagrams if d.key != "overview" for b in d.boxes.values() if not b.stub}
    check("every table appears in a detailed diagram", drawn == tables, f"missing={sorted(tables - drawn)}")
    drawn_o = {b.name for d in diagrams if d.key == "overview" for b in d.boxes.values()}
    check("every table appears in the overview", drawn_o == tables)
    check("excluded tables absent", not (EXCLUDED & (real | {b.name for d in diagrams for b in d.boxes.values()})))
    allcols = {(e.name, a.name) for e in entities.values() for a in e.attrs}
    check("no accessibility_mode_id anywhere", not any(c == "accessibility_mode_id" for _, c in allcols))
    check("user_settings has no accessibility mode column", all("accessib" not in a.name for a in entities["user_settings"].attrs))
    ucols = {a.name for a in entities["AspNetUsers"].attrs}
    check("AspNetUsers (the Identity users table) is the main users table with the Identity columns",
          {"Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "PasswordHash", "LockoutEnd", "LockoutEnabled", "AccessFailedCount", "SecurityStamp"} <= ucols)
    check("AspNetUsers carries UserKind, AccountStatus, IsActive, UpdatedOn, PendingActivationExpiresAt and FirstName/LastName",
          {"UserKind", "AccountStatus", "IsActive", "UpdatedOn", "PendingActivationExpiresAt", "FirstName", "LastName"} <= ucols)
    check("AspNetUsers has no rule column and no profile-table data (date of birth, QR code)", not ({"RuleId", "IsParent", "DateOfBirth", "QrCode"} & ucols))
    check("mobile number and UserType are on AspNetUsers, so one unique index can cover Internal and External users",
          {"MobileCountryCode", "MobileNationalNumber", "UserType", "MobileUniquenessKey"} <= ucols)
    check("PasswordHash is nullable (social-only users have none)", next(a for a in entities["AspNetUsers"].attrs if a.name == "PasswordHash").null)
    check("the two Identity-linked profile tables exist", {"external_users", "internal_users"} <= set(entities))
    ext = {a.name for a in entities["external_users"].attrs}
    check("external_users has date of birth, QR code and consents, and no mobile or user type (those are on AspNetUsers)",
          {"date_of_birth", "qr_code", "terms_accepted_at", "receive_newsletter"} <= ext and not ({"mobile_country_code", "mobile_national_number", "user_type"} & ext))
    check("internal_users has no employee attributes", not any(re.search(r"(?i)employee|department|job_?title", a.name) for a in entities["internal_users"].attrs))
    for prof in ("external_users", "internal_users"):
        r = nd.get(("AspNetUsers", prof)) if False else None
    check("disability_types is a lookup with 4 seed rows", "disability_types" in LOOKUPS and len(DISABILITY_SEED) == 4)
    ua = entities["user_accessibility_needs"]
    check("user_accessibility_needs is a composite-PK junction", [a.name for a in ua.attrs if a.pk] == ["user_id", "disability_type_id"])
    nd = {(norm(r.a), norm(r.b)): r for r in rels}
    r = nd.get(("notifications", "notification_deliveries"))
    check("notifications -> notification_deliveries is 1:N physical", r and not r.logical and r.tl == "zero_one" and r.tr == "zero_many")
    r = nd.get(("AspNetUsers", "user_settings")); check("AspNetUsers -> user_settings 1:0..1", r and r.tl == "one" and r.tr == "zero_one")
    for prof in ("external_users", "internal_users"):
        r = nd.get(("AspNetUsers", prof)); check(f"AspNetUsers -> {prof} physical FK 1:0..1", r and not r.logical and r.tl == "one" and r.tr == "zero_one")
    r = nd.get(("AspNetUsers", "AspNetUserLogins")); check("AspNetUsers -> AspNetUserLogins 1:N (Google/Apple)", r and not r.logical and r.tr == "zero_many")
    r = nd.get(("AspNetUsers", "user_accessibility_needs"))
    check("AspNetUsers -> user_accessibility_needs 1:N through the composite-key junction", r and not r.logical and r.tr == "zero_many")
    r = nd.get(("AspNetUsers", "user_rule_assignments")); check("AspNetUsers -> user_rule_assignments 1:N (no UQ on user_id: single vs several Rules is open)", r and r.tr == "zero_many")
    check("otp_challenges has the Email-only channel column", any(a.name == "channel" for a in entities["otp_challenges"].attrs))
    check("user_sessions.expires_at is nullable (until explicit logout)", next(a for a in entities["user_sessions"].attrs if a.name == "expires_at").null)
    check("pending_payload is varbinary(max)", next(a for a in entities["otp_challenges"].attrs if a.name == "pending_payload").type == "varbinary(max)")
    check("no passcode columns in quick_login_registrations",
          not any("pass" in a.name for a in entities["quick_login_registrations"].attrs))
    cross = [r for r in rels if inv[norm(r.a)]["module"] != inv[norm(r.b)]["module"]]
    check("every cross-module relationship is logical (dashed)", cross and all(r.logical for r in cross), f"{len(cross)} relationships")
    phys_bad = [r for r in rels if not r.logical and inv[norm(r.a)]["module"] != inv[norm(r.b)]["module"]]
    check("no physical FK crosses a module boundary", not phys_bad)
    intra_logical = [r for r in rels if r.logical and inv[norm(r.a)]["module"] == inv[norm(r.b)]["module"]]
    check("no logical line inside one module (those are physical FKs)", not intra_logical, ", ".join(f"{r.a}-{r.b}" for r in intra_logical))
    check("one PK on every table", all(any(a.pk for a in entities[t].attrs) for t in tables))
    return res


# ----------------------------------------------------------------------------- raster output
def find_browser():
    for p in (r"C:\Program Files\Google\Chrome\Application\chrome.exe", r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
              r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
              "/usr/bin/google-chrome", "/usr/bin/chromium", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"):
        if os.path.exists(p): return p
    return None


def rasterize(items, pdf_path):
    br = find_browser()
    if not br:
        print("  no Chrome/Edge found: skipped PNG and PDF"); return False
    prof = tempfile.mkdtemp(prefix="erd-chrome-")
    base = [br, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-sandbox", f"--user-data-dir={prof}",
            "--default-background-color=FFFFFFFF"]
    for key, svg_path, png_path, w, h in items:
        wrapper = Path(prof) / f"{key}.html"
        wrapper.write_text(f'<!doctype html><meta charset="utf-8"><style>html,body{{margin:0;background:#fff}}img{{display:block}}</style>'
                           f'<img src="{svg_path.resolve().as_uri()}" width="{w:.0f}" height="{h:.0f}">', encoding="utf-8")
        cmd = base + ["--force-device-scale-factor=2", f"--window-size={int(w)},{int(h)}", f"--screenshot={png_path}", wrapper.as_uri()]
        subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=180)
        print(f"  png  {png_path.name}  {'ok' if png_path.exists() else 'FAILED'}")
    css, secs = [], []
    for i, (key, svg_path, png_path, w, h) in enumerate(items):
        css.append(f"@page p{i}{{size:{w:.0f}px {h:.0f}px;margin:0}} .s{i}{{page:p{i};width:{w:.0f}px;height:{h:.0f}px;overflow:hidden;"
                   f"{'break-after:page;' if i < len(items) - 1 else ''}}}")
        secs.append(f'<section class="s{i}"><img src="{svg_path.resolve().as_uri()}" width="{w:.0f}" height="{h:.0f}" style="display:block"></section>')
    doc = Path(prof) / "all.html"
    doc.write_text(f'<!doctype html><meta charset="utf-8"><style>html,body{{margin:0}}{"".join(css)}</style>{"".join(secs)}', encoding="utf-8")
    subprocess.run(base + ["--no-pdf-header-footer", f"--print-to-pdf={pdf_path}", doc.as_uri()],
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=300)
    print(f"  pdf  {pdf_path.name}  {'ok' if pdf_path.exists() else 'FAILED'}")
    return True


# ----------------------------------------------------------------------------- main
def main():
    raster = "--no-raster" not in sys.argv
    entities, rels = parse_erd(DOCS / "erd.md")
    inv = parse_inventory(DOCS / "database-design.md")
    diagrams = [build_overview(entities, rels, inv)] + [build_detail(m, entities, rels, inv) for m in
                                                       ("UsersIdentity", "Family", "Notifications", "Platform", "Support", "Cms")]
    results = validate(entities, rels, inv, diagrams)
    (OUT / "svg").mkdir(parents=True, exist_ok=True); (OUT / "png").mkdir(exist_ok=True)
    names = {"overview": "00-overview", "UsersIdentity": "01-users-identity", "Family": "02-family", "Notifications": "03-notifications",
             "Platform": "04-platform", "Support": "05-support", "Cms": "06-cms"}
    items = []
    for d in diagrams:
        svg, w, h = render_svg(d)
        p = OUT / "svg" / f"ilmi-erd-{names[d.key]}.svg"
        p.write_text(svg, encoding="utf-8")
        items.append((names[d.key], p, OUT / "png" / f"ilmi-erd-{names[d.key]}.png", w, h))
        print(f"  svg  {p.name}  {w:.0f}x{h:.0f}")
    (OUT / "ilmi-erd.drawio").write_text(drawio(diagrams), encoding="utf-8")
    print("  drawio ilmi-erd.drawio")
    if raster: rasterize(items, OUT / "ilmi-erd-all.pdf")
    print("\nValidation:")
    for name, ok, detail in results:
        print(f"  [{'PASS' if ok else 'FAIL'}] {name}{('  (' + detail + ')') if detail else ''}")
    (OUT / "validation.txt").write_text("\n".join(f"[{'PASS' if ok else 'FAIL'}] {n}{('  (' + d + ')') if d else ''}" for n, ok, d in results) + "\n", encoding="utf-8")
    if not all(ok for _, ok, _ in results): sys.exit(1)


if __name__ == "__main__":
    main()
