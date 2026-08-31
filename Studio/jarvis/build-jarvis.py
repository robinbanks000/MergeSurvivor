#!/usr/bin/env python3
"""
JARVIS -- the founder's cockpit over the studio's own state.

    python3 Studio/jarvis/build-jarvis.py && open Studio/jarvis/jarvis.html

Reads the committed kernel records and renders one self-contained page: what is
blocked, what only the founder can unblock, which of the hundred agents are
awake, which gates have ruled, and what is still open.

THREE RULES THIS TOOL IS BUILT ON.

1. It reads; it never writes. Nothing here can change a work order's status,
   sign a gate or file a record. A cockpit that can alter the instruments is
   not a cockpit. Everything below is derived from files under Studio/, and if
   the page and the repository disagree, the repository is right.

2. It never renders a status it cannot verify. This studio spent a week
   removing green lights that meant nothing -- a G3 gate that reported success
   on sixteen runs while running zero tests, a closure check that accepted a
   fail verdict. A dashboard is the easiest place in a system to reintroduce
   that, because a grey box looks like a bug and a green one looks like
   progress. So anything this script cannot establish from disk is rendered
   UNKNOWN and says why. CI results in particular are not knowable from a
   checkout: they live in GitHub Actions, not in the tree.

3. It is separate from the game. It imports nothing from Assets/, runs without
   Unity, and has no dependency beyond the Python standard library -- no npm,
   no lockfile, no package for the supply-chain auditor to carry. The studio's
   tooling must never become the reason the studio is expensive.
"""

import html
import json
import os
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Studio" / "jarvis" / "jarvis.html"


# ---------------------------------------------------------------- reading ---

def read_json(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, json.JSONDecodeError):
        return None


_TRACKED = None


def tracked_files():
    """
    Only files git tracks. Cached because every panel asks.

    This is not fussiness. Walking the filesystem picked up
    Studio/evidence/sims/metrics_pre.json and metrics_post.json -- gitignored
    simulation scratch output with no id, tier or verdict -- and rendered them as two
    more evidence records, taking the count from 14 to 16. Scratch output shown as
    studio evidence is exactly the invented state this page exists not to produce.

    The kernel's own EveryKernelDocumentIsCoveredByTheManifest already draws this line
    and says why: if git does not track it, it is not part of the studio's memory. Same
    rule here, so the page and the gate cannot disagree about what a record is.
    """
    global _TRACKED
    if _TRACKED is None:
        out = git("ls-files", "-z")
        _TRACKED = {p for p in out.split("\0") if p}
    return _TRACKED


def glob_json(relative_dir, pattern="*.json"):
    d = ROOT / relative_dir
    if not d.is_dir():
        return []
    out = []
    for p in sorted(d.rglob(pattern)):
        rel = p.relative_to(ROOT).as_posix()
        if rel not in tracked_files():
            continue
        doc = read_json(p)
        if doc is not None:
            out.append((rel, doc))
    return out


def git(*args, default=""):
    try:
        return subprocess.run(
            ["git", *args], cwd=ROOT, capture_output=True, text=True, timeout=10
        ).stdout.strip() or default
    except (OSError, subprocess.SubprocessError):
        return default


# ------------------------------------------------------------- rendering ---

def esc(value):
    return html.escape(str(value), quote=True)


def chip(text, tone="neutral"):
    return f'<span class="chip {tone}">{esc(text)}</span>'


# The four states any non-literal cell must declare itself as. Rule 2 is enforced by
# making this the only way to render one, so a panel added later cannot invent a fifth
# meaning or quietly show a colour it has not earned. UNAVAILABLE and UNKNOWN are
# different claims and the difference matters: the first says a source exists and this
# checkout cannot reach it, the second says nothing has ever been recorded. Collapsing
# them would turn "we have not looked" into "there is nothing to see".
STATES = {
    "real": ("REAL", "Read from a record in this checkout."),
    "pending": ("PENDING", "A record exists and is open or unresolved."),
    "unavailable": ("UNAVAILABLE", "The source exists, but a checkout cannot reach it."),
    "unknown": ("UNKNOWN", "No record exists."),
}


def state_chip(kind, why=None):
    label, default_why = STATES[kind]
    return f'<span class="chip state-{kind}" title="{esc(why or default_why)}">{esc(label)}</span>'


def unknown(why):
    return state_chip("unknown", why)


def unavailable(why):
    return state_chip("unavailable", why)


def legend():
    items = "".join(
        f'<li>{state_chip(k)}<span class="dim">{esc(why)}</span></li>'
        for k, (_, why) in STATES.items())
    return f'<ul class="legend">{items}</ul>'


def section(sid, title, body, note=None):
    n = f'<p class="note">{note}</p>' if note else ""
    return (f'<section id="{esc(sid)}" aria-labelledby="h-{esc(sid)}" tabindex="-1">'
            f'<h2 id="h-{esc(sid)}">{esc(title)}</h2>{n}{body}</section>')


def table(headers, rows):
    """
    One table markup that works in two layouts.

    Every cell carries its column name in data-label. On a phone the CSS drops the
    header row and turns each row into a stacked record using those labels, which is
    the only way a six-column audit table stays readable at 390px without either
    horizontal scrolling or dropping columns. Dropping columns was the tempting option
    and it is the wrong one: this page's whole claim is that it shows the record, and a
    record with three of its six fields hidden on mobile is a different record.
    """
    if not rows:
        return '<p class="empty">Nothing recorded.</p>'
    head = "".join(f"<th scope=\"col\">{esc(h)}</th>" for h in headers)
    body = ""
    for r in rows:
        cells = "".join(
            f'<td data-label="{esc(headers[i] if i < len(headers) else "")}">{c}</td>'
            for i, c in enumerate(r))
        body += f"<tr>{cells}</tr>"
    return (f'<div class="scroll"><table><thead><tr>{head}</tr></thead>'
            f"<tbody>{body}</tbody></table></div>")


# ---------------------------------------------------------------- panels ---

def panel_founder_queue(state):
    """First, because it is the only list on the page nobody else can act on."""
    items = (state or {}).get("humanQueue") or []
    if not items:
        return '<p class="empty">Nothing is waiting on the founder.</p>'
    return "<ol class='queue'>" + "".join(f"<li>{esc(i)}</li>" for i in items) + "</ol>"


def panel_blocked(state):
    phase = (state or {}).get("phase") or {}
    blocked = phase.get("blockedOn") or []
    deps = (state or {}).get("blockedDependencies") or []
    out = ""
    if blocked:
        out += "<ul>" + "".join(f"<li>{esc(b)}</li>" for b in blocked) + "</ul>"
    if deps:
        out += "<p class='note'>Agents waiting on another agent that is itself dormant:</p><ul class='deps'>"
        out += "".join(f"<li><code>{esc(d)}</code></li>" for d in deps) + "</ul>"
    return out or '<p class="empty">Nothing blocked.</p>'


def panel_gates(gates_doc, verdicts):
    """
    Gate definitions come from the constitution. Whether a gate is CURRENTLY
    passing does not: G2 and G3 are owned by 'ci', and a checkout cannot see a
    CI run. So the status column reports the latest recorded VERDICT per gate --
    a fact on disk -- and says UNKNOWN where none exists, rather than implying
    the gate is green because nothing has contradicted it.
    """
    latest = {}
    for _, v in verdicts:
        g = v.get("gate")
        if not g:
            continue
        prior = latest.get(g)
        if prior is None or v.get("evaluatedAt", "") > prior.get("evaluatedAt", ""):
            latest[g] = v

    rows = []
    for g in (gates_doc or {}).get("gates", []):
        gid = g.get("id", "?")
        v = latest.get(gid)
        if v is None:
            # Owner decides which of the two honest answers applies. A gate owned by
            # 'ci' HAS a live result -- it just lives in GitHub Actions, which no
            # checkout can read. A gate owned by anyone else has simply never ruled.
            if g.get("owner") == "ci":
                status = unavailable(
                    "This gate runs in CI. Its result lives in GitHub Actions and "
                    "cannot be read from a checkout; no verdict record has been filed.")
            else:
                status = unknown(f"No gate-verdict record has ever been filed for {gid}.")
            detail = "&mdash;"
        else:
            tone = {"pass": "ok", "fail": "bad"}.get(v.get("verdict"), "warn")
            status = chip(v.get("verdict", "?"), tone)
            detail = (f'<code>{esc(v.get("commit", "?"))}</code> '
                      f'<span class="dim">{esc(v.get("evaluatedAt", ""))}</span>')
        override = g.get("overridableBy") or []
        rows.append([
            f'<strong>{esc(gid)}</strong> {esc(g.get("name", ""))}',
            f'<code>{esc(g.get("owner", "?"))}</code>',
            status,
            detail,
            chip("nobody", "ok") if override == [] else ", ".join(f"<code>{esc(o)}</code>" for o in override),
        ])
    return table(["Gate", "Owner", "Latest recorded verdict", "At", "Overridable by"], rows)


def panel_orders(orders, verdicts):
    by_key = {}
    for _, v in verdicts:
        by_key[(v.get("gate"), v.get("taskId"), v.get("commit"))] = v

    rows = []
    for path, o in orders:
        if o.get("kind") != "work-order":
            continue
        status = o.get("status", "?")
        tone = {"completed": "ok", "blocked": "bad", "escalated": "warn",
                "dispatched": "info", "in_progress": "info"}.get(status, "neutral")

        if status == "completed":
            commit = o.get("completedByVerdictCommit")
            v = by_key.get((o.get("completedByGate"), o.get("id"), commit))
            if v is None:
                closed = unknown("Closure names a verdict that is not on disk.")
            else:
                closed = (f'{esc(o.get("completedByGate"))} @ <code>{esc(commit)}</code> '
                          f'{chip(v.get("verdict", "?"), "ok" if v.get("verdict") == "pass" else "bad")}')
        else:
            closed = '<span class="dim">not closed</span>'

        rows.append([
            f'<strong>{esc(o.get("id", "?"))}</strong>',
            chip(status, tone),
            f'<code>{esc(o.get("agent", "?"))}</code>',
            str(len(o.get("acceptanceCriteria") or [])),
            closed,
        ])
    return table(["Order", "Status", "Agent", "Criteria", "Closed by"], rows)


def panel_agents(agent_files):
    rows = []
    total = active = 0
    for path, doc in agent_files:
        agents = doc.get("agents") or []
        act = [a for a in agents if a.get("status") == "active"]
        total += len(agents)
        active += len(act)
        division = doc.get("division") or Path(path).stem
        dormant = len(agents) - len(act)

        # activatesWhen is the reason a division sleeps. Showing the count without
        # the reason turns a solvable blocker into a mood.
        reasons = sorted({a.get("activatesWhen") for a in agents
                          if a.get("status") != "active" and a.get("activatesWhen")})
        why = "<br>".join(f'<span class="dim">{esc(r)}</span>' for r in reasons) or "&mdash;"

        rows.append([
            f'<code>{esc(division)}</code>',
            f'{len(act)} / {len(agents)}',
            chip("all awake", "ok") if dormant == 0 else chip(f"{dormant} dormant", "warn"),
            why,
        ])
    summary = (f'<p class="big">{active} <span class="dim">of</span> {total} '
               f'<span class="dim">agents active</span></p>')
    return summary + table(["Division", "Active", "State", "Waiting on"], rows)


def panel_agent_state(state):
    """
    Per-agent state, which the division roll-up above averages away. An idle division
    and a working one look identical at 3/7.
    """
    entries = (state or {}).get("agentStatus") or []
    if not entries:
        return ('<p class="empty">No per-agent state recorded. '
                + unknown("project-state.agentStatus is absent or empty.") + "</p>")

    tone = {"working": "info", "blocked": "bad", "idle": "neutral"}
    rows = [[
        f'<code>{esc(a.get("agent", "?"))}</code>',
        f'<code>{esc(a.get("division", "?"))}</code>',
        chip(a.get("state", "?"), tone.get(a.get("state"), "neutral")),
    ] for a in entries]
    return table(["Agent", "Division", "State"], rows)


def panel_open_questions(challenges, rulings):
    rows = []
    for _, c in challenges:
        if c.get("status") == "open":
            rows.append([
                f'<strong>{esc(c.get("id", "?"))}</strong>',
                "challenge",
                f'<code>{esc(c.get("taskId", "&mdash;"))}</code>',
                esc((c.get("claim") or "")[:160] + ("..." if len(c.get("claim") or "") > 160 else "")),
            ])
    for _, r in rulings:
        readiness = (r.get("gateReadiness") or {}).get("state")
        if readiness == "not_ready":
            blockers = (r.get("gateReadiness") or {}).get("blockingConditions") or []
            rows.append([
                f'<strong>{esc(r.get("id", "?"))}</strong>',
                f'ruling &middot; {esc(r.get("verdict", "?"))}',
                f'<code>{esc(r.get("taskId", "&mdash;"))}</code>',
                f'{len(blockers)} blocking condition(s)' if blockers else "not ready",
            ])
    return table(["Record", "Kind", "Order", "Detail"], rows)


def panel_decisions(decisions):
    rows = []
    for _, d in sorted(decisions, key=lambda t: t[1].get("id", "")):
        rows.append([
            f'<strong>{esc(d.get("id", "?"))}</strong>',
            f'<code>{esc(d.get("level", "?"))}</code>',
            chip(d.get("status", "?"), "ok" if d.get("status") == "accepted" else "warn"),
            esc(d.get("title") or ""),
        ])
    return table(["ADR", "Level", "Status", "Decision"], rows)


def panel_validation(evidence):
    """
    Test and validation status, to the extent a checkout can know it.

    These are EVIDENCE RECORDS, not a live test run: each says a named actor observed a
    named result at a named commit. That is a strong claim -- anyone can check out the
    commit and contradict it -- but it is a claim about the past, and the page must not
    let it read as "the tests are green right now". G3's actual result lives in GitHub
    Actions and is unreachable from here; that is stated rather than papered over.
    """
    rows = []
    for _, e in sorted(evidence, key=lambda t: t[1].get("id", "")):
        verdict = e.get("verdict", "?")
        rows.append([
            f'<strong>{esc(e.get("id", "?"))}</strong>',
            f'<code>{esc(e.get("tier", "?"))}</code>',
            chip(verdict, {"pass": "ok", "fail": "bad"}.get(verdict, "warn")),
            f'<code>{esc(e.get("commit") or "&mdash;")}</code>',
            f'<code>{esc(e.get("producedBy", "?"))}</code>',
            esc(e.get("summary") or ""),
        ])
    note = ('Recorded evidence, each bound to a commit anyone can check out and re-run. '
            'This is history, not a live result: current CI status is '
            + unavailable("CI runs in GitHub Actions and does not commit back, so no "
                          "checkout can read the current result.") + '.')
    return f'<p class="note">{note}</p>' + table(
        ["Evidence", "Tier", "Verdict", "Commit", "Produced by", "Summary"], rows)


def panel_proposals(proposals):
    rows = []
    for _, p in sorted(proposals, key=lambda t: t[1].get("id", "")):
        status = p.get("status", "?")
        rows.append([
            f'<strong>{esc(p.get("id", "?"))}</strong>',
            state_chip("pending") if status == "open" else chip(status, "ok"),
            f'<code>{esc(p.get("raisedBy", "?"))}</code>',
            esc(p.get("priority") or "&mdash;"),
            esc((p.get("problem") or "")[:200] + ("..." if len(p.get("problem") or "") > 200 else "")),
        ])
    return table(["Proposal", "Status", "Raised by", "Priority", "Problem"], rows)


def panel_escalations(escalations):
    rows = []
    for _, e in sorted(escalations, key=lambda t: t[1].get("id", "")):
        status = e.get("status", "?")
        rows.append([
            f'<strong>{esc(e.get("id", "?"))}</strong>',
            state_chip("pending") if status == "open" else chip(status, "ok"),
            f'<code>{esc(e.get("raisedBy", "?"))}</code>',
            esc((e.get("question") or "")[:200]),
        ])
    return table(["Escalation", "Status", "Raised by", "Question"], rows)


def panel_events(events):
    """The studio's ordered log. seq is the ordering the kernel guarantees, not at."""
    rows = []
    for _, e in sorted(events, key=lambda t: (t[1].get("seq") or 0, t[1].get("id", ""))):
        rows.append([
            f'<code>{esc(e.get("id", "?"))}</code>',
            f'<span class="dim">{esc(e.get("at", ""))}</span>',
            f'<code>{esc(e.get("type", "?"))}</code>',
            f'<code>{esc(e.get("actor", "?"))}</code>',
            f'<code>{esc(e.get("subject") or "&mdash;")}</code>',
            esc(e.get("summary") or ""),
        ])
    return table(["Event", "At", "Type", "Actor", "Subject", "Summary"], rows)


def panel_budget(budgets):
    ceiling = (budgets or {}).get("studioCeiling") or {}
    rows = []
    for b in (budgets or {}).get("budgets", []):
        rows.append([
            f'<strong>{esc(b.get("id", "?"))}</strong>',
            esc(b.get("division") or b.get("scope", "")),
            f'{esc(b.get("softWarn", "?"))} DKK',
            f'{esc(b.get("hardStop", "?"))} DKK',
            unknown("No spend records on disk. Actual spend is not tracked in the "
                    "repository yet, so this page cannot show it."),
        ])
    head = (f'<p class="big">{esc(ceiling.get("target", "?"))} <span class="dim">DKK target &middot; </span>'
            f'{esc(ceiling.get("hardStop", "?"))} <span class="dim">DKK hard stop &middot; per '
            f'{esc(ceiling.get("periodDays", "?"))} days</span></p>')
    return head + table(["Budget", "Scope", "Soft warn", "Hard stop", "Spent"], rows)


def panel_audit(verdicts, evidence):
    rows = []
    for path, v in sorted(verdicts, key=lambda t: t[1].get("evaluatedAt", ""), reverse=True):
        rows.append([
            f'<code>{esc(Path(path).name)}</code>',
            chip(v.get("verdict", "?"), "ok" if v.get("verdict") == "pass" else "bad"),
            f'<code>{esc(v.get("taskId", "?"))}</code>',
            f'<code>{esc(v.get("commit", "?"))}</code>',
            f'<code>{esc(v.get("evaluatedBy", "?"))}</code>',
            ", ".join(f'<code>{esc(e)}</code>' for e in (v.get("evidence") or [])),
        ])
    return (f'<p class="note">{len(verdicts)} gate verdict(s), {len(evidence)} evidence record(s) on disk.</p>'
            + table(["Record", "Verdict", "Order", "Commit", "Issued by", "Evidence"], rows))


# ------------------------------------------------------------------ page ---

# ---------------------------------------------------------------- identity ---
#
# JARVIS has its own visual identity and it is deliberately NOT derived from
# MergeSurvivor. JARVIS sits above the games: a second project with a completely
# different art direction must be able to appear in this interface without the
# interface looking wrong. So the language here is instrument panel, not game --
# near-black ground, one cyan accent, tight type, hairline rules. Nothing about it
# should ever be carried into a game's UI, and nothing from a game's UI belongs here.
#
# Dark only, on purpose. A product identity that flips wholesale with a system
# setting is two identities maintained badly; this commits to one.

ACCENT = "#5ee0d0"

# Small stroked glyphs, 16px grid, currentColor. Iconography earns its place in the
# rail by making sections findable by shape once the labels are familiar; it is not
# decoration, so there is exactly one per section and none anywhere else.
ICONS = {
    "overview": "M3 12h4l2-6 3 12 2-7 2 3h3",
    "blocked": "M8 2h6l4 4v6l-4 4H8l-4-4V6z M9 9l4 4 M13 9l-4 4",
    "gates": "M4 4v14 M18 4v14 M4 7h14 M4 11h14",
    "work": "M4 5h14v12H4z M4 9h14 M8 5v12",
    "agents": "M8 9a3 3 0 100-6 3 3 0 000 6z M2 19c0-3 3-5 6-5s6 2 6 5 M15 8h6 M18 5v6",
    "validation": "M3 11l4 4 8-9 M10 18h10",
    "decisions": "M11 3v6 M11 9L5 13v6 M11 9l6 4v6 M11 3h0",
    "questions": "M8 8a3 3 0 116 2c0 2-3 2-3 4 M11 18h.01",
    "proposals": "M5 3h8l5 5v13H5z M13 3v5h5 M8 13h7 M8 16h5",
    "escalations": "M11 3l9 16H2z M11 9v4 M11 16h.01",
    "events": "M4 6h16 M4 12h16 M4 18h16 M2 6h.01 M2 12h.01 M2 18h.01",
    "budget": "M3 6h18v12H3z M3 10h18 M7 14h4",
    "audit": "M10 3a7 7 0 100 14 7 7 0 000-14z M15 15l5 5",
}

CSS = """
:root{
--bg:#0a0c0f; --bg2:#0d1015; --panel:#11151b; --panel2:#151a21;
--fg:#e6eaef; --dim:#98a2b0; --faint:#697384;
--line:#1e242d; --line2:#28303b; --rule:#191f27;
--accent:#5ee0d0; --accent-dim:#2f7d75; --accent-bg:#0e2b28;
--ok:#4bd4a0; --ok-bg:#0d2620; --bad:#ff8a80; --bad-bg:#2c1416;
--warn:#f0c46a; --warn-bg:#2a2110; --info:#7fb4f5; --info-bg:#111e30;
--mut:#9aa5b3; --mut-bg:#171d25;
--r:8px; --rail:236px; --aside:280px;
--t:140ms cubic-bezier(.4,0,.2,1);
}
@media (prefers-reduced-motion:reduce){:root{--t:1ms}
*{animation:none!important;transition:none!important;scroll-behavior:auto!important}}

*{box-sizing:border-box}
html{color-scheme:dark}
body{margin:0;background:var(--bg);color:var(--fg);
font:14px/1.5 ui-sans-serif,system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;
font-variant-numeric:tabular-nums;-webkit-font-smoothing:antialiased;
text-rendering:optimizeLegibility}
:focus-visible{outline:2px solid var(--accent);outline-offset:2px;border-radius:4px}
/* Sections are focused programmatically so a screen reader announces the new view.
   That is a move cue, not a control, so it must not draw a focus ring around the
   whole panel -- which it did, framing the page in accent on every load. */
section:focus,section:focus-visible{outline:none}

/* The standard visually-hidden pattern, and the reason matters: the first version used
   left:-9999px, which extended the scrollable area and gave the phone layout a real
   horizontal overflow -- content clipped at the right edge on a 390px viewport. Clipping
   to a 1px box takes it out of layout entirely instead of parking it off-canvas. */
.skip{position:absolute;width:1px;height:1px;margin:-1px;padding:0;overflow:hidden;
clip:rect(0 0 0 0);clip-path:inset(50%);white-space:nowrap;border:0}
.skip:focus{position:fixed;left:10px;top:10px;width:auto;height:auto;margin:0;
padding:10px 16px;overflow:visible;clip:auto;clip-path:none;z-index:99;
background:var(--accent);color:#04231f;font-weight:700;border-radius:var(--r)}

/* ---------------------------------------------------------------- shell --- */
.app{display:grid;grid-template-columns:var(--rail) minmax(0,1fr);
grid-template-rows:auto minmax(0,1fr);min-height:100vh;
grid-template-areas:"brand top" "rail main"}

/* ---------------------------------------------------------------- brand --- */
.brand{grid-area:brand;display:flex;align-items:center;gap:10px;
padding:0 18px;height:56px;border-right:1px solid var(--line);
border-bottom:1px solid var(--line);background:var(--bg2)}
.mark{width:24px;height:24px;flex:none;border-radius:6px;
background:linear-gradient(150deg,var(--accent),var(--accent-dim));
display:grid;place-items:center;color:#04231f;font-weight:800;font-size:12px;
letter-spacing:-.02em}
.brand .word{font-size:14.5px;font-weight:700;letter-spacing:.16em}
.brand .word em{font-style:normal;color:var(--accent)}

/* ------------------------------------------------------------- topbar ---- */
.top{grid-area:top;display:flex;align-items:center;justify-content:space-between;
gap:16px;height:56px;padding:0 22px;border-bottom:1px solid var(--line);
background:var(--bg2);min-width:0}
.top .ctx{display:flex;align-items:baseline;gap:10px;min-width:0}
.top .ctx b{font-size:13.5px;font-weight:650;white-space:nowrap;overflow:hidden;
text-overflow:ellipsis}
.top .ctx span{color:var(--faint);font-size:12px;white-space:nowrap}
.sysbar{display:flex;align-items:center;gap:14px;flex:none}
.sysbar .kv{display:flex;align-items:center;gap:6px;font-size:11.5px;color:var(--faint);
white-space:nowrap}
.sysbar .kv b{color:var(--dim);font-weight:600;letter-spacing:.06em;
text-transform:uppercase;font-size:10px}

/* ---------------------------------------------------------------- rail --- */
.rail{grid-area:rail;border-right:1px solid var(--line);background:var(--bg2);
padding:14px 0 28px;overflow-y:auto;position:sticky;top:0;
max-height:calc(100vh - 56px)}
.grp{padding:0 12px;margin-bottom:14px}
.grp>h3{font-size:9.5px;text-transform:uppercase;letter-spacing:.16em;
color:var(--faint);margin:0 0 6px;padding:0 10px;font-weight:700}
.nav a{display:flex;align-items:center;gap:10px;padding:7px 10px;border-radius:6px;
color:var(--dim);text-decoration:none;font-size:13px;position:relative;
transition:background var(--t),color var(--t)}
.nav a svg{width:16px;height:16px;flex:none;stroke:currentColor;fill:none;
stroke-width:1.6;stroke-linecap:round;stroke-linejoin:round;opacity:.75}
.nav a .lbl{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;
white-space:nowrap}
.nav a .n{font-size:11px;color:var(--faint);font-weight:600;flex:none}
.nav a:hover{background:var(--panel2);color:var(--fg)}
.nav a:hover svg{opacity:1}
.nav a.on{background:var(--accent-bg);color:var(--accent);font-weight:600}
.nav a.on svg{opacity:1}
.nav a.on .n{color:var(--accent)}
.nav a.on::before{content:"";position:absolute;left:-12px;top:6px;bottom:6px;
width:2px;background:var(--accent);border-radius:0 2px 2px 0}

/* ---------------------------------------------------------------- main --- */
.main{grid-area:main;display:grid;grid-template-columns:minmax(0,1fr);min-width:0}
@media (min-width:1340px){.main{grid-template-columns:minmax(0,1fr) var(--aside)}}
.work{padding:24px 26px 96px;min-width:0}
h2{font-size:16px;font-weight:650;letter-spacing:-.01em;margin:0 0 4px}
section{display:block;margin-bottom:38px;scroll-margin-top:70px}
.js section{display:none;margin-bottom:0}
.js section.on{display:block;animation:in var(--t) both}
@keyframes in{from{opacity:0;transform:translateY(3px)}to{opacity:1;transform:none}}
.note{color:var(--dim);font-size:12.5px;margin:0 0 16px;max-width:80ch;line-height:1.6}
.empty{color:var(--faint);font-style:italic;padding:14px 0}
.big{font-size:30px;font-weight:680;letter-spacing:-.025em;margin:0 0 18px}
.big .dim{font-size:13px;font-weight:400;letter-spacing:0}
.dim{color:var(--dim)}

/* --------------------------------------------------------------- aside --- */
.aside{display:none;border-left:1px solid var(--line);background:var(--bg2);
padding:22px 20px 60px;font-size:12.5px}
@media (min-width:1340px){.aside{display:block}}
.aside h3{font-size:9.5px;text-transform:uppercase;letter-spacing:.16em;
color:var(--faint);margin:0 0 10px;font-weight:700}
.aside+.aside,.aside .blk{margin-bottom:26px}
.aside dl{margin:0;display:grid;grid-template-columns:1fr auto;gap:6px 10px}
.aside dt{color:var(--faint);font-size:12px}
.aside dd{margin:0;color:var(--dim);font-variant-numeric:tabular-nums}
.aside p{color:var(--faint);line-height:1.6;margin:0 0 10px}

/* --------------------------------------------------------------- table --- */
/* overflow-x:auto, not hidden. Hidden was rounder at the corners and it silently ate
   data: at 1500px the evidence table measured 1070px inside a 915px box, so 155px of
   every Summary was invisible with no scrollbar to reveal it -- the failure mode this
   page least tolerates, since a truncated record still reads as the whole record. */
.scroll{border:1px solid var(--line);border-radius:var(--r);background:var(--panel);
overflow-x:auto}
table{border-collapse:collapse;width:100%;font-size:12.5px}
/* Long identifiers -- RejectsAbsorbingDeltaTimeThatWouldNeverAdvanceTheSchedule and its
   kind -- set a min-content width wider than the column, which is what made the table
   overflow in the first place. Breaking them keeps the scrollbar a fallback rather than
   the normal case. */
td{overflow-wrap:anywhere}
th{text-align:left;font-size:10px;text-transform:uppercase;letter-spacing:.09em;
color:var(--faint);font-weight:700;padding:10px 14px;background:var(--panel2);
border-bottom:1px solid var(--line);white-space:nowrap}
td{padding:11px 14px;border-bottom:1px solid var(--rule);vertical-align:top;
line-height:1.5}
tbody tr:last-child td{border-bottom:none}
tbody tr{transition:background var(--t)}
tbody tr:hover{background:var(--panel2)}
code{font:11.5px/1.4 ui-monospace,SFMono-Regular,Menlo,monospace;
background:var(--mut-bg);border:1px solid var(--line);padding:1px 5px;
border-radius:4px;color:var(--dim);white-space:nowrap}

/* --------------------------------------------------------------- chips --- */
.chip{display:inline-block;font-size:10px;font-weight:700;text-transform:uppercase;
letter-spacing:.07em;padding:3px 8px;border-radius:5px;white-space:nowrap;
border:1px solid transparent}
.chip.ok{color:var(--ok);background:var(--ok-bg);border-color:#1c4a3c}
.chip.bad{color:var(--bad);background:var(--bad-bg);border-color:#5a2528}
.chip.warn{color:var(--warn);background:var(--warn-bg);border-color:#4a3a14}
.chip.info{color:var(--info);background:var(--info-bg);border-color:#1e3550}
.chip.neutral{color:var(--mut);background:var(--mut-bg);border-color:var(--line2)}
/* The four honesty states read as one family and stay quieter than a verdict: they
   describe how much is known, not whether it is good. UNAVAILABLE and UNKNOWN keep
   the dashed edge that marks "this is not a result". */
.chip.state-real{color:var(--ok);background:var(--ok-bg);border-color:#1c4a3c}
.chip.state-pending{color:var(--warn);background:var(--warn-bg);border-color:#4a3a14}
.chip.state-unavailable,.chip.state-unknown{color:var(--mut);background:transparent;
border:1px dashed var(--line2);cursor:help}
.chip.state-unknown{color:var(--faint)}

/* ---------------------------------------------------------------- misc --- */
ol.queue{list-style:none;padding:0;margin:0;counter-reset:q}
ol.queue li{counter-increment:q;position:relative;padding:14px 18px 14px 46px;
background:var(--panel);border:1px solid var(--line);border-radius:var(--r);
margin-bottom:8px;line-height:1.55}
ol.queue li::before{content:counter(q,decimal-leading-zero);position:absolute;
left:16px;top:14px;color:var(--accent);font-weight:700;font-size:11px;
font-family:ui-monospace,monospace}
ul{margin:0 0 14px;padding-left:18px}
ul li{margin-bottom:8px;line-height:1.55}
ul.legend{list-style:none;padding:14px 16px;display:grid;gap:10px;margin:0 0 20px;
background:var(--panel);border:1px solid var(--line);border-radius:var(--r);
grid-template-columns:repeat(auto-fit,minmax(230px,1fr))}
ul.legend li{display:flex;align-items:center;gap:10px;margin:0;font-size:12px;
color:var(--dim)}
.aside ul.legend{grid-template-columns:1fr;background:none;border:none;padding:0}
.banner{border:1px solid var(--line);border-left:2px solid var(--accent);
background:var(--panel);padding:14px 18px;border-radius:0 var(--r) var(--r) 0;
margin:0 0 24px;font-size:12.5px;color:var(--dim);max-width:84ch;line-height:1.6}
footer{margin-top:40px;padding-top:16px;border-top:1px solid var(--rule);
color:var(--faint);font-size:11.5px;line-height:1.7}

/* ------------------------------------------------------- mobile nav ------ */
.menubtn,.sheet,.sheetwrap{display:none}

@media (max-width:1023px){
/* The status row is kept, not dropped. Which commit the page describes, and whether
   G2 and G3 are known, are the facts that make everything below meaningful; a phone
   layout that silently discards them is showing the same data with less truth. It
   moves under the brand and wraps instead of competing for one line. */
.app{grid-template-columns:minmax(0,1fr);grid-template-rows:auto auto minmax(0,1fr);
grid-template-areas:"brand" "top" "main"}
.brand{border-right:none;position:sticky;top:0;z-index:20;justify-content:flex-start;
height:52px}
.top{height:auto;padding:10px 16px 12px;flex-wrap:wrap;gap:8px 14px;align-items:center}
.top .ctx{flex:1 1 100%;gap:8px;flex-wrap:wrap}
.top .ctx b{white-space:normal;font-size:13px}
/* The branch name is longer than a phone is wide, and nowrap on it pushed the
   document 2px past the viewport. Nothing else on the page overflowed; a single
   unbreakable token is all it takes. */
.top .ctx span{white-space:normal}
.top .ctx code{white-space:normal;word-break:break-all}
.sysbar{flex-wrap:wrap;gap:8px 14px;flex:1 1 100%}
.rail{display:none}
.main{grid-template-columns:minmax(0,1fr)}
.work{padding:18px 16px 108px}
.big{font-size:26px}
section{scroll-margin-top:64px}

/* Sections become stacked records. The header row goes away and each cell carries
   its own label, so nothing is dropped and nothing scrolls sideways. */
.scroll{border:none;background:none;border-radius:0;overflow:visible}
table,thead,tbody,tr,td{display:block;width:100%}
thead{position:absolute;left:-9999px}
tbody tr{background:var(--panel);border:1px solid var(--line);
border-radius:var(--r);margin-bottom:10px;padding:4px 0}
tbody tr:hover{background:var(--panel)}
td{border-bottom:1px solid var(--rule);padding:9px 14px;display:grid;
grid-template-columns:minmax(84px,34%) minmax(0,1fr);gap:12px;align-items:baseline}
tbody tr td:last-child{border-bottom:none}
td::before{content:attr(data-label);color:var(--faint);font-size:9.5px;
text-transform:uppercase;letter-spacing:.09em;font-weight:700;line-height:1.7}
td code{white-space:normal;word-break:break-all}

/* Thumb-reachable. The sheet opens from the bottom because the top of a modern
   phone is the part of the screen a hand holding it cannot comfortably reach. */
.menubtn{display:flex;align-items:center;gap:10px;position:fixed;left:12px;right:12px;
bottom:12px;z-index:40;height:52px;padding:0 16px;border-radius:12px;
background:var(--panel2);border:1px solid var(--line2);color:var(--fg);
font:inherit;font-size:13.5px;font-weight:600;cursor:pointer;
box-shadow:0 6px 24px rgba(0,0,0,.55)}
.menubtn .cur{flex:1;text-align:left;overflow:hidden;text-overflow:ellipsis;
white-space:nowrap}
.menubtn .bars{width:16px;height:16px;stroke:var(--accent);stroke-width:1.8;fill:none;
stroke-linecap:round}
.menubtn .cnt{color:var(--faint);font-size:11px;font-weight:600}
.sheetwrap{display:block;position:fixed;inset:0;z-index:50;visibility:hidden}
.sheetwrap.open{visibility:visible}
.scrim{position:absolute;inset:0;background:rgba(0,0,0,.6);opacity:0;
transition:opacity var(--t)}
.sheetwrap.open .scrim{opacity:1}
.sheet{display:block;position:absolute;left:0;right:0;bottom:0;max-height:82vh;
overflow-y:auto;background:var(--bg2);border-top:1px solid var(--line2);
border-radius:16px 16px 0 0;padding:8px 12px calc(16px + env(safe-area-inset-bottom));
transform:translateY(100%);transition:transform var(--t)}
.sheetwrap.open .sheet{transform:none}
.sheet .hnd{width:36px;height:4px;border-radius:2px;background:var(--line2);
margin:8px auto 12px}
.sheet .grp{padding:0;margin-bottom:12px}
.sheet a{padding:12px 12px;font-size:14px;min-height:46px}
.sheet a.on::before{display:none}
}

@media (max-width:1023px) and (orientation:landscape){
.sheet{max-height:88vh}
.sheet .grp{display:grid;grid-template-columns:1fr 1fr;gap:0 8px}
.sheet .grp>h3{grid-column:1/-1}
}

@media (min-width:1024px){.sheetwrap,.menubtn{display:none!important}}
"""

# Progressive enhancement, and it is load-bearing. An earlier version hid every
# section by default and revealed one from script, so with JS off the page rendered
# blank. Sections are visible by default; the script opts into switching by setting
# .js on the root. Everything below degrades to a long, complete document.
NAV_JS = """
(function(){
  var root=document.documentElement;
  var links=[].slice.call(document.querySelectorAll('a[data-go]'));
  var secs=[].slice.call(document.querySelectorAll('section[id]'));
  if(!links.length||!secs.length)return;
  root.classList.add('js');

  var wrap=document.querySelector('.sheetwrap');
  var btn=document.querySelector('.menubtn');
  var cur=document.querySelector('.menubtn .cur');

  function closeSheet(){
    if(!wrap)return;
    wrap.classList.remove('open');
    if(btn)btn.setAttribute('aria-expanded','false');
  }
  function openSheet(){
    if(!wrap)return;
    wrap.classList.add('open');
    if(btn)btn.setAttribute('aria-expanded','true');
    var on=wrap.querySelector('a.on');if(on)on.focus();
  }

  function show(id,focus){
    var found=false;
    secs.forEach(function(s){
      var m=s.id===id;s.classList.toggle('on',m);if(m)found=true;
    });
    if(!found)return false;
    links.forEach(function(a){
      var m=a.getAttribute('data-go')===id;
      a.classList.toggle('on',m);
      if(m){a.setAttribute('aria-current','page');
            if(cur)cur.textContent=a.getAttribute('data-label')||id;}
      else a.removeAttribute('aria-current');
    });
    try{history.replaceState(null,'','#'+id)}catch(e){}
    if(focus){var s=document.getElementById(id);if(s)s.focus({preventScroll:true});
              window.scrollTo(0,0);}
    return true;
  }

  links.forEach(function(a){
    a.addEventListener('click',function(e){
      e.preventDefault();
      show(a.getAttribute('data-go'),true);
      closeSheet();
    });
  });

  if(btn&&wrap){
    btn.addEventListener('click',function(){
      wrap.classList.contains('open')?closeSheet():openSheet();
    });
    var scrim=wrap.querySelector('.scrim');
    if(scrim)scrim.addEventListener('click',closeSheet);
  }
  document.addEventListener('keydown',function(e){
    if(e.key==='Escape')closeSheet();
  });
  window.addEventListener('hashchange',function(){
    show((location.hash||'').replace('#',''),false);
  });

  var start=(location.hash||'').replace('#','');
  if(!show(start,false))show(secs[0].id,false);
})();
"""


def icon(sid):
    """One glyph per section. A single path can carry several subpaths, so the whole
    entry goes in verbatim; a missing entry renders nothing rather than a placeholder."""
    d = ICONS.get(sid)
    if not d:
        return ""
    return f'<svg viewBox="0 0 22 22" aria-hidden="true" focusable="false"><path d="{esc(d)}"/></svg>'


def build():
    state = read_json(ROOT / "Studio/state/project-state.json")
    gates = read_json(ROOT / "Studio/constitution/gates.json")
    budgets = read_json(ROOT / "Studio/constitution/budgets.json")
    agent_files = glob_json("Studio/constitution/agents")
    orders = glob_json("Studio/orders")
    verdicts = glob_json("Studio/state/verdicts")
    challenges = glob_json("Studio/state/challenges")
    rulings = glob_json("Studio/state/rulings")
    evidence = glob_json("Studio/evidence")
    decisions = glob_json("Studio/decisions")
    proposals = glob_json("Studio/state/proposals")
    escalations = glob_json("Studio/state/escalations")
    events = glob_json("Studio/state/events")

    commit = git("rev-parse", "--short=7", "HEAD", default="unknown")
    branch = git("rev-parse", "--abbrev-ref", "HEAD", default="unknown")
    dirty = bool(git("status", "--porcelain"))
    now = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%SZ")

    phase = (state or {}).get("phase") or {}
    queue = (state or {}).get("humanQueue") or []
    open_proposals = [p for _, p in proposals if p.get("status") == "open"]
    not_ready = [r for _, r in rulings if (r.get("gateReadiness") or {}).get("state") == "not_ready"]
    open_challenges = [c for _, c in challenges if c.get("status") == "open"]

    # (id, rail label, count badge, title, note, body). One list drives both the rail and
    # the sections, so a panel cannot exist without navigation to it, or the reverse.
    #
    # GROUPS is the reading order of the studio itself: what needs a person, what is
    # being built, who is building it, and what the studio has written down. Thirteen
    # flat entries is a list; four groups of three is a control surface.
    GROUPS = [
        ("Command", ["overview", "blocked"]),
        ("Delivery", ["work", "gates", "validation"]),
        ("Organisation", ["agents", "budget"]),
        ("Record", ["decisions", "questions", "proposals", "escalations", "events", "audit"]),
    ]
    panels = [
        ("overview", "Overview", len(queue), "Waiting on the founder",
         "Nobody else in the studio can clear these. Everything below is read from records "
         "in this checkout; the legend says how far each claim reaches.",
         legend() + panel_founder_queue(state)),
        ("blocked", "Blocked", None, "Blocked", None, panel_blocked(state)),
        ("gates", "Gates", None, "Gate ladder",
         "Status is the latest gate-verdict record on disk. It is not a live CI result, and "
         "the two are not the same claim.", panel_gates(gates, verdicts)),
        ("work", "Work orders", len(orders), "Work orders", None, panel_orders(orders, verdicts)),
        ("agents", "Agents", None, "The hundred agents", None,
         panel_agents(agent_files) + '<h2 style="margin-top:26px">Per-agent state</h2>'
         + panel_agent_state(state)),
        ("validation", "Test &amp; validation", len(evidence), "Test and validation", None,
         panel_validation(evidence)),
        ("decisions", "Decisions", len(decisions), "Architecture decisions", None,
         panel_decisions(decisions)),
        ("questions", "Open questions", len(open_challenges) + len(not_ready), "Open questions",
         "Challenges still open and rulings that withheld a gate.",
         panel_open_questions(challenges, rulings)),
        ("proposals", "Proposals", len(open_proposals), "Proposals", None, panel_proposals(proposals)),
        ("escalations", "Escalations", len(escalations), "Escalations", None,
         panel_escalations(escalations)),
        ("events", "Events", len(events), "Event log", None, panel_events(events)),
        ("budget", "Budget", None, "Budget", None, panel_budget(budgets)),
        ("audit", "Audit trail", len(verdicts), "Audit trail", None, panel_audit(verdicts, evidence)),
    ]

    by_id = {p[0]: p for p in panels}
    grouped = [(g, [by_id[s] for s in ids if s in by_id]) for g, ids in GROUPS]

    # Every panel must appear in exactly one group, or the rail silently loses a
    # section. check() enforces nav/section agreement, but failing here names the cause.
    placed = {s for _, ids in GROUPS for s in ids}
    missing = [p[0] for p in panels if p[0] not in placed]
    if missing:
        raise SystemExit(f"panels missing from GROUPS: {missing}")

    def nav(extra_class=""):
        out = ""
        for gname, entries in grouped:
            links = ""
            for sid, label, count, _, _, _ in entries:
                n = f'<span class="n">{count}</span>' if count is not None else ""
                links += (f'<a href="#{sid}" data-go="{sid}" data-label="{esc(label)}">'
                          f'{icon(sid)}<span class="lbl">{label}</span>{n}</a>')
            out += f'<div class="grp{extra_class}"><h3>{esc(gname)}</h3>{links}</div>'
        return out

    body = "".join(section(sid, title, panel_body, note)
                   for sid, _, _, title, note, panel_body in panels)

    # Global status in the top bar: the two gates whose state a reader asks about first,
    # each shown as what it actually is. G2 has a real recorded verdict; G3's live result
    # is in Actions and no checkout can read it, so it says so rather than staying blank.
    g2 = [v for _, v in verdicts if v.get("gate") == "G2"]
    g2 = max(g2, key=lambda v: v.get("evaluatedAt", ""), default=None)
    g2_cell = (chip(g2["verdict"], "ok" if g2["verdict"] == "pass" else "bad")
               if g2 else unknown("No G2 verdict record on disk."))
    g3_cell = unavailable("G3 runs in CI. Its result lives in GitHub Actions and cannot "
                          "be read from a checkout.")

    brand = ('<div class="brand"><span class="mark">J</span>'
             '<span class="word">JARV<em>I</em>S</span></div>')

    parts = [
        "<title>JARVIS &middot; Studio Operations</title>",
        '<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">',
        f'<meta name="theme-color" content="{ACCENT}">',
        f"<style>{CSS}</style>",
        '<a class="skip" href="#overview">Skip to content</a>',
        '<div class="app">',
        brand,
        '<div class="top">',
        f'<div class="ctx"><b>Phase {esc(phase.get("current", "?"))} &mdash; '
        f'{esc(phase.get("name", "?"))}</b>'
        f'<span><code>{esc(branch)}</code> @ <code>{esc(commit)}</code></span>'
        + (' <span class="chip warn">tree dirty</span>' if dirty else "") + "</div>",
        f'<div class="sysbar"><span class="kv"><b>G2</b>{g2_cell}</span>'
        f'<span class="kv"><b>G3</b>{g3_cell}</span>'
        f'<span class="kv"><b>Generated</b>{esc(now)}</span></div>',
        "</div>",
        f'<nav class="rail nav" aria-label="Sections">{nav()}</nav>',
        '<div class="main"><div class="work" id="work-root">',
        '<div class="banner">Generated from the files in this checkout and nothing else. '
        'It reads only &mdash; it can change no record &mdash; and it never guesses: every '
        'claim carries one of four states, and anything it cannot establish from disk says '
        'so rather than showing a colour it has not earned.</div>',
        body,
        f'<footer>Regenerate with <code>python3 Studio/jarvis/build-jarvis.py</code> '
        f'(<code>--check</code> validates the output). Reads {len(orders)} order(s), '
        f'{len(verdicts)} verdict(s), {len(rulings)} ruling(s), {len(evidence)} evidence '
        f'record(s), {len(decisions)} decision(s), {len(proposals)} proposal(s), '
        f'{len(events)} event(s), {len(agent_files)} division file(s).</footer>',
        "</div>",
        # The context aside earns its width by carrying what the page cannot know and
        # where its numbers came from -- the two questions a reader of a dashboard should
        # always be able to answer. It is not a place for decoration.
        '<aside class="aside">',
        '<div class="blk"><h3>Claim states</h3>' + legend() + "</div>",
        '<div class="blk"><h3>Not knowable here</h3>'
        '<p>Live CI status. G3 runs in GitHub Actions and does not commit back.</p>'
        '<p>Actual spend. No spend records exist in the repository yet.</p></div>',
        '<div class="blk"><h3>Records read</h3><dl>'
        f'<dt>Work orders</dt><dd>{len(orders)}</dd>'
        f'<dt>Gate verdicts</dt><dd>{len(verdicts)}</dd>'
        f'<dt>Evidence</dt><dd>{len(evidence)}</dd>'
        f'<dt>Rulings</dt><dd>{len(rulings)}</dd>'
        f'<dt>Decisions</dt><dd>{len(decisions)}</dd>'
        f'<dt>Proposals</dt><dd>{len(proposals)}</dd>'
        f'<dt>Events</dt><dd>{len(events)}</dd>'
        "</dl></div></aside>",
        "</div>",
        "</div>",
        # Mobile: a thumb-reachable bar naming the current section, opening a sheet.
        '<button class="menubtn" type="button" aria-expanded="false" aria-controls="sheet">'
        '<svg class="bars" viewBox="0 0 16 16" aria-hidden="true"><path d="M2 4h12"/>'
        '<path d="M2 8h12"/><path d="M2 12h12"/></svg>'
        '<span class="cur">Overview</span><span class="cnt">Sections</span></button>',
        '<div class="sheetwrap"><div class="scrim"></div>'
        '<nav class="sheet nav" id="sheet" aria-label="Sections">'
        f'<div class="hnd"></div>{nav()}</nav></div>',
        f"<script>{NAV_JS}</script>",
    ]

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(parts), encoding="utf-8")
    return OUT, [p[0] for p in panels]


def check(path, expected_sections):
    """
    Validate the page this script just wrote.

    Not decoration. Rule 2 is a claim about every cell on the page, and a claim that
    large needs something enforcing it -- otherwise the next panel added quietly renders
    a status outside the vocabulary and the honesty rule becomes a comment rather than a
    property. This is also what lets "JARVIS works" be said without hand-waving.
    """
    from html.parser import HTMLParser

    html_text = path.read_text(encoding="utf-8")
    problems = []
    seen_sections, seen_navs, chip_classes = set(), set(), set()

    class Reader(HTMLParser):
        def handle_starttag(self, tag, attrs):
            a = dict(attrs)
            if tag == "section" and a.get("id"):
                seen_sections.add(a["id"])
            if tag == "a" and a.get("data-go"):
                seen_navs.add(a["data-go"])
            if tag == "span" and "chip" in (a.get("class") or "").split():
                chip_classes.update(c for c in a["class"].split() if c != "chip")

    Reader().feed(html_text)

    missing = [s for s in expected_sections if s not in seen_sections]
    if missing:
        problems.append(f"sections missing from the page: {', '.join(missing)}")

    # Every section must be reachable, and every nav entry must lead somewhere.
    if seen_navs != seen_sections:
        problems.append(
            f"navigation and sections disagree: nav-only {sorted(seen_navs - seen_sections)}, "
            f"section-only {sorted(seen_sections - seen_navs)}")

    allowed = {"ok", "bad", "warn", "info", "neutral"} | {f"state-{k}" for k in STATES}
    stray = chip_classes - allowed
    if stray:
        problems.append(
            f"status chips outside the vocabulary: {sorted(stray)}. Every status must be "
            f"one of {sorted(allowed)} -- see rule 2.")

    if not problems:
        print(f"check: OK -- {len(seen_sections)} sections, all reachable, "
              f"{len(chip_classes)} chip kinds all in vocabulary")
        return 0

    for p in problems:
        print(f"check: FAIL -- {p}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    path, sections = build()
    print(path.relative_to(ROOT).as_posix())
    if "--check" in sys.argv:
        sys.exit(check(path, sections))
