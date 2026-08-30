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


def glob_json(relative_dir, pattern="*.json"):
    d = ROOT / relative_dir
    if not d.is_dir():
        return []
    out = []
    for p in sorted(d.rglob(pattern)):
        doc = read_json(p)
        if doc is not None:
            out.append((p.relative_to(ROOT).as_posix(), doc))
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


def unknown(why):
    """The honest cell. Rule 2 above is enforced by using this and not a colour."""
    return f'<span class="chip unknown" title="{esc(why)}">UNKNOWN</span>'


def section(title, body, note=None):
    n = f'<p class="note">{note}</p>' if note else ""
    return f"<section><h2>{esc(title)}</h2>{n}{body}</section>"


def table(headers, rows):
    if not rows:
        return '<p class="empty">Nothing recorded.</p>'
    head = "".join(f"<th>{esc(h)}</th>" for h in headers)
    body = "".join("<tr>" + "".join(f"<td>{c}</td>" for c in r) + "</tr>" for r in rows)
    return f'<div class="scroll"><table><thead><tr>{head}</tr></thead><tbody>{body}</tbody></table></div>'


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
            status = unknown("No gate-verdict record on disk for this gate. "
                             "CI results are not readable from a checkout.")
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

CSS = """
:root{--bg:#fbfaf8;--fg:#1a1a1a;--dim:#6b6b6b;--line:#e2e0dc;--card:#fff;
--ok:#136f4a;--okbg:#e2f3ea;--bad:#9b1c1c;--badbg:#fdeaea;--warn:#8a5a00;--warnbg:#fdf2dc;
--info:#1e4f8a;--infobg:#e6effa;--unk:#5b5b5b;--unkbg:#eeecea;--accent:#7c3aed}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){
--bg:#141414;--fg:#ececec;--dim:#9a9a9a;--line:#2e2e2e;--card:#1c1c1c;
--ok:#6ee7a8;--okbg:#12301f;--bad:#fca5a5;--badbg:#3a1414;--warn:#fcd34d;--warnbg:#332405;
--info:#93c5fd;--infobg:#122238;--unk:#b0b0b0;--unkbg:#262626;--accent:#c4b5fd}}
:root[data-theme="dark"]{--bg:#141414;--fg:#ececec;--dim:#9a9a9a;--line:#2e2e2e;--card:#1c1c1c;
--ok:#6ee7a8;--okbg:#12301f;--bad:#fca5a5;--badbg:#3a1414;--warn:#fcd34d;--warnbg:#332405;
--info:#93c5fd;--infobg:#122238;--unk:#b0b0b0;--unkbg:#262626;--accent:#c4b5fd}
body{background:var(--bg);color:var(--fg);font:14px/1.55 ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif;
margin:0;padding:28px 22px 80px;max-width:1180px;margin-inline:auto}
header{border-bottom:2px solid var(--fg);padding-bottom:14px;margin-bottom:8px}
h1{font-size:26px;margin:0 0 4px;letter-spacing:-.02em}
h1 span{color:var(--accent)}
h2{font-size:15px;text-transform:uppercase;letter-spacing:.09em;margin:34px 0 10px;
padding-bottom:6px;border-bottom:1px solid var(--line)}
.meta{color:var(--dim);font-size:12.5px}
.meta code{color:var(--fg)}
section{margin-bottom:6px}
.note{color:var(--dim);font-size:12.5px;margin:0 0 10px}
.empty{color:var(--dim);font-style:italic}
.big{font-size:22px;margin:6px 0 14px;font-weight:600}
.big .dim{font-size:14px;font-weight:400}
.dim{color:var(--dim)}
.scroll{overflow-x:auto;border:1px solid var(--line);border-radius:8px;background:var(--card)}
table{border-collapse:collapse;width:100%;font-size:13px}
th{text-align:left;font-weight:600;font-size:11px;text-transform:uppercase;letter-spacing:.06em;
color:var(--dim);padding:9px 12px;border-bottom:1px solid var(--line);white-space:nowrap}
td{padding:9px 12px;border-bottom:1px solid var(--line);vertical-align:top}
tr:last-child td{border-bottom:none}
code{font:12px ui-monospace,SFMono-Regular,Menlo,monospace;background:var(--unkbg);
padding:1px 5px;border-radius:4px}
.chip{display:inline-block;font-size:11px;font-weight:600;text-transform:uppercase;
letter-spacing:.05em;padding:2px 8px;border-radius:999px;white-space:nowrap}
.chip.ok{color:var(--ok);background:var(--okbg)}
.chip.bad{color:var(--bad);background:var(--badbg)}
.chip.warn{color:var(--warn);background:var(--warnbg)}
.chip.info{color:var(--info);background:var(--infobg)}
.chip.unknown,.chip.neutral{color:var(--unk);background:var(--unkbg)}
.chip.unknown{cursor:help;border:1px dashed currentColor}
ol.queue{padding-left:20px;margin:0}
ol.queue li{margin-bottom:9px;padding-left:4px}
ul{margin:0 0 10px;padding-left:20px}
ul li{margin-bottom:6px}
.banner{border-left:3px solid var(--accent);background:var(--card);padding:12px 16px;
border-radius:0 8px 8px 0;margin:18px 0;font-size:13px;color:var(--dim)}
footer{margin-top:48px;padding-top:14px;border-top:1px solid var(--line);
color:var(--dim);font-size:12px}
"""


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

    commit = git("rev-parse", "--short=7", "HEAD", default="unknown")
    branch = git("rev-parse", "--abbrev-ref", "HEAD", default="unknown")
    dirty = bool(git("status", "--porcelain"))
    now = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%SZ")

    phase = (state or {}).get("phase") or {}

    parts = [
        f"<title>JARVIS &middot; MergeSurvivor Studio</title><style>{CSS}</style>",
        "<header>",
        "<h1>JARVIS <span>&middot;</span> studio cockpit</h1>",
        f'<p class="meta">Phase {esc(phase.get("current", "?"))} &mdash; {esc(phase.get("name", "?"))} '
        f'&middot; <code>{esc(branch)}</code> at <code>{esc(commit)}</code>'
        + (' &middot; <span class="chip warn">working tree dirty</span>' if dirty else "")
        + f' &middot; generated {esc(now)}</p>',
        "</header>",
        '<div class="banner">This page is generated from the files in this checkout and '
        'nothing else. It cannot see CI, and it never guesses: anything it cannot establish '
        'from disk is marked <span class="chip unknown">UNKNOWN</span> with the reason on hover. '
        'It reads only &mdash; it can change no record.</div>',
        section("Waiting on the founder", panel_founder_queue(state),
                "Nobody else in the studio can clear these."),
        section("Blocked", panel_blocked(state)),
        section("Gates", panel_gates(gates, verdicts),
                "Status is the latest gate-verdict record on disk, not a live CI result."),
        section("Work orders", panel_orders(orders, verdicts)),
        section("The hundred agents", panel_agents(agent_files)),
        section("Open questions", panel_open_questions(challenges, rulings),
                "Challenges still open and rulings that withheld a gate."),
        section("Budget", panel_budget(budgets)),
        section("Audit trail", panel_audit(verdicts, evidence)),
        f'<footer>Regenerate with <code>python3 Studio/jarvis/build-jarvis.py</code>. '
        f'Reads {len(orders)} order(s), {len(verdicts)} verdict(s), {len(rulings)} ruling(s), '
        f'{len(evidence)} evidence record(s), {len(agent_files)} division file(s).</footer>',
    ]

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(parts), encoding="utf-8")
    return OUT


if __name__ == "__main__":
    path = build()
    print(path.relative_to(ROOT).as_posix())
