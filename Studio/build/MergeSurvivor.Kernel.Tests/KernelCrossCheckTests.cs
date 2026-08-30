using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace MergeSurvivor.Kernel.Tests
{
    /// <summary>
    /// Kernel-level invariants that span documents but are not about the organisation
    /// chart. Everything concerning the agent roster, divisions, budgets and permissions
    /// now lives in OrgCrossCheckTests; this file keeps the checks that would still
    /// matter if the studio had one agent.
    /// </summary>
    [TestFixture]
    public class KernelCrossCheckTests
    {
        private static JsonNode ProjectState => Kernel.ReadRepoJson("Studio/state/project-state.json");

        private static IEnumerable<string> Strings(JsonNode node) =>
            node == null ? Enumerable.Empty<string>() : node.AsArray().Select(n => n.GetValue<string>());

        [Test]
        public void EverySchemaIdMatchesItsFileName()
        {
            // A mismatched $id breaks every $ref pointing at it, and the failure mode is a
            // schema that quietly validates nothing rather than an error.
            foreach (string file in Directory.GetFiles(Kernel.SchemaDir, "*.schema.json"))
            {
                JsonNode doc = Kernel.ReadJson(file);
                string id = doc["$id"].GetValue<string>();
                string expected = "https://mergesurvivor.studio/kernel/" + Path.GetFileName(file);

                Assert.That(id, Is.EqualTo(expected), $"{Path.GetFileName(file)} has the wrong $id.");
            }
        }

        [Test]
        public void EveryOpenEscalationInStateExistsOnDisk()
        {
            foreach (string id in Strings(ProjectState["openEscalations"]))
            {
                string path = Path.Combine(Kernel.RepoRoot, "Studio", "state", "escalations", id + ".json");
                Assert.That(File.Exists(path), Is.True, $"Project state references missing escalation {id}.");
            }
        }

        [Test]
        public void EveryOpenEscalationIsListedInProjectState()
        {
            // The direction that actually protects the founder. Without it an escalation
            // can sit open on disk while project state reports none, so the question
            // silently never reaches the daily digest.
            string dir = Path.Combine(Kernel.RepoRoot, "Studio", "state", "escalations");
            if (!Directory.Exists(dir))
            {
                Assert.Pass("No escalations recorded yet.");
            }

            var listed = new HashSet<string>(Strings(ProjectState["openEscalations"]));

            foreach (string file in Directory.GetFiles(dir, "ESC-*.json"))
            {
                JsonNode escalation = Kernel.ReadJson(file);
                if (escalation["status"].GetValue<string>() != "open")
                {
                    continue;
                }

                string id = escalation["id"].GetValue<string>();
                Assert.That(listed, Contains.Item(id),
                    $"{id} is open on disk but missing from project-state.openEscalations, so it would never reach the daily digest.");
            }
        }

        [Test]
        public void NoTwoLiveRecordsShareAnIdentifier()
        {
            // Records are addressed by id across the whole studio — a challenge cites a
            // proposal, an escalation cites a decision — so a duplicate id silently points
            // two references at different documents. Each record validates fine on its own,
            // which is exactly why nothing caught the first collision: the engineering
            // director filed a real PRO-0001 while a fixture already claimed that id.
            //
            // Fixtures use a reserved 9xxx range so they can never collide with live work.
            // evidence and state/rulings were both missing from this list, and both
            // omissions were mine. Rulings because I added the directory and its schema
            // without extending the check that exists to stop exactly this; evidence
            // because the original list was written before evidence ids were
            // cross-referenced by anything. A verifier then filed a second document
            // claiming EVD-0005 and nothing objected -- the same collision this test was
            // written for after PRO-0001, recurring in the two directories it did not
            // look at. Evidence ids are cited by gates and rulings, so a duplicate makes
            // "the evidence for criterion 11" ambiguous between two documents.
            // Enumerated by hand, and wrong twice: state/rulings missing because I created
            // that directory without extending this check, evidence missing because the
            // list predates evidence ids being cited by anything. Naming subdirectories
            // individually reproduces the same failure on the next directory anyone adds
            // -- RUL-0004 called that the generator still running behind the instances.
            // The roots are walked recursively instead, so the default on omission is
            // "checked" rather than "silent".
            string[] roots = { "state", "decisions", "evidence", "orders" };
            var seen = new Dictionary<string, string>();

            foreach (string sub in roots)
            {
                string dir = Path.Combine(Kernel.RepoRoot, "Studio", sub.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                {
                    JsonNode doc = Kernel.ReadJson(file);
                    if (doc["id"] == null)
                    {
                        continue;
                    }

                    string id = doc["id"].GetValue<string>();
                    string relative = Path.GetRelativePath(Kernel.RepoRoot, file);

                    Assert.That(seen.ContainsKey(id), Is.False,
                        $"{relative} and {(seen.TryGetValue(id, out string other) ? other : "?")} both claim id '{id}'. Every cross-reference to it is now ambiguous.");

                    seen[id] = relative;
                }
            }
        }

        [Test]
        public void FixtureIdentifiersStayOutOfTheLiveRange()
        {
            // Keeps the collision above from recurring: an example record must never be
            // addressable by the same id as real work.
            foreach (string file in Kernel.FixtureFiles("valid"))
            {
                JsonNode doc = Kernel.ReadJson(file);
                if (doc is not JsonObject obj || !obj.ContainsKey("id"))
                {
                    continue;
                }

                string id = doc["id"].GetValue<string>();
                if (!id.StartsWith("PRO-", StringComparison.Ordinal) &&
                    !id.StartsWith("CHA-", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(id, Does.Match(@"^(PRO|CHA)-9\d{3}$"),
                    $"{Path.GetFileName(file)} uses live-range id '{id}'. Fixtures use the reserved 9xxx range.");
            }
        }

        [Test]
        public void EscalationsPointAtDecisionsThatExist()
        {
            string dir = Path.Combine(Kernel.RepoRoot, "Studio", "state", "escalations");
            if (!Directory.Exists(dir))
            {
                Assert.Pass("No escalations recorded yet.");
            }

            foreach (string file in Directory.GetFiles(dir, "ESC-*.json"))
            {
                JsonNode escalation = Kernel.ReadJson(file);
                JsonNode decisionId = escalation["decisionId"];
                if (decisionId == null)
                {
                    continue;
                }

                string decisionPath = Path.Combine(
                    Kernel.RepoRoot, "Studio", "decisions", decisionId.GetValue<string>() + ".json");

                Assert.That(File.Exists(decisionPath), Is.True,
                    $"{Path.GetFileName(file)} references {decisionId.GetValue<string>()}, which does not exist.");
            }
        }

        private static IEnumerable<string> WorkOrderDocuments()
        {
            foreach (string file in Kernel.FixtureFiles("valid"))
            {
                if (Path.GetFileName(file).StartsWith("task.", StringComparison.Ordinal))
                {
                    yield return file;
                }
            }

            foreach (string path in Kernel.ExpandGlob("Studio/orders/**/WO-*.json"))
            {
                yield return Path.Combine(Kernel.RepoRoot, path.Replace('/', Path.DirectorySeparatorChar));
            }
        }

        [TestCaseSource(nameof(WorkOrderDocuments))]
        public void AcceptanceCriteriaAreNotWrittenByTheAgentBeingMeasured(string path)
        {
            JsonNode doc = Kernel.ReadJson(path);
            if (doc["kind"].GetValue<string>() != "work-order")
            {
                Assert.Pass("Not a work order.");
            }

            Assert.That(
                doc["acceptanceCriteriaAuthor"].GetValue<string>(),
                Is.Not.EqualTo(doc["agent"].GetValue<string>()),
                $"{Path.GetFileName(path)}: an agent that defines its own success always succeeds.");
        }

        // ====================================================================
        // Gate verdicts.
        //
        // task.schema.json makes completedByGate a pointer and gate.schema.json
        // defines what it points at, but nothing ever routed a live document to
        // that definition -- so no gate verdict existed in this studio until
        // WO-0008 became the first order to reach the gate with its conditions
        // met and found the last link missing. RUL-0005 refused closure on
        // exactly that.
        //
        // Each rule below is a static predicate so it can be sprung against
        // synthetic input. A live-tree check alone passes vacuously whenever the
        // tree happens to be clean, and would keep passing if the rule were
        // gutted -- the defect RUL-0004 recorded against my last check.
        // ====================================================================

        internal static IEnumerable<string> VerdictFiles() =>
            Directory.Exists(Path.Combine(Kernel.RepoRoot, "Studio", "state", "verdicts"))
                ? Directory.GetFiles(Path.Combine(Kernel.RepoRoot, "Studio", "state", "verdicts"), "*.json")
                : Enumerable.Empty<string>();

        /// <summary>
        /// A completed task must NAME the verdict that authorised it, and that verdict must
        /// exist, be for this gate and this task, and have passed.
        ///
        /// Two earlier versions of this rule were both too weak, in different ways. The
        /// first carried only (Gate, TaskId) in its tuple and never read the verdict field,
        /// so a lone `fail` satisfied it exactly as well as a `pass`. The second required
        /// the latest verdict for the pair to be a `pass`, which closed that hole but left
        /// the pointer a query rather than a pointer: it searched for *a* satisfying verdict
        /// instead of resolving the *named* one. WO-0008 accumulated two passing verdicts
        /// and the order could not say which closed it -- RUL-0008 matter 3 filed that
        /// against itself and called it the lead architecture item.
        ///
        /// completedByVerdictCommit supplies the missing coordinate. A verdict has no id and
        /// needs none: it is keyed by (gate, taskId, commit), and an order already carries
        /// the first two, so the commit completes the key without restating anything that
        /// could then disagree. Matching is on record CONTENTS, never on the emitter's
        /// filename convention, so a renamed file still resolves.
        ///
        /// Four clauses, each answering one way a closure can lie:
        ///   1. not completed        -> nothing to prove
        ///   2. exists, and is for this gate and task   (dangling, wrong-task, wrong-gate)
        ///   3. that verdict passed                     (closing on red)
        ///   4. it was the latest verdict available at completedAt   (stale)
        ///
        /// Clause 4 is the supersession policy, authorised by the founder in writing after
        /// it was put to them as a trade, because it supersedes the "latest must be pass"
        /// rule and L-002 forbids replacing a passing assertion's meaning otherwise. A
        /// closed order is a historical claim about a named tree; a `fail` filed later at an
        /// unrelated commit is a different fact and belongs in a challenge, which is where
        /// RUL-0008 put it. The alternative reds every closed order in the studio the day
        /// main breaks, and a check that cries wolf that loudly gets ignored -- the disease
        /// G3 had when it reported success on zero tests, inverted.
        ///
        /// Note that no separate "contradicted at the same commit" clause is needed:
        /// emit-gate-verdict.sh writes {gate}-{task}-{commit}.json, so re-running the gate
        /// at the named commit OVERWRITES the record. Re-run G2 at 9132222, get red, and
        /// clause 3 turns the closed order red by itself -- exactly the alarm RUL-0008
        /// asked for, firing only where the tree was actually attested.
        ///
        /// evaluatedAt and completedAt are schema-required ISO-8601 UTC, so ordinal
        /// comparison orders them correctly and this stays a pure predicate that can be
        /// sprung against synthetic input.
        /// </summary>
        internal static bool ClosureNamesThePassingVerdictThatAuthorisedIt(
            string status, string completedByGate, string taskId,
            string completedByVerdictCommit, string completedAt,
            IEnumerable<(string Gate, string TaskId, string Commit, string Verdict, string EvaluatedAt)> verdicts)
        {
            if (status != "completed")
            {
                return true;
            }

            var forThisOrder = verdicts
                .Where(v => v.Gate == completedByGate && v.TaskId == taskId)
                .ToList();

            var named = forThisOrder
                .Where(v => v.Commit == completedByVerdictCommit)
                .ToList();

            // Zero means the pointer dangles. More than one means two records claim the same
            // (gate, taskId, commit), which is a duplicate-record bug rather than a closure
            // this check may wave through.
            if (named.Count != 1)
            {
                return false;
            }

            if (named[0].Verdict != "pass")
            {
                return false;
            }

            // Stale: at the moment of closure, was there a newer evaluation the closer
            // passed over? Verdicts issued after completedAt are outside the window on
            // purpose -- they are later history, not evidence about this closure.
            var availableAtClosure = forThisOrder
                .Where(v => string.CompareOrdinal(v.EvaluatedAt, completedAt) <= 0)
                .OrderBy(v => v.EvaluatedAt, StringComparer.Ordinal)
                .ToList();

            return availableAtClosure.Count > 0
                && availableAtClosure[availableAtClosure.Count - 1].Commit == completedByVerdictCommit;
        }

        /// <summary>Every evidence id a verdict cites must resolve to a record on disk.</summary>
        internal static bool CitedEvidenceExists(
            IEnumerable<string> cited, ISet<string> knownEvidenceIds) =>
            cited.All(knownEvidenceIds.Contains);

        /// <summary>A verdict must cover every evidence tier its gate declares.</summary>
        internal static bool CoversTheGatesTiers(
            IEnumerable<string> citedTiers, IEnumerable<string> gateTiers) =>
            gateTiers.All(citedTiers.Contains);

        /// <summary>
        /// The invariant that matters most, and the reason authorisation is enforced
        /// mechanically rather than by identity: an implementer may not gate its own work.
        /// </summary>
        internal static bool IsNotSelfIssued(string evaluatedBy, string taskAgent) =>
            evaluatedBy != taskAgent;

        /// <summary>
        /// Evidence must describe a tree the verdict's tree descends from. Written first
        /// as exact equality, which was wrong and was caught by trying to issue a real
        /// verdict rather than by reasoning about one: evidence accumulates across an
        /// order's life, so WO-0008's records name 4f4135e while any verdict closing it
        /// names a later commit. Equality would have made the rule unsatisfiable in
        /// exactly the case it exists for. Ancestry is the invariant that was meant --
        /// evidence may not come from the future or from an unrelated branch.
        /// </summary>
        internal static bool EvidenceIsNotFromTheFuture(
            string verdictCommit, IEnumerable<string> evidenceCommits, Func<string, string, bool> isAncestorOrSame) =>
            evidenceCommits.All(c => isAncestorOrSame(c, verdictCommit));

        internal static bool GitSaysAncestorOrSame(string maybeAncestor, string descendant)
        {
            if (string.IsNullOrEmpty(maybeAncestor) || string.IsNullOrEmpty(descendant))
            {
                return false;
            }

            if (maybeAncestor == descendant)
            {
                return true;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = Kernel.RepoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("merge-base");
            psi.ArgumentList.Add("--is-ancestor");
            psi.ArgumentList.Add(maybeAncestor);
            psi.ArgumentList.Add(descendant);

            using var p = System.Diagnostics.Process.Start(psi);
            p.WaitForExit();
            return p.ExitCode == 0;
        }

        private static IEnumerable<TestCaseData> VerdictRuleCases()
        {
            // Modelled on the real shape of WO-0008: two passing verdicts for one order, the
            // case that made the ambiguity concrete. CLOSED_AT is after both, as the live
            // orders' completedAt is after both of theirs.
            const string closedAt = "2026-08-30T15:30:00Z";
            var twoPasses = new[]
            {
                ("G2", "WO-0008", "0b3d5c3", "pass", "2026-08-30T08:58:51Z"),
                ("G2", "WO-0008", "9132222", "pass", "2026-08-30T09:17:37Z"),
            };

            yield return new TestCaseData(ClosureNamesThePassingVerdictThatAuthorisedIt("completed", "G2", "WO-0008", "9132222", closedAt, twoPasses), true).SetName("names the verdict that exists and passed");

            // The substitution the (gate, taskId) query could not see. Naming 0b3d5c3 is
            // rejected because a later verdict existed at closure -- which independently
            // reproduces RUL-0005 condition 4, that the premature verdict could not be the
            // one to close WO-0008.
            yield return new TestCaseData(ClosureNamesThePassingVerdictThatAuthorisedIt("completed", "G2", "WO-0008", "0b3d5c3", closedAt, twoPasses), false).SetName("stale: names an earlier pass while a later verdict existed at closure");

            yield return new TestCaseData(ClosureNamesThePassingVerdictThatAuthorisedIt("completed", "G2", "WO-0008", "deadbee", closedAt, twoPasses), false).SetName("different verdict: names a commit no verdict carries");

            var otherTask = new[] { ("G2", "WO-0009", "9132222", "pass", "2026-08-30T09:17:37Z") };
            yield return new TestCaseData(ClosureNamesThePassingVerdictThatAuthorisedIt("completed", "G2", "WO-0008", "9132222", closedAt, otherTask), false).SetName("wrong task: a verdict at that commit exists, for another order");

            var otherGate = new[] { ("G3", "WO-0008", "9132222", "pass", "2026-08-30T09:17:37Z") };
            yield return new TestCaseData(ClosureNamesThePassingVerdictThatAuthorisedIt("completed", "G2", "WO-0008", "9132222", closedAt, otherGate), false).SetName("wrong gate: a verdict at that commit exists, from a gate the order did not close on");

            var failAtNamedCommit = new[] { ("G2", "WO-0008", "9132222", "fail", "2026-08-30T09:17:37Z") };
            yield return new TestCaseData(ClosureNamesThePassingVerdictThatAuthorisedIt("completed", "G2", "WO-0008", "9132222", closedAt, failAtNamedCommit), false).SetName("fail verdict: the named record exists and is red");

            // emit-gate-verdict.sh overwrites {gate}-{task}-{commit}.json, so re-running the
            // gate at the named commit replaces the record. This is that case, and it is why
            // no separate contradiction clause is needed.
            var reRunTurnedRed = new[]
            {
                ("G2", "WO-0008", "0b3d5c3", "pass", "2026-08-30T08:58:51Z"),
                ("G2", "WO-0008", "9132222", "fail", "2026-08-30T11:00:00Z"),
            };
            yield return new TestCaseData(ClosureNamesThePassingVerdictThatAuthorisedIt("completed", "G2", "WO-0008", "9132222", closedAt, reRunTurnedRed), false).SetName("a re-run that reddened the named commit reds the closure");

            var verdictAfterClosure = new[] { ("G2", "WO-0008", "9132222", "pass", "2026-08-30T16:00:00Z") };
            yield return new TestCaseData(ClosureNamesThePassingVerdictThatAuthorisedIt("completed", "G2", "WO-0008", "9132222", closedAt, verdictAfterClosure), false).SetName("a verdict evaluated after the closure cannot have authorised it");

            // fail -> fix -> pass is the normal history of a repaired defect. Naming the
            // pass stays legal; forbidding it would make the ratchet unclosable.
            var failThenPass = new[]
            {
                ("G2", "WO-0008", "0b3d5c3", "fail", "2026-08-30T09:00:00Z"),
                ("G2", "WO-0008", "9132222", "pass", "2026-08-30T10:00:00Z"),
            };
            yield return new TestCaseData(ClosureNamesThePassingVerdictThatAuthorisedIt("completed", "G2", "WO-0008", "9132222", closedAt, failThenPass), true).SetName("fix then re-gate stays legal");

            // The supersession policy, stated as a test so it cannot drift silently: a
            // verdict filed after a correct closure does not retroactively invalidate it.
            var laterFailAfterClosure = new[]
            {
                ("G2", "WO-0008", "9132222", "pass", "2026-08-30T09:17:37Z"),
                ("G2", "WO-0008", "aaaaaaa", "fail", "2026-08-31T09:00:00Z"),
            };
            yield return new TestCaseData(ClosureNamesThePassingVerdictThatAuthorisedIt("completed", "G2", "WO-0008", "9132222", closedAt, laterFailAfterClosure), true).SetName("a fail at a later unrelated commit does not unclose a correct closure");

            yield return new TestCaseData(ClosureNamesThePassingVerdictThatAuthorisedIt("completed", "G2", "WO-0009", "9132222", closedAt, twoPasses), false).SetName("closure whose verdict is missing");
            yield return new TestCaseData(ClosureNamesThePassingVerdictThatAuthorisedIt("dispatched", "", "WO-0009", "", "", twoPasses), true).SetName("an open order needs no verdict");

            var known = new HashSet<string> { "EVD-0010", "EVD-0011" };
            yield return new TestCaseData(CitedEvidenceExists(new[] { "EVD-0010" }, known), true).SetName("cited evidence exists");
            yield return new TestCaseData(CitedEvidenceExists(new[] { "EVD-0010", "EVD-9999" }, known), false).SetName("cited evidence that does not exist");

            yield return new TestCaseData(CoversTheGatesTiers(new[] { "T0", "T1" }, new[] { "T0", "T1" }), true).SetName("covers both G2 tiers");
            yield return new TestCaseData(CoversTheGatesTiers(new[] { "T1" }, new[] { "T0", "T1" }), false).SetName("missing the T0 tier");

            yield return new TestCaseData(IsNotSelfIssued("ceo-orchestrator", "core-simulation-engineer"), true).SetName("issued by someone other than the implementer");
            yield return new TestCaseData(IsNotSelfIssued("core-simulation-engineer", "core-simulation-engineer"), false).SetName("the implementer gating its own work");

            // Sprung against real commits in this repository, which is stronger than
            // synthetic ones here: 4f4135e genuinely precedes b0dfe2d, and the reverse
            // is genuinely false, so the case cannot drift out of meaning.
            yield return new TestCaseData(EvidenceIsNotFromTheFuture("b0dfe2d", new[] { "4f4135e", "b0dfe2d" }, GitSaysAncestorOrSame), true).SetName("evidence precedes or equals the verdict's tree");
            yield return new TestCaseData(EvidenceIsNotFromTheFuture("4f4135e", new[] { "b0dfe2d" }, GitSaysAncestorOrSame), false).SetName("evidence from a tree the verdict cannot descend from");
        }

        [TestCaseSource(nameof(VerdictRuleCases))]
        public void GateVerdictRulesHoldAgainstSyntheticCases(bool actual, bool expected)
        {
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void EveryCompletedTaskIsBackedByAGateVerdict()
        {
            var verdicts = VerdictFiles()
                .Select(Kernel.ReadJson)
                .Select(v => (
                    Gate: v["gate"].GetValue<string>(),
                    TaskId: v["taskId"].GetValue<string>(),
                    Commit: v["commit"].GetValue<string>(),
                    Verdict: v["verdict"].GetValue<string>(),
                    EvaluatedAt: v["evaluatedAt"].GetValue<string>()))
                .ToList();

            foreach (string path in Kernel.ExpandGlob("Studio/orders/**/WO-*.json"))
            {
                JsonNode order = Kernel.ReadRepoJson(path);
                string status = order["status"].GetValue<string>();
                string gate = order["completedByGate"]?.GetValue<string>() ?? "";
                string commit = order["completedByVerdictCommit"]?.GetValue<string>() ?? "";
                string closedAt = order["completedAt"]?.GetValue<string>() ?? "";
                string id = order["id"].GetValue<string>();

                // Named rather than merely counted, so a 7-versus-40-character SHA reads as
                // itself instead of as a missing verdict.
                string candidates = string.Join(
                    ", ",
                    verdicts.Where(v => v.Gate == gate && v.TaskId == id)
                        .OrderBy(v => v.EvaluatedAt, StringComparer.Ordinal)
                        .Select(v => $"{v.Commit} ({v.Verdict} at {v.EvaluatedAt})"));

                Assert.That(
                    ClosureNamesThePassingVerdictThatAuthorisedIt(status, gate, id, commit, closedAt, verdicts),
                    Is.True,
                    $"{id} is completed naming {gate} verdict at commit '{commit}', but that is not a passing "
                    + $"verdict for this order that was current at {closedAt}. Verdicts on disk for ({gate}, {id}): "
                    + (candidates.Length == 0 ? "none." : candidates + "."));
            }
        }

        [Test]
        public void EveryGateVerdictIsWellFounded()
        {
            var evidence = Kernel.ExpandGlob("Studio/evidence/**/EVD-*.json")
                .Select(Kernel.ReadRepoJson)
                .ToDictionary(e => e["id"].GetValue<string>(), e => e);
            var knownIds = new HashSet<string>(evidence.Keys);

            JsonNode registry = Kernel.ReadRepoJson("Studio/constitution/gates.json");
            var orders = Kernel.ExpandGlob("Studio/orders/**/WO-*.json")
                .Select(Kernel.ReadRepoJson)
                .ToDictionary(o => o["id"].GetValue<string>(), o => o);

            foreach (string path in VerdictFiles())
            {
                JsonNode v = Kernel.ReadJson(path);
                string name = Path.GetFileName(path);
                var cited = Strings(v["evidence"]).ToList();

                Assert.That(CitedEvidenceExists(cited, knownIds), Is.True,
                    $"{name} cites evidence that does not exist: {string.Join(", ", cited.Where(c => !knownIds.Contains(c)))}");

                JsonNode gate = registry["gates"].AsArray()
                    .First(g => g["id"].GetValue<string>() == v["gate"].GetValue<string>());
                var gateTiers = Strings(gate["evidenceTiers"]).ToList();
                var citedTiers = cited.Select(c => evidence[c]["tier"].GetValue<string>()).ToList();

                Assert.That(CoversTheGatesTiers(citedTiers, gateTiers), Is.True,
                    $"{name} does not cover {v["gate"].GetValue<string>()}'s evidence tiers. Required {string.Join("/", gateTiers)}, cited {string.Join("/", citedTiers)}.");

                Assert.That(EvidenceIsNotFromTheFuture(v["commit"].GetValue<string>(), cited.Select(c => evidence[c]["commit"]?.GetValue<string>() ?? ""), GitSaysAncestorOrSame), Is.True,
                    $"{name} cites evidence from a tree its own commit does not descend from, so the verdict cannot be re-checked against it.");

                string taskId = v["taskId"].GetValue<string>();
                if (orders.TryGetValue(taskId, out JsonNode order))
                {
                    Assert.That(IsNotSelfIssued(v["evaluatedBy"].GetValue<string>(), order["agent"].GetValue<string>()), Is.True,
                        $"{name} was issued by the same agent the order measures. An implementer may not gate its own work.");
                }
            }
        }

        [Test]
        public void SelfAuthoredAcceptanceCriteriaAreActuallyCaught()
        {
            // Proves the check above is not vacuous. This fixture is schema-valid on
            // purpose — the rule it breaks is one JSON Schema cannot see.
            string path = Path.Combine(
                Kernel.FixtureDir, "cross-check-invalid", "task.self-authored-acceptance-criteria.json");

            JsonNode doc = Kernel.ReadJson(path);

            Assert.That(
                doc["acceptanceCriteriaAuthor"].GetValue<string>(),
                Is.EqualTo(doc["agent"].GetValue<string>()),
                "The cross-check fixture no longer demonstrates the violation it exists to demonstrate.");
        }
    }
}
