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


def section(sid, title, body, note=None, eyebrow=None, count=None):
    """
    One section, with the header block that gives the page its hierarchy.

    The eyebrow names the group the section sits in, so a reader who deep-linked
    straight to #audit still knows where they landed without the rail telling them --
    which on a phone it cannot, because the rail is not on screen.
    """
    n = f'<p class="note">{note}</p>' if note else ""
    eb = f'<span class="eyebrow">{esc(eyebrow)}</span>' if eyebrow else ""
    ct = f'<span class="count">{esc(count)}</span>' if count is not None else ""
    return (f'<section id="{esc(sid)}" aria-labelledby="h-{esc(sid)}" tabindex="-1">'
            f'<header class="shead">{eb}<h2 id="h-{esc(sid)}">{esc(title)}</h2>{ct}</header>'
            f"{n}{body}</section>")


# Where a record's own prose is longer than a cell can carry, and the difference
# between shortening it and hiding it.
#
# The first version cut proposals and challenges at 160-200 characters and appended an
# ellipsis. That is data loss dressed as layout: PRO-0005's problem statement ends with
# the condition that makes it a bug, and the page silently dropped it. A <details>
# keeps the whole record in the document -- searchable, selectable, printable, and
# reachable with no JavaScript at all -- while the closed state keeps a table of
# fourteen evidence records to a height a person can scan.
LONG = 150

# The page's standing claim about itself. It sits inside the overview, beside the legend
# that defines the four states it refers to, and not above every section: rendered
# page-wide it cost a phone 130px of the first screen on every view a reader opened, to
# repeat a sentence they had already read once.
BANNER = ('<div class="banner">Generated from the files in this checkout and nothing '
          'else. It reads only &mdash; it can change no record &mdash; and it never '
          'guesses: every claim carries one of four states, and anything it cannot '
          'establish from disk says so rather than showing a colour it has not '
          'earned.</div>')


def long_text(value, limit=LONG):
    text = (value or "").strip()
    if not text:
        return '<span class="dim">&mdash;</span>'
    if len(text) <= limit:
        return f'<span class="tx">{esc(text)}</span>'
    cut = text.rfind(" ", 0, limit)
    head = text[:cut if cut > limit * 0.6 else limit].rstrip()
    return (f'<details class="more tx"><summary><span class="peek">{esc(head)}</span>'
            f'<span class="cue" aria-hidden="true"></span></summary>'
            f'<p>{esc(text)}</p></details>')


def table(headers, rows, widths=None):
    """
    One table markup that works in two layouts.

    Every cell carries its column name in data-label. On a phone the CSS drops the
    header row and turns each row into a stacked record using those labels, which is
    the only way a six-column audit table stays readable at 390px without either
    horizontal scrolling or dropping columns. Dropping columns was the tempting option
    and it is the wrong one: this page's whole claim is that it shows the record, and a
    record with three of its six fields hidden on mobile is a different record.

    Cell content is wrapped in a div rather than dropped straight into the td, and that
    wrapper is load-bearing. The phone layout makes each td a two-column grid -- label,
    then value -- and CSS grid promotes every child of a grid container to an item,
    anonymous text runs included. So a cell reading `G2 @ <code>0b3d5c3</code> <span
    class="chip">pass</span>` became three items and spilled the commit and the verdict
    into the following grid rows, under the wrong label. One wrapper makes the value
    exactly one item, whatever it is built from.

    widths, when given, sets a colgroup. Without it the browser sizes columns by content
    and an evidence Summary of 600 characters squeezes `EVD-0001` into two lines beside
    it -- the identifier being the one thing in the row a reader scans for.
    """
    if not rows:
        return '<p class="empty">Nothing recorded.</p>'
    cols = ("<colgroup>" + "".join(f'<col style="width:{w}">' for w in widths)
            + "</colgroup>") if widths else ""
    head = "".join(f"<th scope=\"col\">{esc(h)}</th>" for h in headers)
    body = ""
    for r in rows:
        cells = "".join(
            f'<td data-label="{esc(headers[i] if i < len(headers) else "")}">'
            f'<div class="v">{c}</div></td>'
            for i, c in enumerate(r))
        body += f"<tr>{cells}</tr>"
    return (f'<div class="scroll"><table>{cols}<thead><tr>{head}</tr></thead>'
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
    return table(["Gate", "Owner", "Latest recorded verdict", "At", "Overridable by"], rows,
                 widths=["21%", "19%", "17%", "24%", "19%"])


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
    return table(["Order", "Status", "Agent", "Criteria", "Closed by"], rows,
                 widths=["13ch", "16ch", "27ch", "11ch", "auto"])


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
    return summary + table(["Division", "Active", "State", "Waiting on"], rows,
                           widths=["18ch", "10ch", "14ch", "auto"])


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
    return table(["Agent", "Division", "State"], rows,
                 widths=["34%", "34%", "32%"])


def panel_open_questions(challenges, rulings):
    rows = []
    for _, c in challenges:
        if c.get("status") == "open":
            rows.append([
                f'<strong>{esc(c.get("id", "?"))}</strong>',
                "challenge",
                f'<code>{esc(c.get("taskId", "&mdash;"))}</code>',
                long_text(c.get("claim")),
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
    return table(["Record", "Kind", "Order", "Detail"], rows,
                 widths=["14%", "16%", "14%", "auto"])


def panel_decisions(decisions):
    rows = []
    for _, d in sorted(decisions, key=lambda t: t[1].get("id", "")):
        rows.append([
            f'<strong>{esc(d.get("id", "?"))}</strong>',
            f'<code>{esc(d.get("level", "?"))}</code>',
            chip(d.get("status", "?"), "ok" if d.get("status") == "accepted" else "warn"),
            esc(d.get("title") or ""),
        ])
    return table(["ADR", "Level", "Status", "Decision"], rows,
                 widths=["12ch", "10ch", "14ch", "auto"])


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
            long_text(e.get("summary")),
        ])
    note = ('Recorded evidence, each bound to a commit anyone can check out and re-run. '
            'This is history, not a live result: current CI status is '
            + unavailable("CI runs in GitHub Actions and does not commit back, so no "
                          "checkout can read the current result.") + '.')
    return f'<p class="note">{note}</p>' + table(
        ["Evidence", "Tier", "Verdict", "Commit", "Produced by", "Summary"], rows,
        widths=["13ch", "7ch", "10ch", "12ch", "27ch", "auto"])


def panel_proposals(proposals):
    rows = []
    for _, p in sorted(proposals, key=lambda t: t[1].get("id", "")):
        status = p.get("status", "?")
        rows.append([
            f'<strong>{esc(p.get("id", "?"))}</strong>',
            state_chip("pending") if status == "open" else chip(status, "ok"),
            f'<code>{esc(p.get("raisedBy", "?"))}</code>',
            esc(p.get("priority") or "&mdash;"),
            long_text(p.get("problem")),
        ])
    return table(["Proposal", "Status", "Raised by", "Priority", "Problem"], rows,
                 widths=["13ch", "15ch", "25ch", "13ch", "auto"])


def panel_escalations(escalations):
    rows = []
    for _, e in sorted(escalations, key=lambda t: t[1].get("id", "")):
        status = e.get("status", "?")
        rows.append([
            f'<strong>{esc(e.get("id", "?"))}</strong>',
            state_chip("pending") if status == "open" else chip(status, "ok"),
            f'<code>{esc(e.get("raisedBy", "?"))}</code>',
            long_text(e.get("question")),
        ])
    return table(["Escalation", "Status", "Raised by", "Question"], rows,
                 widths=["15ch", "15ch", "25ch", "auto"])


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
            long_text(e.get("summary")),
        ])
    return table(["Event", "At", "Type", "Actor", "Subject", "Summary"], rows,
                 widths=["16ch", "16ch", "17ch", "17ch", "15ch", "auto"])


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
    return head + table(["Budget", "Scope", "Soft warn", "Hard stop", "Spent"], rows,
                        widths=["16ch", "22ch", "13ch", "13ch", "auto"])


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
            + table(["Record", "Verdict", "Order", "Commit", "Issued by", "Evidence"], rows,
                    widths=["auto", "11ch", "13ch", "13ch", "22ch", "20ch"]))


def stat(label, value, sub=None, tone="", href=None):
    """
    One tile. value is already-rendered HTML so a tile can carry a chip instead of a
    number -- which is the whole point: the honest answer to "what is CI saying" is a
    state chip, and a tile grid that can only hold numbers would have quietly dropped
    the question rather than answered it.
    """
    inner = (f'<span class="k">{esc(label)}</span><span class="val">{value}</span>'
             + (f'<span class="sub">{sub}</span>' if sub else ""))
    if href:
        return f'<a class="stat {tone}" href="#{esc(href)}" data-go="{esc(href)}">{inner}</a>'
    return f'<div class="stat {tone}">{inner}</div>'


def panel_pulse(f):
    """
    The one screen a founder opens first: how much is waiting on a person, how much of
    the studio is awake, and what the record says about the last gate.

    Every tile is a count of files or a field read from one of them. There is no
    composite health score and no percentage-complete, because neither exists on disk;
    inventing one is exactly the green light rule 2 was written against. Where the
    honest value is a state rather than a number -- live CI, spend -- the tile carries
    the chip and stays grey.
    """
    ev_pass = sum(1 for _, e in f["evidence"] if e.get("verdict") == "pass")
    ev_other = len(f["evidence"]) - ev_pass
    tiles = [
        # No href: the list this tile counts is directly below it. A tile that
        # navigates to the section a reader is already looking at is a dead control.
        stat("Waiting on the founder", str(len(f["queue"])),
             "nobody else can clear these", "accent" if f["queue"] else ""),
        stat("Blocked", str(len(f["blocked"])),
             f'{len(f["deps"])} agent dependency chain(s)', "bad" if f["blocked"] else "",
             "blocked"),
        stat("Agents active", f'{f["active"]} <span class="of">/ {f["total"]}</span>',
             f'{f["total"] - f["active"]} dormant, each with a stated trigger', "", "agents"),
        stat("Work orders open", str(len(f["orders"])),
             f'{len(f["backlog"])} item(s) on the backlog', "", "work"),
        stat("G2 code gate", f["g2_cell"],
             "latest verdict record on disk", "", "gates"),
        stat("G3 integration", f["g3_cell"],
             "runs in GitHub Actions", "", "gates"),
        stat("Evidence records", str(len(f["evidence"])),
             (f'{ev_pass} pass'
              + (f', {ev_other} other' if ev_other else '')
              + ' &middot; history, not a live run'), "", "validation"),
        stat("Open questions", str(len(f["open_challenges"]) + len(f["not_ready"])),
             f'{len(f["open_challenges"])} challenge(s), '
             f'{len(f["not_ready"])} ruling(s) withholding a gate',
             "warn" if (f["open_challenges"] or f["not_ready"]) else "", "questions"),
        stat("Proposals open", str(len(f["open_proposals"])),
             f'of {len(f["proposals"])} filed', "", "proposals"),
        stat("Escalations open", str(len(f["open_escalations"])),
             f'of {len(f["escalations"])} filed', "", "escalations"),
        stat("Spend to date", unknown("No spend records exist in the repository."),
             f'ceiling {esc(f["ceiling"])} DKK', "", "budget"),
        stat("Records read", str(f["records"]),
             "tracked JSON files under Studio/", "", "audit"),
    ]
    return f'<div class="stats">{"".join(tiles)}</div>'


def panel_backlog(state):
    """
    The backlog as project-state.json records it, which is not the same list as the
    work-order files and must not be shown as if it were.

    Studio/orders holds the four orders that have been written; project-state.backlog
    holds seven items, three of which have no order file yet. Rendering only the files
    made the queue look shorter than it is; merging them into one list would have
    invented a work order. So they are two panels, each naming its own source.
    """
    items = (state or {}).get("backlog") or []
    if not items:
        return ('<p class="empty">No backlog recorded. '
                + unknown("project-state.backlog is absent or empty.") + "</p>")
    tone = {"blocked": "bad", "gate_pending": "warn", "dispatched": "info",
            "in_progress": "info", "queued": "neutral", "completed": "ok"}
    rows = []
    for b in sorted(items, key=lambda i: (i.get("priority") or 99, i.get("id", ""))):
        blocked_by = b.get("blockedBy") or []
        rows.append([
            f'<strong>{esc(b.get("id", "?"))}</strong>',
            chip(b.get("status", "?"), tone.get(b.get("status"), "neutral")),
            f'<code>{esc(b.get("level", "?"))}</code>',
            esc(b.get("priority", "?")),
            (f'<code>{esc(b.get("assignedTo"))}</code>' if b.get("assignedTo")
             else unknown("No agent is assigned to this item in project-state.")),
            long_text(b.get("title")),
        ])
        if blocked_by:
            rows[-1][5] += ('<span class="blockedby">blocked by '
                            + ", ".join(f'<code>{esc(x)}</code>' for x in blocked_by)
                            + "</span>")
    return table(["Item", "Status", "Level", "Priority", "Assigned to", "Title"], rows,
                 widths=["13ch", "18ch", "9ch", "11ch", "25ch", "auto"])


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
# Dark is the identity; light is a first-class translation of it, not a tint. The
# earlier pass shipped dark only, on the argument that an identity which flips with a
# system setting is two identities maintained badly. That argument was right about the
# risk and wrong about the remedy: a founder reading a gate verdict on a phone in
# daylight is not served by a near-black page, and a light mode bolted on later is
# exactly the second identity the argument feared. So both are defined in one token
# block, every colour is re-picked for its own ground rather than dimmed, and neither
# theme has a rule the other lacks -- see the tokens at the top of CSS.

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
    "backlog": "M4 5h5v5H4z M4 13h5v5H4z M12 7h7 M12 15h7",
}

# The theme control, rendered exactly once, at the end of the brand row. A first pass
# put a copy in the desktop status bar as well; the script bound the first match, which
# on desktop is the copy CSS had just hidden, so the visible button did nothing. One
# element in one place cannot develop that failure.
THEME_BTN = (
    '<button class="themebtn" type="button" aria-pressed="false" '
    'title="Switch to light">'
    '<svg class="sun" viewBox="0 0 20 20" aria-hidden="true">'
    '<circle cx="10" cy="10" r="3.6"/><path d="M10 1.5v2 M10 16.5v2 M1.5 10h2 '
    'M16.5 10h2 M4 4l1.4 1.4 M14.6 14.6L16 16 M16 4l-1.4 1.4 M5.4 14.6L4 16"/></svg>'
    '<svg class="moon" viewBox="0 0 20 20" aria-hidden="true">'
    '<path d="M16.5 12.4A7 7 0 017.6 3.5a7 7 0 108.9 8.9z"/></svg>'
    '<span class="vh themelbl">Switch to light theme</span></button>')

CSS = """
/* ============================================================== tokens ===
   Two themes, and the light one is not a tint of the dark one. Instrument panels
   read as glowing marks on a dark ground; paper reads as ink on a light one, and
   the same hex values cannot do both -- #5ee0d0 is a 1.9:1 accent on white, which
   is invisible, so light mode carries its own teal at 4.8:1 rather than the same
   colour dimmed. Every colour the page uses is declared here, in :root, and the
   theme blocks only ever redefine these names. A colour defined solely inside a
   theme block is the one thing that reliably breaks the other theme.

   Dark is the default because that is JARVIS's identity. It is not forced:
   prefers-color-scheme moves the page to light for a reader whose system says
   light, and the toggle in the header overrides both by stamping data-theme.
   ------------------------------------------------------------------------ */
:root{
color-scheme:dark;
--bg:#090b0e; --bg2:#0d1015; --panel:#11151b; --panel2:#161b23; --hover:#1a2029;
--fg:#e8ecf1; --dim:#a3adba; --faint:#8d97a6;
--line:#1f2630; --line2:#2b3440; --rule:#1a212a;
--accent:#5ee0d0; --accent-txt:#5ee0d0; --accent-dim:#2f7d75; --accent-bg:#0e2b28;
--mark-fg:#04231f; --mark-bg:#5ee0d0; --mark-a:#5ee0d0; --mark-b:#2f7d75;
--ok:#4bd4a0; --ok-bg:#0d2620; --ok-line:#1c4a3c;
--bad:#ff8f85; --bad-bg:#2c1416; --bad-line:#5a2528;
--warn:#f2c76e; --warn-bg:#2a2110; --warn-line:#4a3a14;
--info:#8bbcf7; --info-bg:#111e30; --info-line:#1e3550;
--mut:#a3adba; --mut-bg:#181e26; --mut-line:#2b3440;
--shadow:0 8px 30px rgba(0,0,0,.55); --shadow-s:0 1px 2px rgba(0,0,0,.4);
--r:8px; --r-l:12px; --rail:238px; --aside:268px; --head:56px;
--work-max:1180px;
--t:140ms cubic-bezier(.4,0,.2,1);
--pad:16px;
}

/* Light. Same structure, ink on paper: panels are white and lift off a grey ground
   with a hairline, not a shadow, so the density stays instrument-like rather than
   card-shaped. Every status colour is re-picked against white -- the dark theme's
   #4bd4a0 is 1.7:1 on paper and would turn PASS into a suggestion. */
:root[data-theme="light"]{
color-scheme:light;
--bg:#eef1f5; --bg2:#ffffff; --panel:#ffffff; --panel2:#f5f7fa; --hover:#eef2f6;
--fg:#0e1319; --dim:#48546a; --faint:#5d6a7d;
--line:#dde3ea; --line2:#c6cfda; --rule:#eaeef3;
--accent:#0b6f64; --accent-txt:#0a6259; --accent-dim:#0b6f64; --accent-bg:#dcf0ed;
--mark-fg:#ffffff; --mark-bg:#0a5b52; --mark-a:#0f8377; --mark-b:#0a5b52;
--ok:#0a6b4a; --ok-bg:#dcf1e7; --ok-line:#a7d8c4;
--bad:#a51f16; --bad-bg:#fce9e7; --bad-line:#f0bdb8;
--warn:#7a4e00; --warn-bg:#fbeed4; --warn-line:#e6cd97;
--info:#17439c; --info-bg:#e5edfc; --info-line:#bcd0f2;
--mut:#48546a; --mut-bg:#eff2f6; --mut-line:#ccd4de;
--shadow:0 10px 30px rgba(16,24,40,.14); --shadow-s:0 1px 2px rgba(16,24,40,.08);
}
@media (prefers-color-scheme:light){
:root:not([data-theme="dark"]){
color-scheme:light;
--bg:#eef1f5; --bg2:#ffffff; --panel:#ffffff; --panel2:#f5f7fa; --hover:#eef2f6;
--fg:#0e1319; --dim:#48546a; --faint:#5d6a7d;
--line:#dde3ea; --line2:#c6cfda; --rule:#eaeef3;
--accent:#0b6f64; --accent-txt:#0a6259; --accent-dim:#0b6f64; --accent-bg:#dcf0ed;
--mark-fg:#ffffff; --mark-bg:#0a5b52; --mark-a:#0f8377; --mark-b:#0a5b52;
--ok:#0a6b4a; --ok-bg:#dcf1e7; --ok-line:#a7d8c4;
--bad:#a51f16; --bad-bg:#fce9e7; --bad-line:#f0bdb8;
--warn:#7a4e00; --warn-bg:#fbeed4; --warn-line:#e6cd97;
--info:#17439c; --info-bg:#e5edfc; --info-line:#bcd0f2;
--mut:#48546a; --mut-bg:#eff2f6; --mut-line:#ccd4de;
--shadow:0 10px 30px rgba(16,24,40,.14); --shadow-s:0 1px 2px rgba(16,24,40,.08);
}}

@media (prefers-reduced-motion:reduce){:root{--t:1ms}
*{animation:none!important;transition:none!important;scroll-behavior:auto!important}}

/* ================================================================ base === */
*{box-sizing:border-box}
html{-webkit-text-size-adjust:100%}
body{margin:0;background:var(--bg);color:var(--fg);
font:15px/1.55 ui-sans-serif,system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;
font-variant-numeric:tabular-nums;-webkit-font-smoothing:antialiased;
text-rendering:optimizeLegibility;overflow-x:hidden}
:focus-visible{outline:2px solid var(--accent);outline-offset:2px;border-radius:4px}
/* Sections are focused programmatically so a screen reader announces the new view.
   That is a move cue, not a control, so it must not draw a focus ring around the
   whole panel -- which it did, framing the page in accent on every load. */
section:focus,section:focus-visible{outline:none}
h1,h2,h3{margin:0}
a{color:var(--accent-txt)}

/* The standard visually-hidden pattern, and the reason matters: the first version used
   left:-9999px, which extended the scrollable area and gave the phone layout a real
   horizontal overflow -- content clipped at the right edge on a 390px viewport. Clipping
   to a 1px box takes it out of layout entirely instead of parking it off-canvas. */
.vh,.skip{position:absolute;width:1px;height:1px;margin:-1px;padding:0;overflow:hidden;
clip:rect(0 0 0 0);clip-path:inset(50%);white-space:nowrap;border:0}
.skip:focus{position:fixed;left:10px;top:10px;width:auto;height:auto;margin:0;
padding:12px 18px;overflow:visible;clip:auto;clip-path:none;z-index:99;
background:var(--accent);color:var(--mark-fg);font-weight:700;border-radius:var(--r)}

/* ======================================================= shell (phone) ===
   Phone first, and literally so: everything below describes a 390px screen, and the
   desktop shell is the override further down rather than the other way round. The
   previous pass wrote the desktop layout first and undid it in a max-width block,
   which is how the phone ended up inheriting a 236px rail it then had to hide. */
.app{display:flex;flex-direction:column;min-height:100vh;min-height:100dvh}

.brand{display:flex;align-items:center;gap:10px;position:sticky;top:0;z-index:30;
height:54px;padding:0 max(var(--pad),env(safe-area-inset-left)) 0
max(var(--pad),env(safe-area-inset-left));
background:var(--bg2);border-bottom:1px solid var(--line)}
.mark{width:25px;height:25px;flex:none;border-radius:7px;
background-color:var(--mark-bg);
background-image:linear-gradient(150deg,var(--mark-a),var(--mark-b));
display:grid;place-items:center;color:var(--mark-fg);font-weight:800;font-size:12px;
letter-spacing:-.02em}
.brand .word{font-size:14.5px;font-weight:700;letter-spacing:.17em}
.brand .word em{font-style:normal;color:var(--accent-txt)}
.brand .spacer{flex:1}

/* Theme control. A button, not a checkbox dressed as one: it changes one thing and
   says which state it is in through aria-pressed and its own label. */
.themebtn{display:grid;place-items:center;width:44px;height:44px;flex:none;
border-radius:9px;border:1px solid var(--line);background:transparent;color:var(--dim);
cursor:pointer;transition:background var(--t),color var(--t),border-color var(--t)}
.themebtn:hover{background:var(--panel2);color:var(--fg);border-color:var(--line2)}
.themebtn svg{width:17px;height:17px;stroke:currentColor;fill:none;stroke-width:1.7;
stroke-linecap:round;stroke-linejoin:round}
.themebtn .moon{display:none}
:root[data-theme="light"] .themebtn .sun{display:none}
:root[data-theme="light"] .themebtn .moon{display:block}
@media (prefers-color-scheme:light){
:root:not([data-theme="dark"]) .themebtn .sun{display:none}
:root:not([data-theme="dark"]) .themebtn .moon{display:block}}

/* Status strip. Kept on the phone, not dropped. Which commit the page describes, and
   whether G2 and G3 are known, are the facts that make everything below meaningful;
   a phone layout that silently discards them shows the same data with less truth. */
.top{display:flex;flex-wrap:wrap;align-items:center;gap:7px 14px;
padding:9px max(var(--pad),env(safe-area-inset-right)) 10px
max(var(--pad),env(safe-area-inset-left));
border-bottom:1px solid var(--line);background:var(--bg2);min-width:0}
.top .ctx{display:flex;align-items:baseline;flex-wrap:wrap;gap:5px 10px;
flex:1 1 100%;min-width:0}
.top .ctx b{font-size:13.5px;font-weight:650;flex:1 1 100%}
/* The branch name is longer than a phone is wide, and nowrap on it pushed the document
   2px past the viewport. Nothing else on the page overflowed; one unbreakable token is
   all it takes. */
.top .ctx span{color:var(--faint);font-size:12px}
/* code is nowrap everywhere else on the page, and the branch name is longer than a
   320px phone is wide: without resetting white-space here it painted 15px past the
   header's own box. */
.top .ctx code{white-space:normal;word-break:break-all;max-width:100%}
.sysbar{display:flex;align-items:center;flex-wrap:wrap;gap:8px 16px;flex:1 1 100%}
.sysbar .kv{display:flex;align-items:center;gap:7px;font-size:11.5px;color:var(--faint)}
.sysbar .kv b{color:var(--dim);font-weight:700;letter-spacing:.07em;
text-transform:uppercase;font-size:10.5px}

.rail{display:none}
.main{display:block;min-width:0;flex:1}
.work{padding:20px max(var(--pad),env(safe-area-inset-right))
calc(104px + env(safe-area-inset-bottom)) max(var(--pad),env(safe-area-inset-left));
min-width:0;max-width:var(--work-max);margin:0 auto;width:100%}

/* ============================================================= sections === */
section{display:block;margin-bottom:36px;scroll-margin-top:var(--head)}
.js section{display:none;margin-bottom:0}
.js section.on{display:block;animation:in var(--t) both}
@keyframes in{from{opacity:0;transform:translateY(3px)}to{opacity:1;transform:none}}

/* Header block. The eyebrow carries the rail group, the count carries the number that
   was previously only visible in the rail -- which the phone does not show. */
.shead{display:flex;align-items:center;flex-wrap:wrap;gap:0 10px;margin:0 0 6px}
.eyebrow{flex:1 1 100%;font-size:10px;font-weight:700;letter-spacing:.18em;
text-transform:uppercase;color:var(--faint);margin-bottom:5px}
.shead h2{font-size:20px;font-weight:660;letter-spacing:-.018em;line-height:1.25}
.shead .count{font-size:11.5px;font-weight:700;color:var(--dim);background:var(--mut-bg);
border:1px solid var(--line);border-radius:999px;padding:2px 9px}
h3.sub{font-size:15px;font-weight:650;letter-spacing:-.01em;margin:30px 0 10px;
padding-top:22px;border-top:1px solid var(--rule)}
.note{color:var(--dim);font-size:13px;margin:0 0 16px;max-width:78ch;line-height:1.62}
.empty{color:var(--faint);font-style:italic;padding:14px 16px;background:var(--panel);
border:1px dashed var(--line2);border-radius:var(--r)}
.big{font-size:28px;font-weight:680;letter-spacing:-.025em;margin:0 0 18px}
.big .dim{font-size:13px;font-weight:400;letter-spacing:0}
.dim{color:var(--dim)}

.banner{border:1px solid var(--line);border-left:2px solid var(--accent);
background:var(--panel);padding:13px 16px;border-radius:0 var(--r) var(--r) 0;
margin:0 0 22px;font-size:12.5px;color:var(--dim);max-width:84ch;line-height:1.6}

/* ================================================================ stats ===
   The overview's instrument row. Two columns on a phone rather than one, because
   these are short paired facts and a single column turns twelve of them into a
   scroll; and never more than one line of number, so the row reads as a row. */
.stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(148px,1fr));
gap:10px;margin:0 0 26px}
.stat{display:flex;flex-direction:column;gap:5px;padding:13px 14px 14px;
background:var(--panel);border:1px solid var(--line);border-radius:var(--r);
min-width:0;text-decoration:none;color:inherit;position:relative;
transition:border-color var(--t),background var(--t)}
a.stat:hover{border-color:var(--line2);background:var(--panel2)}
.stat .k{font-size:10.5px;font-weight:700;letter-spacing:.09em;text-transform:uppercase;
color:var(--faint)}
.stat .val{font-size:26px;font-weight:670;letter-spacing:-.03em;line-height:1.1;
color:var(--fg);display:flex;align-items:center;gap:8px;flex-wrap:wrap;min-width:0}
.stat .val .chip{font-size:10px;letter-spacing:.07em}
.stat .val .of{font-size:14px;font-weight:400;color:var(--faint);letter-spacing:0}
.stat .sub{font-size:11.5px;color:var(--faint);line-height:1.45}
.stat.accent{border-color:var(--accent-dim)}
.stat.accent .val{color:var(--accent-txt)}
.stat.bad .val{color:var(--bad)}
.stat.warn .val{color:var(--warn)}

/* ================================================================ table ===
   On a phone every row is a stacked record: the header row goes away and each cell
   carries its own label, so nothing is dropped and nothing scrolls sideways. */
.scroll{overflow:visible}
table{width:100%;border-collapse:collapse}
colgroup,thead{display:none}
table,tbody,tr,td{display:block;width:100%}
tbody tr{background:var(--panel);border:1px solid var(--line);border-radius:var(--r);
margin-bottom:10px;overflow:hidden}
td{border-bottom:1px solid var(--rule);padding:9px 14px;display:grid;
grid-template-columns:minmax(80px,32%) minmax(0,1fr);gap:12px;align-items:baseline;
font-size:13.5px}
tbody tr td:last-child{border-bottom:none}
td::before{content:attr(data-label);color:var(--faint);font-size:10px;
text-transform:uppercase;letter-spacing:.09em;font-weight:700;line-height:1.75}
/* The value wrapper. Without it the chip and the code in a two-part cell became
   separate grid items and landed under the next label. */
td .v{min-width:0;overflow-wrap:anywhere}
td .v code{white-space:normal;word-break:break-all}

code{font:11.5px/1.45 ui-monospace,SFMono-Regular,Menlo,monospace;
background:var(--mut-bg);border:1px solid var(--line);padding:1px 5px;
border-radius:4px;color:var(--dim);white-space:nowrap;max-width:100%}
.blockedby{display:block;margin-top:6px;font-size:11.5px;color:var(--warn)}

/* Long record prose. Closed, a cell shows its first line and a cue; open, the whole
   field. Native <details>, so it works with scripting off and prints expanded. */
details.more>summary{list-style:none;cursor:pointer;display:block;color:var(--fg)}
details.more>summary::-webkit-details-marker{display:none}
details.more .peek{color:var(--dim)}
details.more .cue::after{content:" more";color:var(--accent-txt);font-weight:650;
font-size:11.5px;white-space:nowrap}
details.more[open] .cue::after{content:" less"}
details.more[open] .peek{display:none}
details.more>p{margin:0;color:var(--fg);line-height:1.6}
details.more>summary:focus-visible{outline:2px solid var(--accent);outline-offset:2px}

/* ================================================================ chips === */
.chip{display:inline-block;font-size:10.5px;font-weight:700;text-transform:uppercase;
letter-spacing:.07em;padding:3px 8px;border-radius:5px;white-space:nowrap;
border:1px solid transparent;vertical-align:baseline}
.chip.ok{color:var(--ok);background:var(--ok-bg);border-color:var(--ok-line)}
.chip.bad{color:var(--bad);background:var(--bad-bg);border-color:var(--bad-line)}
.chip.warn{color:var(--warn);background:var(--warn-bg);border-color:var(--warn-line)}
.chip.info{color:var(--info);background:var(--info-bg);border-color:var(--info-line)}
.chip.neutral{color:var(--mut);background:var(--mut-bg);border-color:var(--mut-line)}
/* The four honesty states read as one family and stay quieter than a verdict: they
   describe how much is known, not whether it is good. UNAVAILABLE and UNKNOWN keep
   the dashed edge that marks "this is not a result". */
.chip.state-real{color:var(--ok);background:var(--ok-bg);border-color:var(--ok-line)}
.chip.state-pending{color:var(--warn);background:var(--warn-bg);border-color:var(--warn-line)}
.chip.state-unavailable,.chip.state-unknown{color:var(--mut);background:transparent;
border:1px dashed var(--line2);cursor:help}
.chip.state-unknown{color:var(--faint)}

/* ================================================================= misc === */
ol.queue{list-style:none;padding:0;margin:0;counter-reset:q}
ol.queue li{counter-increment:q;position:relative;padding:15px 16px 15px 46px;
background:var(--panel);border:1px solid var(--line);border-radius:var(--r);
margin-bottom:8px;line-height:1.55;font-size:14px}
ol.queue li::before{content:counter(q,decimal-leading-zero);position:absolute;
left:16px;top:16px;color:var(--accent-txt);font-weight:700;font-size:11px;
font-family:ui-monospace,monospace}
ul{margin:0 0 14px;padding-left:20px}
ul li{margin-bottom:9px;line-height:1.55}
ul.deps li{margin-bottom:6px}
/* "balance-director -> design-director" set a min-content width of 380px and pushed
   a 320px viewport into horizontal scrolling. Commit hashes stay nowrap; these do not. */
ul.deps code{white-space:normal;overflow-wrap:anywhere}
ul.legend{list-style:none;padding:14px 16px;display:grid;gap:11px;margin:0 0 22px;
background:var(--panel);border:1px solid var(--line);border-radius:var(--r);
grid-template-columns:repeat(auto-fit,minmax(228px,1fr))}
ul.legend li{display:flex;align-items:flex-start;gap:10px;margin:0;font-size:12.5px;
color:var(--dim);line-height:1.5}
ul.legend li .chip{flex:none}
.narrowonly{display:block}
@media (min-width:1400px){.narrowonly{display:none}}
.aside ul.legend{grid-template-columns:1fr;background:none;border:none;padding:0}
footer{margin-top:36px;padding-top:16px;border-top:1px solid var(--rule);
color:var(--faint);font-size:11.5px;line-height:1.75}
.aside{display:none}

/* ========================================================= mobile nav ===
   Thumb-reachable. The sheet opens from the bottom because the top of a modern phone
   is the part of the screen a hand holding it cannot comfortably reach. */
.menubtn{display:flex;align-items:center;gap:10px;position:fixed;left:12px;right:12px;
bottom:calc(12px + env(safe-area-inset-bottom));z-index:40;height:54px;padding:0 16px;
border-radius:14px;background:var(--panel2);border:1px solid var(--line2);
color:var(--fg);font:inherit;font-size:14px;font-weight:600;cursor:pointer;
box-shadow:var(--shadow)}
.menubtn .cur{flex:1;text-align:left;overflow:hidden;text-overflow:ellipsis;
white-space:nowrap}
.menubtn .bars{width:17px;height:17px;stroke:var(--accent-txt);stroke-width:1.8;
fill:none;stroke-linecap:round;flex:none}
.menubtn .cnt{color:var(--faint);font-size:11px;font-weight:600;flex:none}
.sheetwrap{position:fixed;inset:0;z-index:50;visibility:hidden}
.sheetwrap.open{visibility:visible}
.scrim{position:absolute;inset:0;background:rgba(0,0,0,.55);opacity:0;
transition:opacity var(--t)}
.sheetwrap.open .scrim{opacity:1}
.sheet{position:absolute;left:0;right:0;bottom:0;max-height:84vh;max-height:84dvh;
overflow-y:auto;-webkit-overflow-scrolling:touch;background:var(--bg2);
border-top:1px solid var(--line2);border-radius:18px 18px 0 0;
padding:8px 12px calc(18px + env(safe-area-inset-bottom));
transform:translateY(100%);transition:transform var(--t);box-shadow:var(--shadow)}
.sheetwrap.open .sheet{transform:none}
.sheet .hnd{width:38px;height:4px;border-radius:2px;background:var(--line2);
margin:8px auto 12px}

/* One nav markup, two containers. */
.grp{margin-bottom:12px}
.grp>h3{font-size:10px;text-transform:uppercase;letter-spacing:.17em;
color:var(--faint);margin:0 0 6px;padding:0 12px;font-weight:700}
.nav a{display:flex;align-items:center;gap:11px;padding:12px;border-radius:8px;
color:var(--dim);text-decoration:none;font-size:14.5px;position:relative;min-height:46px;
transition:background var(--t),color var(--t)}
.nav a svg{width:17px;height:17px;flex:none;stroke:currentColor;fill:none;
stroke-width:1.6;stroke-linecap:round;stroke-linejoin:round;opacity:.8}
.nav a .lbl{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.nav a .n{font-size:12px;color:var(--faint);font-weight:600;flex:none}
.nav a:hover{background:var(--panel2);color:var(--fg)}
.nav a:hover svg{opacity:1}
.nav a.on{background:var(--accent-bg);color:var(--accent-txt);font-weight:650}
.nav a.on svg{opacity:1}
.nav a.on .n{color:var(--accent-txt)}

/* 600-879px -- a tablet in portrait, a phone on its side. One column of stacked
   records here wastes half the width and, in landscape, spends the little vertical
   space there is on a 270px-wide empty label gutter. Two cards abreast is the same
   markup reading as a deliberate layout for the width instead of a stretched phone. */
@media (min-width:600px) and (max-width:879px){
tbody{display:grid;grid-template-columns:1fr 1fr;gap:10px;align-items:start}
tbody tr{margin-bottom:0}
td{grid-template-columns:minmax(72px,30%) minmax(0,1fr)}
.stats{grid-template-columns:repeat(auto-fit,minmax(170px,1fr))}
}

@media (max-width:879px) and (orientation:landscape){
.sheet{max-height:88vh;max-height:88dvh}
.sheet .grp{display:grid;grid-template-columns:1fr 1fr;gap:0 8px}
.sheet .grp>h3{grid-column:1/-1}
}

/* ============================================== >= 880px : real tables ===
   The stacked-record layout is right for a phone and wrong for a tablet: at 880px a
   six-field record becomes six near-empty rows, and the columns a reader wants to
   compare down are gone. Above this width the table is a table again, inside a
   horizontal scroller so a wide one degrades to scrolling rather than clipping. */
@media (min-width:880px){
:root{--pad:26px}
.work{padding-top:26px;padding-bottom:64px}
/* overflow-x:auto, not hidden. Hidden was rounder at the corners and it silently ate
   data: at 1500px the evidence table measured 1070px inside a 915px box, so 155px of
   every Summary was invisible with no scrollbar to reveal it -- the failure mode this
   page least tolerates, since a truncated record still reads as the whole record. */
.scroll{border:1px solid var(--line);border-radius:var(--r);background:var(--panel);
overflow-x:auto}
table{display:table;table-layout:auto;font-size:12.5px}
colgroup{display:table-column-group}
thead{display:table-header-group}
tbody{display:table-row-group}
tr{display:table-row;width:auto}
td{display:table-cell;width:auto;padding:11px 14px;border-bottom:1px solid var(--rule);
vertical-align:top;line-height:1.5;font-size:12.5px}
td::before{content:none}
/* Headers wrap rather than run on. With table-layout:fixed, nowrap does not widen a
   column -- it prints "LATEST RECORDED VERDICT" straight over the column beside it,
   which is how the gate table shipped its heading across two columns at 1440px. */
th{display:table-cell;text-align:left;font-size:10px;text-transform:uppercase;
letter-spacing:.09em;color:var(--faint);font-weight:700;padding:10px 14px;
background:var(--panel2);border-bottom:1px solid var(--line);white-space:nowrap;
vertical-align:bottom}
tbody tr{background:none;border:none;border-radius:0;margin:0;
transition:background var(--t)}
tbody tr:hover{background:var(--panel2)}
tbody tr:last-child td{border-bottom:none}
/* Long identifiers -- RejectsAbsorbingDeltaTimeThatWouldNeverAdvanceTheSchedule and
   its kind -- set a min-content width wider than the column, which is what made the
   table overflow in the first place. Breaking them keeps the scrollbar a fallback
   rather than the normal case. */
/* Which values may break, and which may not, is what actually decides every column
   width here -- because overflow-wrap:anywhere reports an element's min-content width
   as one character, and table-layout:auto sizes columns from min-content.
   Applied to the whole cell, as the first pass did, it told the browser that PENDING
   and PRIORITY were each one character wide, and they duly wrapped to PENDI/NG and
   PRIORIT/Y inside columns with room to spare. The colgroup widths are preferences
   layered on top; content that needs more takes more.
   So: a chip is a fixed label and never breaks. A heading never breaks. An identifier
   breaks at its hyphens -- `design-` / `architect`, not `design-architec` / `t`. Only
   a record's own prose, the .tx that long_text emits, may break anywhere, because
   RejectsAbsorbingDeltaTimeThatWouldNeverAdvanceTheSchedule inside it would otherwise
   claim a 480px column of its own. */
td .v{min-width:0}
td .v .tx{overflow-wrap:anywhere}
td .v code{white-space:normal;overflow-wrap:break-word}
td .v .chip{white-space:nowrap;max-width:100%}
.shead h2{font-size:21px}
.stats{grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}
}

/* ================================================ >= 1024px : the shell ===
   Desktop stops being a scrolling document and becomes an application: the identity,
   the studio context and the rail are fixed, and only the workspace moves. A founder
   scrolling the audit trail should not lose the commit the page is describing. */
@media (min-width:1024px){
:root{--pad:28px}
html,body{height:100%}
body{overflow:hidden}
.app{display:grid;grid-template-columns:var(--rail) minmax(0,1fr);
grid-template-rows:var(--head) minmax(0,1fr);grid-template-areas:"brand top" "rail main";
height:100vh;height:100dvh;overflow:hidden}
.brand{grid-area:brand;position:static;height:auto;padding:0 12px 0 18px;
border-right:1px solid var(--line)}
.brand .themebtn{width:32px;height:32px;border-color:transparent}
.top{grid-area:top;flex-wrap:nowrap;height:var(--head);padding:0 22px;gap:18px;
justify-content:space-between}
/* Both min-width:0 and overflow:hidden, and both are needed. A flex item's default
   min-width is auto, so the context block refused to shrink below the width of the
   branch chip inside it and painted straight over the G2 reading at 1280px. min-width
   lets it shrink; overflow keeps anything that still does not fit inside its own box
   rather than on top of its neighbour. */
.top .ctx{flex:0 1 auto;flex-wrap:nowrap;align-items:baseline;gap:10px;min-width:0;
overflow:hidden}
.top .ctx b{min-width:0}
.top .ctx .chip{flex:none}
.top .ctx b{white-space:nowrap;font-size:13.5px;flex:0 1 auto;overflow:hidden;
text-overflow:ellipsis}
/* When the header runs out of room the branch gives way, and nothing else: a reader
   who loses "Phase 3 -- Hundred-agent organisation" has lost the page's subject, and
   one who loses the tail of a branch name has lost a detail the title attribute still
   carries. Clipping the whole span instead took the commit with it, which is the one
   value on this bar the rest of the page is relative to. */
.top .ctx span{white-space:nowrap;flex:0 1 auto;min-width:0;display:flex;
align-items:baseline;gap:5px}
.top .ctx code{word-break:normal;white-space:nowrap}
.top .ctx code.br{max-width:24ch;overflow:hidden;text-overflow:ellipsis;
display:inline-block;vertical-align:bottom;flex:0 1 auto;min-width:6ch}
.sysbar{flex:none;flex-wrap:nowrap;gap:14px}
.rail{grid-area:rail;display:block;border-right:1px solid var(--line);
background:var(--bg2);padding:14px 0 28px;overflow-y:auto;min-height:0}
.grp{padding:0 12px}
.nav a{padding:7px 10px;font-size:13px;min-height:0;border-radius:6px}
.nav a svg{width:16px;height:16px}
.nav a .n{font-size:11px}
.nav a.on::before{content:"";position:absolute;left:-12px;top:6px;bottom:6px;width:2px;
background:var(--accent);border-radius:0 2px 2px 0}
.main{grid-area:main;display:grid;grid-template-columns:minmax(0,1fr);min-width:0;
overflow:hidden;min-height:0}
/* The workspace is the scroller, not .main, and it is full-width with the content
   centred by padding rather than by max-width. Scrolling .main instead dragged the
   context column up with it, so the aside's first line was cut off the moment a
   reader scrolled a table. */
.work{overflow-y:auto;overflow-x:hidden;min-height:0;max-width:none;margin:0;
padding:26px max(28px,(100% - var(--work-max))/2) 80px}
section{scroll-margin-top:14px}
.menubtn,.sheetwrap{display:none!important}
.shead h2{font-size:22px}
ol.queue li{font-size:14px}
}

/* ================================== >= 1400px : the context column =======
   The aside earns its width by carrying what the page cannot know and where its
   numbers came from -- the two questions a reader of a dashboard should always be
   able to answer. It is not a place for decoration, and it appears only where it
   does not steal width from the table beside it. */
@media (min-width:1024px) and (max-width:1339px){
.top .ctx code.br{max-width:12ch}
.sysbar{gap:11px}
}
@media (min-width:1340px) and (max-width:1599px){.top .ctx code.br{max-width:17ch}}

@media (min-width:1400px){
.main{grid-template-columns:minmax(0,1fr) var(--aside)}
.aside{display:block;border-left:1px solid var(--line);background:var(--bg2);
padding:24px 22px 60px;font-size:12.5px;overflow-y:auto;min-height:0}
.aside h3{font-size:10px;text-transform:uppercase;letter-spacing:.17em;
color:var(--faint);margin:0 0 10px;font-weight:700}
.aside .blk{margin-bottom:26px}
.aside dl{margin:0;display:grid;grid-template-columns:1fr auto;gap:7px 10px}
.aside dt{color:var(--faint);font-size:12px}
.aside dd{margin:0;color:var(--dim);font-variant-numeric:tabular-nums}
.aside p{color:var(--faint);line-height:1.6;margin:0 0 10px}
}
/* Above the work column's own maximum the extra pixels go to the workspace, not to
   the table: a 2560px monitor stretching a six-column table to 2100px makes every row
   a saccade. The column centres instead. */
@media (min-width:1800px){:root{--work-max:1320px}}

/* ================================================================ print === */
@media print{
:root{color-scheme:light}
body{overflow:visible;background:#fff;color:#000}
.app{display:block;height:auto;overflow:visible}
.brand,.top{position:static}
.rail,.aside,.menubtn,.sheetwrap,.skip{display:none!important}
.js section,section{display:block!important;margin-bottom:24px;break-inside:avoid}
details.more>p{display:block}
.main,.work{overflow:visible;max-width:none;padding:0}
}
"""

# Progressive enhancement, and it is load-bearing. An earlier version hid every
# section by default and revealed one from script, so with JS off the page rendered
# blank. Sections are visible by default; the script opts into switching by setting
# .js on the root. Everything below degrades to a long, complete document.
NAV_JS = """
(function(){
  var root=document.documentElement;

  /* ---- theme -----------------------------------------------------------
     Three states, two of which are one: the page follows the system until a
     reader says otherwise, and data-theme is only ever stamped after a click.
     Storage is wrapped because this file is routinely opened over file://,
     where localStorage throws in some browsers rather than returning null --
     an uncaught throw here would take the whole navigation script with it. */
  var KEY='jarvis-theme';
  function stored(){try{return localStorage.getItem(KEY)}catch(e){return null}}
  function remember(v){try{localStorage.setItem(KEY,v)}catch(e){}}
  function systemDark(){
    return !window.matchMedia||!window.matchMedia('(prefers-color-scheme: light)').matches;
  }
  function isDark(){
    var t=root.getAttribute('data-theme');
    return t?t==='dark':systemDark();
  }
  var tbtn=document.querySelector('.themebtn');
  function paintTheme(){
    var dark=isDark();
    if(tbtn){
      tbtn.setAttribute('aria-pressed',dark?'false':'true');
      tbtn.setAttribute('title',dark?'Switch to light':'Switch to dark');
      var l=tbtn.querySelector('.themelbl');
      if(l)l.textContent=dark?'Switch to light theme':'Switch to dark theme';
    }
    var m=document.querySelector('meta[name="theme-color"]:not([media])');
    if(m)m.setAttribute('content',dark?'#0d1015':'#ffffff');
  }
  var saved=stored();
  if(saved==='dark'||saved==='light')root.setAttribute('data-theme',saved);
  paintTheme();
  if(tbtn)tbtn.addEventListener('click',function(){
    var next=isDark()?'light':'dark';
    root.setAttribute('data-theme',next);remember(next);paintTheme();
  });
  if(window.matchMedia){
    var mq=window.matchMedia('(prefers-color-scheme: light)');
    var onmq=function(){if(!root.getAttribute('data-theme'))paintTheme()};
    if(mq.addEventListener)mq.addEventListener('change',onmq);
    else if(mq.addListener)mq.addListener(onmq);
  }

  /* ---- section switching ----------------------------------------------
     Progressive enhancement, and it is load-bearing. An earlier version hid every
     section by default and revealed one from script, so with JS off the page rendered
     blank. Sections are visible by default; this opts into switching by setting .js on
     the root. Everything degrades to a long, complete document. */
  var links=[].slice.call(document.querySelectorAll('a[data-go]'));
  var secs=[].slice.call(document.querySelectorAll('section[id]'));
  if(!links.length||!secs.length)return;
  root.classList.add('js');

  var wrap=document.querySelector('.sheetwrap');
  var btn=document.querySelector('.menubtn');
  var cur=document.querySelector('.menubtn .cur');
  var main=document.querySelector('.main');
  var lastFocus=null;

  function closeSheet(restore){
    if(!wrap||!wrap.classList.contains('open'))return;
    wrap.classList.remove('open');
    if(btn)btn.setAttribute('aria-expanded','false');
    if(restore&&lastFocus&&lastFocus.focus)lastFocus.focus();
  }
  function openSheet(){
    if(!wrap)return;
    lastFocus=document.activeElement;
    wrap.classList.add('open');
    if(btn)btn.setAttribute('aria-expanded','true');
    var on=wrap.querySelector('a.on')||wrap.querySelector('a');
    if(on)on.focus();
  }

  /* The desktop shell scrolls .main, not the document, so a section change has to
     reset whichever of the two is actually the scroller. Resetting only the window
     left the reader half way down the previous section's table. */
  function toTop(){
    try{window.scrollTo(0,0)}catch(e){}
    if(main&&main.scrollTop)main.scrollTop=0;
  }

  /* The initial view writes no fragment, and that is a fix rather than a nicety.
     replaceState('#overview') on load left Chromium scrolled 370px down on a phone --
     past the whole status strip, so the commit, the branch and the two gate states
     were off screen before the reader touched anything. The hash is now written only
     when a person navigates. */
  function show(id,focus,hash){
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
    if(hash)try{history.replaceState(null,'','#'+id)}catch(e){}
    if(focus){var s=document.getElementById(id);if(s)s.focus({preventScroll:true});toTop();}
    return true;
  }

  links.forEach(function(a){
    a.addEventListener('click',function(e){
      e.preventDefault();
      show(a.getAttribute('data-go'),true,true);
      closeSheet(false);
    });
  });

  if(btn&&wrap){
    btn.addEventListener('click',function(){
      wrap.classList.contains('open')?closeSheet(true):openSheet();
    });
    var scrim=wrap.querySelector('.scrim');
    if(scrim)scrim.addEventListener('click',function(){closeSheet(true)});
    /* Tab must not walk out of an open sheet into the document behind the scrim,
       where the focus ring is invisible and the reader is lost. */
    wrap.addEventListener('keydown',function(e){
      if(e.key!=='Tab')return;
      var f=[].slice.call(wrap.querySelectorAll('a[data-go]'));
      if(!f.length)return;
      var first=f[0],last=f[f.length-1];
      if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus()}
      else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus()}
    });
  }
  document.addEventListener('keydown',function(e){
    if(e.key==='Escape')closeSheet(true);
  });
  window.addEventListener('hashchange',function(){
    show((location.hash||'').replace('#',''),false,false);
  });

  var start=(location.hash||'').replace('#','');
  if(!show(start,false,false))show(secs[0].id,false,false);
  /* A deep link scrolls the document to the section before this script hides the
     others; once they are hidden that offset points at nothing. */
  toTop();
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
    blocked = phase.get("blockedOn") or []
    deps = (state or {}).get("blockedDependencies") or []
    backlog = (state or {}).get("backlog") or []
    open_proposals = [p for _, p in proposals if p.get("status") == "open"]
    open_escalations = [e for _, e in escalations if e.get("status") == "open"]
    not_ready = [r for _, r in rulings if (r.get("gateReadiness") or {}).get("state") == "not_ready"]
    open_challenges = [c for _, c in challenges if c.get("status") == "open"]

    # Global status: the two gates a reader asks about first, each shown as what it
    # actually is. G2 has a real recorded verdict; G3's live result is in Actions and
    # no checkout can read it, so it says so rather than staying blank.
    g2 = [v for _, v in verdicts if v.get("gate") == "G2"]
    g2 = max(g2, key=lambda v: v.get("evaluatedAt", ""), default=None)
    g2_cell = (chip(g2["verdict"], "ok" if g2["verdict"] == "pass" else "bad")
               if g2 else unknown("No G2 verdict record on disk."))
    g3_cell = unavailable("G3 runs in CI. Its result lives in GitHub Actions and cannot "
                          "be read from a checkout.")

    agent_total = sum(len(d.get("agents") or []) for _, d in agent_files)
    agent_active = sum(1 for _, d in agent_files for a in (d.get("agents") or [])
                       if a.get("status") == "active")
    ceiling = ((budgets or {}).get("studioCeiling") or {}).get("hardStop", "?")
    records = sum(len(x) for x in (orders, verdicts, challenges, rulings, evidence,
                                   decisions, proposals, escalations, events, agent_files))

    facts = {
        "queue": queue, "blocked": blocked, "deps": deps, "backlog": backlog,
        "orders": orders, "evidence": evidence, "proposals": proposals,
        "escalations": escalations, "open_proposals": open_proposals,
        "open_escalations": open_escalations, "open_challenges": open_challenges,
        "not_ready": not_ready, "active": agent_active, "total": agent_total,
        "g2_cell": g2_cell, "g3_cell": g3_cell, "ceiling": ceiling, "records": records,
    }

    # (id, rail label, count badge, title, note, body). One list drives both the rail and
    # the sections, so a panel cannot exist without navigation to it, or the reverse.
    #
    # GROUPS is the reading order of the studio itself: what needs a person, what is
    # being built, who is building it, and what the studio has written down. Fourteen
    # flat entries is a list; four groups is a control surface.
    GROUPS = [
        ("Command", ["overview", "blocked"]),
        ("Delivery", ["work", "backlog", "gates", "validation"]),
        ("Organisation", ["agents", "budget"]),
        ("Record", ["decisions", "questions", "proposals", "escalations", "events", "audit"]),
    ]
    panels = [
        # Reading order: the instruments, then the one list nobody else can act on,
        # then the page's own footnotes. The first pass led with two paragraphs about
        # what the page is, which on a phone put every actual number below the fold.
        ("overview", "Overview", len(queue), "Studio status",
         "No health score and no percent-complete: neither exists on disk. Where the "
         "honest answer is a state rather than a number, the tile carries the state.",
         panel_pulse(facts)
         + '<h3 class="sub">Waiting on the founder</h3>'
         + '<p class="note">Nobody else in the studio can clear these.</p>'
         + panel_founder_queue(state)
         + '<h3 class="sub">How to read this page</h3>' + BANNER
         # The four states are defined in the context column, which only exists from
         # 1400px. Below that the column is gone and the chips would be four unexplained
         # words, so the legend is repeated here and hidden again where the column
         # returns -- one legend on screen at any width, never two.
         + '<div class="narrowonly">' + legend() + '</div>'),
        ("blocked", "Blocked", len(blocked) or None, "Blocked",
         "What the studio says is stopping it, as project-state records it.",
         panel_blocked(state)),
        ("gates", "Gates", None, "Gate ladder",
         "Status is the latest gate-verdict record on disk. It is not a live CI result, and "
         "the two are not the same claim.", panel_gates(gates, verdicts)),
        ("work", "Work orders", len(orders), "Work orders",
         "The orders that have been written, from <code>Studio/orders</code>.",
         panel_orders(orders, verdicts)),
        ("backlog", "Backlog", len(backlog) or None, "Backlog",
         "What <code>project-state.json</code> lists as queued, dispatched or blocked. "
         "This is a longer list than the work orders above, because not every backlog "
         "item has had an order written for it yet; the two are shown apart rather than "
         "merged, so neither borrows the other's authority.",
         panel_backlog(state)),
        ("agents", "Agents", None, "The hundred agents", None,
         panel_agents(agent_files) + '<h3 class="sub">Per-agent state</h3>'
         + panel_agent_state(state)),
        ("validation", "Test & validation", len(evidence), "Test and validation", None,
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
    group_of = {sid: g for g, ids in GROUPS for sid in ids}

    # Every panel must appear in exactly one group, or the rail silently loses a
    # section. check() enforces nav/section agreement, but failing here names the cause.
    placed = {s for _, ids in GROUPS for s in ids}
    missing = [p[0] for p in panels if p[0] not in placed]
    if missing:
        raise SystemExit(f"panels missing from GROUPS: {missing}")

    def nav():
        out = ""
        for gname, entries in grouped:
            links = ""
            for sid, label, count, _, _, _ in entries:
                n = f'<span class="n">{count}</span>' if count is not None else ""
                # data-label carries the label the phone's section bar shows. It is
                # escaped exactly once: the labels above are plain text, so passing an
                # already-encoded entity here printed "Test &amp; validation" literally
                # on the button, because the script writes it back as textContent.
                links += (f'<a href="#{sid}" data-go="{sid}" data-label="{esc(label)}">'
                          f'{icon(sid)}<span class="lbl">{esc(label)}</span>{n}</a>')
            out += f'<div class="grp"><h3>{esc(gname)}</h3>{links}</div>'
        return out

    body = "".join(
        section(sid, title, panel_body, note,
                eyebrow=group_of.get(sid), count=None if sid == "overview" else count)
        # The overview's own count would be the founder queue, which the first tile
        # states in full a line below. A number with two meanings on one screen is worse
        # than no number.
        for sid, _, count, title, note, panel_body in panels)

    brand = ('<div class="brand"><span class="mark" aria-hidden="true">J</span>'
             '<h1 class="word">JARV<em>I</em>S<span class="vh"> &mdash; studio '
             'operations</span></h1><span class="spacer"></span>' + THEME_BTN + "</div>")

    parts = [
        "<!doctype html>",
        '<html lang="en"><head><meta charset="utf-8">',
        '<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">',
        "<title>JARVIS &middot; Studio Operations</title>",
        '<meta name="description" content="Read-only view of the studio\'s own recorded '
        'state: gates, work orders, evidence, decisions and what is waiting on a person.">',
        # Two media-scoped values so the browser chrome matches whichever theme the
        # system asks for; the script overwrites the unscoped one when a reader picks.
        '<meta name="theme-color" content="#0d1015">',
        '<meta name="theme-color" media="(prefers-color-scheme: light)" content="#ffffff">',
        '<meta name="color-scheme" content="dark light">',
        f"<style>{CSS}</style>",
        "</head><body>",
        '<a class="skip" href="#overview">Skip to content</a>',
        '<div class="app">',
        brand,
        '<div class="top">',
        # Both the phase and the branch carry a title, because on a 1024px laptop the rail,
        # the two gate readings and the generated time leave the header about 250px short
        # of everything it would like to say. Truncation with the full string one hover
        # away is a layout compromise; dropping the value would be a different page.
        f'<div class="ctx"><b title="Phase {esc(phase.get("current", "?"))} '
        f'&mdash; {esc(phase.get("name", "?"))}">'
        f'Phase {esc(phase.get("current", "?"))} &mdash; '
        f'{esc(phase.get("name", "?"))}</b>'
        f'<span>Branch <code class="br" title="{esc(branch)}">{esc(branch)}</code> '
        f'@ <code>{esc(commit)}</code></span>'
        + (' <span class="chip warn">tree dirty</span>' if dirty else "") + "</div>",
        f'<div class="sysbar"><span class="kv"><b>G2</b>{g2_cell}</span>'
        f'<span class="kv"><b>G3</b>{g3_cell}</span>'
        f'<span class="kv"><b>Generated</b>{esc(now)}</span></div>',
        "</div>",
        f'<nav class="rail nav" aria-label="Sections">{nav()}</nav>',
        '<div class="main"><div class="work" id="work-root">',
        body,
        f'<footer>Regenerate with <code>python3 Studio/jarvis/build-jarvis.py</code> '
        f'(<code>--check</code> validates the output). Reads {len(orders)} order(s), '
        f'{len(verdicts)} verdict(s), {len(rulings)} ruling(s), {len(evidence)} evidence '
        f'record(s), {len(decisions)} decision(s), {len(proposals)} proposal(s), '
        f'{len(events)} event(s), {len(agent_files)} division file(s). '
        f'Generated {esc(now)} from <code>{esc(branch)}</code> at '
        f'<code>{esc(commit)}</code>.</footer>',
        "</div>",
        # The context aside carries what the page cannot know and where its numbers came
        # from -- the two questions a reader of a dashboard should always be able to
        # answer. It is not a place for decoration.
        '<aside class="aside" aria-label="Context">',
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
        "</body></html>",
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
    nav_labels, headings, cells_without_label, unwrapped_cells = [], [], [], []
    counts = {"h1": 0, "viewport": 0, "lang": 0, "theme": 0, "td": 0}
    open_td = [False]

    class Reader(HTMLParser):
        def handle_starttag(self, tag, attrs):
            a = dict(attrs)
            if tag == "html" and a.get("lang"):
                counts["lang"] += 1
            if tag == "meta" and a.get("name") == "viewport":
                counts["viewport"] += 1
            if tag == "button" and "themebtn" in (a.get("class") or "").split():
                counts["theme"] += 1
            if tag == "section" and a.get("id"):
                seen_sections.add(a["id"])
            if tag in ("h1", "h2", "h3"):
                headings.append(tag)
                if tag == "h1":
                    counts["h1"] += 1
            if tag == "a" and a.get("data-go"):
                seen_navs.add(a["data-go"])
                nav_labels.append(a.get("data-label") or "")
            if tag == "span" and "chip" in (a.get("class") or "").split():
                chip_classes.update(c for c in a["class"].split() if c != "chip")
            if tag == "td":
                counts["td"] += 1
                open_td[0] = True
                if not a.get("data-label"):
                    cells_without_label.append(a)
            elif open_td[0]:
                # The first thing inside a cell must be the value wrapper; see table().
                open_td[0] = False
                if not (tag == "div" and "v" in (a.get("class") or "").split()):
                    unwrapped_cells.append(tag)

        def handle_endtag(self, tag):
            if tag == "td":
                open_td[0] = False

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

    # The four states must all still be reachable in the vocabulary. UNKNOWN and
    # UNAVAILABLE collapsing into one another is the specific regression this page was
    # built to prevent, and it would be invisible -- both render as a grey dashed chip.
    for kind in ("unknown", "unavailable"):
        if f"state-{kind}" not in chip_classes:
            problems.append(
                f"no cell on the page renders {STATES[kind][0]}. The two are different "
                f"claims and the page must keep saying which one it means.")

    # Structure the shell depends on.
    if counts["lang"] != 1:
        problems.append("the document must declare <html lang=...> exactly once.")
    if counts["viewport"] != 1:
        problems.append("the viewport meta is missing; the phone layout needs it.")
    if counts["h1"] != 1:
        problems.append(f"expected exactly one h1, found {counts['h1']}.")
    if headings and headings[0] != "h1":
        problems.append(f"the first heading is {headings[0]}, not h1.")
    if counts["theme"] != 1:
        problems.append(
            f"expected exactly one theme control, found {counts['theme']}. The script "
            f"binds the first one it finds, so a second copy is a button that does "
            f"nothing wherever CSS happens to show it.")

    # Both phone affordances rest on these two, and neither fails loudly at runtime.
    if cells_without_label:
        problems.append(
            f"{len(cells_without_label)} table cell(s) carry no data-label. The phone "
            f"layout drops the header row and renders that attribute in its place, so a "
            f"cell without one becomes an unlabelled value.")
    if unwrapped_cells:
        problems.append(
            f"table cell(s) whose content is not wrapped in div.v: {sorted(set(unwrapped_cells))}. "
            f"The phone layout makes each cell a grid, and every unwrapped child becomes "
            f"its own grid item under the wrong label.")

    # Escaping applied twice reaches the reader as literal entity text, and only on the
    # phone -- the bar writes data-label back as textContent.
    bad_labels = [l for l in nav_labels if "&" in l and ";" in l]
    if bad_labels:
        problems.append(f"nav labels look double-escaped: {sorted(set(bad_labels))}")

    if not problems:
        print(f"check: OK -- {len(seen_sections)} sections, all reachable, "
              f"{len(chip_classes)} chip kinds all in vocabulary, "
              f"{counts['td']} cells labelled and wrapped, "
              f"UNKNOWN and UNAVAILABLE both present, shell structure intact")
        return 0

    for p in problems:
        print(f"check: FAIL -- {p}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    path, sections = build()
    print(path.relative_to(ROOT).as_posix())
    if "--check" in sys.argv:
        sys.exit(check(path, sections))
