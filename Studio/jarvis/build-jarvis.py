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
    return f'<section id="{esc(sid)}"><h2>{esc(title)}</h2>{n}{body}</section>'


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

CSS = """
:root{--bg:#f7f6f4;--panel:#fff;--fg:#16181d;--dim:#6a6f7a;--faint:#9aa0ab;
--line:#e4e2de;--rule:#efedea;
--ok:#0f6b45;--okbg:#e4f2ea;--bad:#9c1c1c;--badbg:#fceceb;--warn:#87550a;--warnbg:#fbf1dd;
--info:#1c4f86;--infobg:#e8eff8;--mut:#5a5f69;--mutbg:#edebe8;
--accent:#4f46e5;--accentdim:#6d67e8}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){
--bg:#0e0f12;--panel:#16181d;--fg:#e9eaec;--dim:#9096a1;--faint:#6b7280;
--line:#25282f;--rule:#1e2127;
--ok:#5fd6a0;--okbg:#0f2a1e;--bad:#f4a3a0;--badbg:#331416;--warn:#e8bd5e;--warnbg:#2c2208;
--info:#8bbcf5;--infobg:#111f33;--mut:#a3a8b2;--mutbg:#212429;
--accent:#a5a0fb;--accentdim:#8b85f5}}
:root[data-theme="dark"]{--bg:#0e0f12;--panel:#16181d;--fg:#e9eaec;--dim:#9096a1;--faint:#6b7280;
--line:#25282f;--rule:#1e2127;
--ok:#5fd6a0;--okbg:#0f2a1e;--bad:#f4a3a0;--badbg:#331416;--warn:#e8bd5e;--warnbg:#2c2208;
--info:#8bbcf5;--infobg:#111f33;--mut:#a3a8b2;--mutbg:#212429;
--accent:#a5a0fb;--accentdim:#8b85f5}

*{box-sizing:border-box}
body{background:var(--bg);color:var(--fg);margin:0;
font:14px/1.55 ui-sans-serif,system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;
font-variant-numeric:tabular-nums;-webkit-font-smoothing:antialiased}
.shell{display:grid;grid-template-columns:216px minmax(0,1fr);gap:0;min-height:100vh;
max-width:1400px;margin-inline:auto}

/* --- rail --------------------------------------------------------------- */
.rail{border-right:1px solid var(--line);padding:26px 0 40px;position:sticky;top:0;
height:100vh;overflow-y:auto;background:var(--bg)}
.brand{padding:0 20px 20px;border-bottom:1px solid var(--rule);margin-bottom:14px}
.brand b{display:block;font-size:16px;letter-spacing:.14em;font-weight:600}
.brand b i{font-style:normal;color:var(--accent)}
.brand span{display:block;color:var(--faint);font-size:10.5px;letter-spacing:.1em;
text-transform:uppercase;margin-top:5px}
.rail nav{display:flex;flex-direction:column;padding:0 10px}
.rail a{display:flex;justify-content:space-between;align-items:center;gap:8px;
padding:7px 10px;border-radius:6px;color:var(--dim);text-decoration:none;font-size:13px;
border-left:2px solid transparent}
.rail a:hover{background:var(--mutbg);color:var(--fg)}
.rail a.on{background:var(--mutbg);color:var(--fg);font-weight:600;border-left-color:var(--accent)}
.rail a .n{font-size:11px;color:var(--faint);font-variant-numeric:tabular-nums}
.rail a.on .n{color:var(--accent)}

/* --- main --------------------------------------------------------------- */
main{padding:26px 30px 90px;min-width:0}
header{margin-bottom:20px}
h1{font-size:19px;margin:0 0 6px;letter-spacing:-.01em;font-weight:650}
.meta{color:var(--dim);font-size:12.5px}
.meta code{color:var(--fg)}
h2{font-size:12px;text-transform:uppercase;letter-spacing:.1em;color:var(--dim);
margin:0 0 12px;font-weight:650}
/* Progressive enhancement, and it is load-bearing rather than principle: the first
   version hid every section by default and revealed one from script, so with JS off the
   page rendered completely blank. Sections are visible by default and the script opts
   into switching by setting .js on the root. */
section{display:block;margin-bottom:34px}
.js section{display:none;margin-bottom:0}
.js section.on{display:block}
.note{color:var(--dim);font-size:12.5px;margin:0 0 12px;max-width:78ch}
.empty{color:var(--faint);font-style:italic}
.big{font-size:26px;margin:0 0 16px;font-weight:650;letter-spacing:-.02em}
.big .dim{font-size:13px;font-weight:400;letter-spacing:0}
.dim{color:var(--dim)}

.scroll{overflow-x:auto;border:1px solid var(--line);border-radius:10px;background:var(--panel)}
table{border-collapse:collapse;width:100%;font-size:13px}
th{text-align:left;font-weight:600;font-size:10.5px;text-transform:uppercase;
letter-spacing:.07em;color:var(--faint);padding:10px 14px;
border-bottom:1px solid var(--line);white-space:nowrap;background:var(--panel)}
td{padding:10px 14px;border-bottom:1px solid var(--rule);vertical-align:top}
tbody tr:last-child td{border-bottom:none}
tbody tr:hover td{background:var(--mutbg)}
code{font:12px/1.4 ui-monospace,SFMono-Regular,Menlo,monospace;background:var(--mutbg);
padding:1.5px 5px;border-radius:4px;color:var(--fg)}

.chip{display:inline-block;font-size:10px;font-weight:700;text-transform:uppercase;
letter-spacing:.06em;padding:2.5px 8px;border-radius:5px;white-space:nowrap}
.chip.ok{color:var(--ok);background:var(--okbg)}
.chip.bad{color:var(--bad);background:var(--badbg)}
.chip.warn{color:var(--warn);background:var(--warnbg)}
.chip.info{color:var(--info);background:var(--infobg)}
.chip.neutral{color:var(--mut);background:var(--mutbg)}
/* The four honesty states read as one family, deliberately quieter than a verdict:
   they describe how much we know, not whether something is good. */
.chip.state-real{color:var(--ok);background:var(--okbg)}
.chip.state-pending{color:var(--warn);background:var(--warnbg)}
.chip.state-unavailable,.chip.state-unknown{color:var(--mut);background:var(--mutbg);
cursor:help;border:1px dashed currentColor}
.chip.state-unknown{opacity:.85}

ol.queue{padding-left:0;margin:0;list-style:none;counter-reset:q}
ol.queue li{counter-increment:q;position:relative;padding:12px 16px 12px 44px;
background:var(--panel);border:1px solid var(--line);border-radius:8px;margin-bottom:8px}
ol.queue li::before{content:counter(q);position:absolute;left:14px;top:12px;
color:var(--accent);font-weight:700;font-size:12px}
ul{margin:0 0 12px;padding-left:18px}
ul li{margin-bottom:7px}
ul.legend{list-style:none;padding:0;display:flex;flex-wrap:wrap;gap:8px 20px;
margin:0 0 16px;font-size:12px}
ul.legend li{display:flex;align-items:center;gap:8px;margin:0}

.banner{border:1px solid var(--line);border-left:2px solid var(--accent);
background:var(--panel);padding:13px 16px;border-radius:0 8px 8px 0;margin:0 0 22px;
font-size:12.5px;color:var(--dim);max-width:82ch}
footer{margin-top:36px;padding-top:14px;border-top:1px solid var(--rule);
color:var(--faint);font-size:11.5px}

@media (max-width:840px){
.shell{grid-template-columns:1fr}
.rail{position:static;height:auto;border-right:none;border-bottom:1px solid var(--line);padding-bottom:14px}
.rail nav{flex-direction:row;flex-wrap:wrap}
main{padding:20px 16px 60px}
section{display:block}
}
"""

# No framework, no dependency. Sections are all present in the DOM; this shows one.
# Without JS every section stays visible and the page degrades to a long document,
# which is why sections are display:block in the narrow media query too.
NAV_JS = """
(function(){
  var links=[].slice.call(document.querySelectorAll('.rail a[data-go]'));
  var secs=[].slice.call(document.querySelectorAll('section[id]'));
  if(!links.length||!secs.length)return;
  document.documentElement.classList.add('js');
  function show(id){
    secs.forEach(function(s){s.classList.toggle('on',s.id===id)});
    links.forEach(function(a){a.classList.toggle('on',a.getAttribute('data-go')===id)});
    try{history.replaceState(null,'','#'+id)}catch(e){}
  }
  links.forEach(function(a){
    a.addEventListener('click',function(e){e.preventDefault();show(a.getAttribute('data-go'))});
  });
  var start=(location.hash||'').replace('#','');
  show(secs.some(function(s){return s.id===start})?start:secs[0].id);
})();
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

    rail = "".join(
        f'<a href="#{sid}" data-go="{sid}">{label}'
        + (f'<span class="n">{count}</span>' if count is not None else "")
        + "</a>"
        for sid, label, count, _, _, _ in panels)

    body = "".join(section(sid, title, panel_body, note)
                   for sid, _, _, title, note, panel_body in panels)

    parts = [
        f"<title>JARVIS &middot; MergeSurvivor Studio</title><style>{CSS}</style>",
        '<div class="shell">',
        '<aside class="rail">',
        '<div class="brand"><b>JARV<i>I</i>S</b><span>MergeSurvivor Studio</span></div>',
        f"<nav>{rail}</nav>",
        "</aside>",
        "<main>",
        "<header>",
        f'<h1>Phase {esc(phase.get("current", "?"))} &mdash; {esc(phase.get("name", "?"))}</h1>',
        f'<p class="meta"><code>{esc(branch)}</code> at <code>{esc(commit)}</code>'
        + (' &middot; <span class="chip warn">working tree dirty</span>' if dirty else "")
        + f' &middot; generated {esc(now)}</p>',
        "</header>",
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
        "</main></div>",
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
