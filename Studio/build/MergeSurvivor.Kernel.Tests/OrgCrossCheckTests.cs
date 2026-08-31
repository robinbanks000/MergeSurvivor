using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace MergeSurvivor.Kernel.Tests
{
    /// <summary>
    /// Proves the organisation is real rather than nominal, at whatever size it is.
    ///
    /// These are the checks that make "no filler agents" enforceable instead of
    /// aspirational: an agent whose measurable output duplicates another's, whose
    /// reporting line does not resolve, or whose tools cannot produce anything, fails
    /// the build. Structure alone cannot prove a role is worth having — but it can
    /// prove that a role is distinguishable, reachable, funded and able to act, and
    /// those four are where filler is caught.
    ///
    /// Since ADR-0005 they carry that load alone: the roster has no fixed size, so the
    /// count is no longer a backstop and these checks are the only thing standing
    /// between a justified hire and sprawl.
    /// </summary>
    [TestFixture]
    public class OrgCrossCheckTests
    {
        private static JsonNode Org => Kernel.ReadRepoJson("Studio/constitution/org.json");
        private static JsonNode Permissions => Kernel.ReadRepoJson("Studio/constitution/permissions.json");
        private static JsonNode Budgets => Kernel.ReadRepoJson("Studio/constitution/budgets.json");
        private static JsonNode Gates => Kernel.ReadRepoJson("Studio/constitution/gates.json");
        private static JsonNode Memory => Kernel.ReadRepoJson("Studio/constitution/memory.json");

        /// <summary>Every agent in the roster, flattened across the division files.</summary>
        private static List<JsonNode> AllAgents()
        {
            var agents = new List<JsonNode>();
            string dir = Path.Combine(Kernel.RepoRoot, "Studio", "constitution", "agents");

            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
            {
                foreach (JsonNode agent in Kernel.ReadJson(file)["agents"].AsArray())
                {
                    agents.Add(agent);
                }
            }

            return agents;
        }

        private static string Id(JsonNode a) => a["id"].GetValue<string>();

        private static IEnumerable<string> Strings(JsonNode node) =>
            node == null ? Enumerable.Empty<string>() : node.AsArray().Select(n => n.GetValue<string>());

        private static Dictionary<string, JsonNode> ById() =>
            AllAgents().ToDictionary(Id, a => a);

        private static string Status(JsonNode a) => a["status"].GetValue<string>();

        /// <summary>
        /// Everyone still on the roster: active or dormant, but not retired.
        ///
        /// The distinction matters to the distinctness checks specifically. A retired
        /// agent stays in the registry by design -- other agents' dependsOn and
        /// challenges lists still name its id, and generate-agent-definitions.sh
        /// resolves display names from the whole file, so deleting the record breaks
        /// both. But leaving it inside the duplicate-output check made retirement
        /// one-way in practice: retire an agent and its measurableOutput is reserved
        /// forever, so no successor could ever claim the work it used to do. A roster
        /// that can only grow is the thing ADR-0004 set out to prevent, and it would
        /// have arrived through the mechanism meant to allow shrinking.
        /// </summary>
        private static List<JsonNode> StaffedAgents() =>
            AllAgents().Where(a => Status(a) != "retired").ToList();

        // ---------- the roster is what it claims to be ----------

        [Test]
        public void EveryDivisionInTheOrgChartIsActuallyStaffed()
        {
            // What replaced RosterIsExactlyOneHundredAgents, and it is a weaker claim on
            // purpose. ADR-0005 removed the fixed count: the roster may grow past a
            // hundred to close a filed capability gap, or shrink below it as roles
            // retire, and neither direction is a defect. Asserting a number would now
            // fail on correct work.
            //
            // What still must hold is that the org chart is not aspirational. A division
            // defined in org.json with nobody in it is a mandate no agent carries, and
            // that failure is silent -- the work simply never gets done and no check
            // notices. So the shape is asserted where the size no longer is.
            var staffed = StaffedAgents()
                .Select(a => a["division"].GetValue<string>())
                .ToHashSet();

            foreach (JsonNode division in Org["divisions"].AsArray())
            {
                string id = division["id"].GetValue<string>();
                Assert.That(staffed, Contains.Item(id),
                    $"Division {id} is defined in the org chart with no agent in it, so its mandate has no owner.");
            }

            Assert.That(StaffedAgents(), Is.Not.Empty, "The roster is empty.");
        }

        [Test]
        public void EveryAgentIdIsUnique()
        {
            List<string> ids = AllAgents().Select(Id).ToList();

            Assert.That(ids, Is.Unique);
        }

        [Test]
        public void EveryAgentIsFiledUnderTheDivisionItClaims()
        {
            string dir = Path.Combine(Kernel.RepoRoot, "Studio", "constitution", "agents");

            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                JsonNode doc = Kernel.ReadJson(file);
                string fileDivision = doc["division"].GetValue<string>();

                foreach (JsonNode agent in doc["agents"].AsArray())
                {
                    Assert.That(agent["division"].GetValue<string>(), Is.EqualTo(fileDivision),
                        $"{Id(agent)} is filed in {fileDivision} but claims division {agent["division"]}.");
                }
            }
        }

        // ---------- the hierarchy resolves ----------

        [Test]
        public void ExactlyOneAgentReportsToTheFounder()
        {
            List<string> topLevel = AllAgents()
                .Where(a => a["reportsTo"].GetValue<string>() == "human")
                .Select(Id)
                .ToList();

            Assert.That(topLevel, Has.Count.EqualTo(1),
                "More than one agent reporting to the founder makes the founder the routing layer, which is what the hierarchy exists to prevent.");
            Assert.That(topLevel[0], Is.EqualTo(Org["chiefExecutive"].GetValue<string>()));
        }

        [Test]
        public void EveryReportingLineResolvesToAnAgentThatExists()
        {
            var known = new HashSet<string>(AllAgents().Select(Id));

            foreach (JsonNode agent in AllAgents())
            {
                string boss = agent["reportsTo"].GetValue<string>();
                if (boss == "human")
                {
                    continue;
                }

                Assert.That(known, Contains.Item(boss),
                    $"{Id(agent)} reports to '{boss}', which is not an agent in the roster.");
            }
        }

        [Test]
        public void NoReportingCycleExists()
        {
            Dictionary<string, JsonNode> byId = ById();

            foreach (JsonNode start in AllAgents())
            {
                var seen = new HashSet<string>();
                string current = Id(start);

                while (current != "human")
                {
                    Assert.That(seen.Add(current), Is.True,
                        $"Reporting cycle reached through {Id(start)}: {string.Join(" -> ", seen)} -> {current}. Nobody would ever be accountable inside that loop.");

                    current = byId[current]["reportsTo"].GetValue<string>();
                }
            }
        }

        [Test]
        public void EverySpecialistReportsInsideItsOwnDivision()
        {
            Dictionary<string, JsonNode> byId = ById();

            foreach (JsonNode agent in AllAgents().Where(a => a["tier"].GetValue<string>() == "specialist"))
            {
                JsonNode boss = byId[agent["reportsTo"].GetValue<string>()];

                Assert.That(boss["division"].GetValue<string>(), Is.EqualTo(agent["division"].GetValue<string>()),
                    $"{Id(agent)} reports outside its division, so its boss is accountable for work they do not own.");
            }
        }

        [Test]
        public void EveryDivisionBossIsTheOneNamedInTheOrgChart()
        {
            Dictionary<string, JsonNode> byId = ById();

            foreach (JsonNode division in Org["divisions"].AsArray())
            {
                string divisionId = division["id"].GetValue<string>();
                string bossId = division["boss"].GetValue<string>();

                Assert.That(byId.ContainsKey(bossId), Is.True,
                    $"Division {divisionId} names boss '{bossId}', who is not in the roster.");

                JsonNode boss = byId[bossId];
                Assert.That(boss["division"].GetValue<string>(), Is.EqualTo(divisionId));
                Assert.That(boss["tier"].GetValue<string>(), Is.AnyOf("boss", "ceo"),
                    $"{bossId} runs a division but is filed as a specialist.");
            }
        }

        [Test]
        public void EveryDivisionInTheRosterExistsInTheOrgChart()
        {
            var known = new HashSet<string>(Org["divisions"].AsArray().Select(d => d["id"].GetValue<string>()));

            foreach (JsonNode agent in AllAgents())
            {
                Assert.That(known, Contains.Item(agent["division"].GetValue<string>()),
                    $"{Id(agent)} belongs to a division the org chart does not define.");
            }
        }

        // ---------- no filler: every role is distinguishable ----------

        [Test]
        public void NoTwoAgentsShareAMeasurableOutput()
        {
            var seen = new Dictionary<string, string>();

            foreach (JsonNode agent in StaffedAgents())
            {
                foreach (string output in Strings(agent["measurableOutput"]))
                {
                    string key = Normalise(output);

                    Assert.That(seen.ContainsKey(key), Is.False,
                        $"{Id(agent)} and {(seen.TryGetValue(key, out string other) ? other : "?")} both claim to produce \"{output}\". Two agents producing the same artifact are one agent.");

                    seen[key] = Id(agent);
                }
            }
        }

        [Test]
        public void NoTwoAgentsHaveNearlyIdenticalOutputs()
        {
            // Exact duplicates are the easy case. This catches the harder one: two roles
            // whose outputs are worded differently but describe the same artifact, which is
            // how a roster quietly acquires filler that passes a duplicate check.
            const double threshold = 0.8;
            List<JsonNode> agents = StaffedAgents();
            var offenders = new List<string>();

            for (int i = 0; i < agents.Count; i++)
            {
                for (int j = i + 1; j < agents.Count; j++)
                {
                    foreach (string a in Strings(agents[i]["measurableOutput"]))
                    {
                        foreach (string b in Strings(agents[j]["measurableOutput"]))
                        {
                            double similarity = Jaccard(Words(a), Words(b));
                            if (similarity >= threshold)
                            {
                                offenders.Add(
                                    $"{Id(agents[i])} vs {Id(agents[j])} ({similarity:0.00}):\n    \"{a}\"\n    \"{b}\"");
                            }
                        }
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Near-duplicate measurable outputs:\n" + string.Join("\n", offenders));
        }

        [Test]
        public void NoTwoAgentsShareASuccessMetric()
        {
            var seen = new Dictionary<string, string>();

            foreach (JsonNode agent in StaffedAgents())
            {
                string key = Normalise(agent["successMetric"].GetValue<string>());

                Assert.That(seen.ContainsKey(key), Is.False,
                    $"{Id(agent)} and {(seen.TryGetValue(key, out string other) ? other : "?")} are measured identically, so one of them is not needed.");

                seen[key] = Id(agent);
            }
        }

        [Test]
        public void EveryAgentCanActuallyProduceSomething()
        {
            // A role whose only capability is reading is a commentator, not a worker. This
            // is the check that separates an agent doing real work in the repository from
            // one that merely generates text about it.
            string[] passiveOnly = { "read" };

            foreach (JsonNode agent in AllAgents())
            {
                List<string> tools = Strings(agent["tools"]).ToList();
                bool canAct = tools.Any(t => !passiveOnly.Contains(t));

                Assert.That(canAct, Is.True,
                    $"{Id(agent)} can only read. Every agent must be able to write, run, measure, dispatch or adjudicate something.");
            }
        }

        [Test]
        public void EveryAgentStatesWhatItProducesAndHowItIsJudged()
        {
            foreach (JsonNode agent in AllAgents())
            {
                Assert.That(Strings(agent["measurableOutput"]).Any(), Is.True, $"{Id(agent)} produces nothing.");
                Assert.That(agent["successMetric"].GetValue<string>().Length, Is.GreaterThan(20),
                    $"{Id(agent)} has no meaningful success metric.");
                Assert.That(agent["existsBecause"].GetValue<string>().Length, Is.GreaterThanOrEqualTo(40),
                    $"{Id(agent)} cannot justify its own existence in a sentence.");
            }
        }

        // ---------- collaboration and dispute resolution actually resolve ----------

        [Test]
        public void EveryDependencyAndChallengeTargetExists()
        {
            var known = new HashSet<string>(AllAgents().Select(Id));

            foreach (JsonNode agent in AllAgents())
            {
                foreach (string target in Strings(agent["dependsOn"]).Concat(Strings(agent["challenges"])))
                {
                    Assert.That(known, Contains.Item(target),
                        $"{Id(agent)} references '{target}', who does not exist.");
                    Assert.That(target, Is.Not.EqualTo(Id(agent)),
                        $"{Id(agent)} depends on or challenges itself.");
                }
            }
        }

        [Test]
        public void EveryChallengeHasAnAdjudicatorAboveBothParties()
        {
            // A dispute that cannot be settled by a common superior would deadlock, which
            // is exactly the failure the challenge mechanism exists to avoid.
            Dictionary<string, JsonNode> byId = ById();

            foreach (JsonNode agent in AllAgents())
            {
                foreach (string target in Strings(agent["challenges"]))
                {
                    var chainA = ChainToTop(byId, Id(agent));
                    var chainB = ChainToTop(byId, target);

                    bool hasCommonSuperior = chainA.Intersect(chainB).Any();

                    Assert.That(hasCommonSuperior, Is.True,
                        $"{Id(agent)} may challenge {target}, but no agent sits above both to rule on it.");
                }
            }
        }

        /// <summary>
        /// Everyone above the given agent, ending at the founder. "human" is included
        /// deliberately: a challenge against the chief executive has no agent above it,
        /// and the founder is the adjudicator of last resort. Omitting them made every
        /// upward challenge look unresolvable.
        /// </summary>
        private static List<string> ChainToTop(Dictionary<string, JsonNode> byId, string start)
        {
            var chain = new List<string>();
            var seen = new HashSet<string>();
            string current = byId[start]["reportsTo"].GetValue<string>();

            // Bail on a cycle instead of walking it forever. NoReportingCycleExists is the
            // test that reports the cycle; without this guard every other test that walks
            // the hierarchy would hang first and bury that diagnosis under a timeout.
            while (current != "human" && seen.Add(current))
            {
                chain.Add(current);
                current = byId[current]["reportsTo"].GetValue<string>();
            }

            chain.Add("human");
            return chain;
        }

        // ---------- cost discipline is structural ----------

        [Test]
        public void DivisionBudgetsFitInsideTheStudioCeiling()
        {
            double ceiling = Budgets["studioCeiling"]["hardStop"].GetValue<double>();
            double sum = Budgets["budgets"].AsArray()
                .Where(b => b["scope"].GetValue<string>() == "division")
                .Sum(b => b["hardStop"].GetValue<double>());

            Assert.That(sum, Is.LessThanOrEqualTo(ceiling),
                $"Division ceilings sum to {sum} against a studio hard stop of {ceiling}. Every division could hit its limit and still overrun the studio.");
        }

        [Test]
        public void EveryBudgetHardStopIsAtLeastItsSoftWarning()
        {
            foreach (JsonNode budget in Budgets["budgets"].AsArray())
            {
                Assert.That(budget["hardStop"].GetValue<double>(),
                    Is.GreaterThanOrEqualTo(budget["softWarn"].GetValue<double>()),
                    $"Budget {budget["id"]} would hard-stop before it warns.");
            }
        }

        [Test]
        public void EveryDivisionAndAgentBudgetResolves()
        {
            var known = new HashSet<string>(Budgets["budgets"].AsArray().Select(b => b["id"].GetValue<string>()));

            foreach (JsonNode division in Org["divisions"].AsArray())
            {
                Assert.That(known, Contains.Item(division["budgetId"].GetValue<string>()),
                    $"Division {division["id"]} names a budget that does not exist.");
            }

            foreach (JsonNode agent in AllAgents())
            {
                Assert.That(known, Contains.Item(agent["budgetId"].GetValue<string>()),
                    $"{Id(agent)} names a budget that does not exist, so its spend would be untracked.");
            }
        }

        [Test]
        public void ExpensiveModelsAreConfinedToTheRolesThatEarnThem()
        {
            string[] allowed = { "ceo-orchestrator", "design-director", "engineering-director", "qa-director" };

            List<string> opus = AllAgents()
                .Where(a => a["model"].GetValue<string>() == "opus")
                .Select(Id)
                .ToList();

            Assert.That(opus, Is.SubsetOf(allowed),
                "At this budget an Opus task costs roughly ten times a batched Haiku one. Widening this list is a budget decision, not a modelling preference.");

            foreach (JsonNode agent in AllAgents().Where(a => a["model"].GetValue<string>() != "haiku"))
            {
                Assert.That(agent["modelJustification"], Is.Not.Null,
                    $"{Id(agent)} runs above the cheap default without saying why.");
            }
        }

        [Test]
        public void MostOfTheRosterRunsOnTheCheapestModel()
        {
            // Staffed only: a retired agent costs nothing, so counting it either way
            // misreports what the studio actually spends per period.
            List<JsonNode> agents = StaffedAgents();
            int haiku = agents.Count(a => a["model"].GetValue<string>() == "haiku");

            Assert.That(haiku, Is.GreaterThan(agents.Count / 2),
                $"Only {haiku} of {agents.Count} agents run on the cheap default. The roster is only affordable because most work is routine.");
        }

        // ---------- staged activation ----------

        [Test]
        public void EveryDormantAgentSaysWhatItIsWaitingFor()
        {
            foreach (JsonNode agent in AllAgents().Where(a => a["status"].GetValue<string>() == "dormant"))
            {
                Assert.That(agent["activatesWhen"], Is.Not.Null,
                    $"{Id(agent)} is dormant with no stated precondition, so nobody knows when it should wake.");
            }
        }

        [Test]
        public void EveryActiveToDormantDependencyIsDeclaredAsABlocker()
        {
            // An active agent waiting on a dormant dependency is a real and sometimes
            // legitimate state — engineering can build Core while design waits on the
            // pillars. What is not acceptable is that blockage being invisible, because
            // a silently blocked agent looks exactly like an agent with nothing to do.
            //
            // So rather than forbidding the situation, the exact set is required to be
            // declared in project state. Activate the dependency and the declaration must
            // shrink, or this fails.
            Dictionary<string, JsonNode> byId = ById();

            var actual = new SortedSet<string>();
            foreach (JsonNode agent in AllAgents().Where(a => a["status"].GetValue<string>() == "active"))
            {
                foreach (string dependency in Strings(agent["dependsOn"]))
                {
                    if (byId[dependency]["status"].GetValue<string>() != "active")
                    {
                        actual.Add($"{Id(agent)} -> {dependency}");
                    }
                }
            }

            JsonNode state = Kernel.ReadRepoJson("Studio/state/project-state.json");
            var declared = new SortedSet<string>(Strings(state["blockedDependencies"]));

            Assert.That(actual, Is.EquivalentTo(declared),
                "The set of active agents blocked on dormant dependencies has changed and project state no longer reflects it.");
        }

        [Test]
        public void EveryRetiredAgentSaysWhyAndWhen()
        {
            // The counterpart of existsBecause, and the schema enforces it too. It is
            // repeated here because the schema only sees one file at a time and this is
            // the check a reader of the roster will actually look at, but mainly because
            // "retired" was in the status enum for weeks while nothing anywhere read it:
            // an agent could be taken out of circulation leaving no reason on the record,
            // which is exactly how a role gets re-created a quarter later.
            foreach (JsonNode agent in AllAgents().Where(a => Status(a) == "retired"))
            {
                Assert.That(agent["retiredBecause"], Is.Not.Null,
                    $"{Id(agent)} is retired without saying why. Hiring needs a reason; so does firing.");
                Assert.That(agent["retiredAt"], Is.Not.Null,
                    $"{Id(agent)} is retired with no date, so no audit can place it in time.");
            }
        }

        [Test]
        public void NoLiveAgentDependsOnOrChallengesARetiredOne()
        {
            // A dormant dependency is a declared wait and EveryActiveToDormantDependency-
            // IsDeclaredAsABlocker already governs it. A retired dependency is different
            // in kind: nothing is coming, so the waiting agent is not blocked but broken,
            // and it would look identical to an agent that simply has nothing to do.
            Dictionary<string, JsonNode> byId = ById();
            var offenders = new List<string>();

            foreach (JsonNode agent in StaffedAgents())
            {
                foreach (string target in Strings(agent["dependsOn"]).Concat(Strings(agent["challenges"])))
                {
                    if (Status(byId[target]) == "retired")
                    {
                        offenders.Add($"{Id(agent)} -> {target}");
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "These agents point at a retired agent, whose output is never coming:\n"
                + string.Join("\n", offenders)
                + "\nRetire the dependants too, or repoint them at whatever replaced it.");
        }

        [Test]
        public void ARetiredAgentIsNeverStillNamedAsADivisionBoss()
        {
            // Retirement is a status change rather than a deletion, which means a retired
            // boss stays readable in the registry and org.json can keep pointing at it
            // without anything else objecting. Everyone under it would then report to a
            // role that no longer works.
            Dictionary<string, JsonNode> byId = ById();

            foreach (JsonNode division in Org["divisions"].AsArray())
            {
                string bossId = division["boss"].GetValue<string>();
                if (!byId.ContainsKey(bossId))
                {
                    continue; // EveryDivisionBossIsTheOneNamedInTheOrgChart reports this.
                }

                Assert.That(Status(byId[bossId]), Is.Not.EqualTo("retired"),
                    $"Division {division["id"]} is still run by {bossId}, who is retired.");
            }
        }

        [Test]
        public void ARetiredAgentsReplacementExistsAndIsNotItselfRetired()
        {
            var byId = ById();

            foreach (JsonNode agent in AllAgents().Where(a => a["replacedBy"] != null))
            {
                string replacement = agent["replacedBy"].GetValue<string>();

                Assert.That(byId.ContainsKey(replacement), Is.True,
                    $"{Id(agent)} names {replacement} as its replacement, who is not in the roster.");
                Assert.That(replacement, Is.Not.EqualTo(Id(agent)),
                    $"{Id(agent)} is its own replacement.");
                Assert.That(Status(byId[replacement]), Is.Not.EqualTo("retired"),
                    $"{Id(agent)} was replaced by {replacement}, who is also retired. The capability has no owner.");
            }
        }

        [Test]
        public void EveryActiveAgentSitsUnderAnActiveChainOfCommand()
        {
            Dictionary<string, JsonNode> byId = ById();

            foreach (JsonNode agent in AllAgents().Where(a => a["status"].GetValue<string>() == "active"))
            {
                // The chain ends at the founder, who has no agent record and is never dormant.
                foreach (string superior in ChainToTop(byId, Id(agent)).Where(s => s != "human"))
                {
                    Assert.That(byId[superior]["status"].GetValue<string>(), Is.EqualTo("active"),
                        $"{Id(agent)} is active but reports through {superior}, who is dormant. Its work would have nobody to report to.");
                }
            }
        }

        // ---------- permissions and gates survive the rewrite ----------

        [Test]
        public void EveryDivisionHasExactlyOneGrant()
        {
            List<string> granted = Permissions["grants"].AsArray()
                .Select(g => g["division"].GetValue<string>()).ToList();
            List<string> divisions = Org["divisions"].AsArray()
                .Select(d => d["id"].GetValue<string>()).ToList();

            Assert.That(granted, Is.Unique);
            Assert.That(granted, Is.EquivalentTo(divisions));
        }

        [Test]
        public void EveryAgentWriteScopeStaysInsideItsDivisionGrant()
        {
            foreach (JsonNode agent in AllAgents())
            {
                if (agent["writeScope"] == null)
                {
                    continue;
                }

                JsonNode grant = Permissions["grants"].AsArray()
                    .First(g => g["division"].GetValue<string>() == agent["division"].GetValue<string>());

                var allowed = new HashSet<string>(Strings(grant["write"]).Concat(Strings(grant["append"])));

                foreach (string path in Strings(agent["writeScope"]))
                {
                    Assert.That(allowed, Contains.Item(path),
                        $"{Id(agent)} narrows to '{path}', which its division was never granted.");
                }
            }
        }

        [Test]
        public void TheGateMachineryIsHumanExclusive()
        {
            IEnumerable<string> exclusive = Strings(Permissions["humanExclusivePaths"]);

            Assert.That(exclusive, Contains.Item(".github/workflows/**"));
            Assert.That(exclusive, Contains.Item("Studio/build/**"),
                "The workflow calls gate-g2.sh, so write access to that script is write access to the gate itself.");
        }

        [Test]
        public void NoGrantOverlapsAHumanExclusivePath()
        {
            List<string> exclusive = Strings(Permissions["humanExclusivePaths"]).ToList();

            foreach (JsonNode grant in Permissions["grants"].AsArray())
            {
                string division = grant["division"].GetValue<string>();

                foreach (string writable in Strings(grant["write"]).Concat(Strings(grant["append"])))
                {
                    foreach (string reserved in exclusive)
                    {
                        Assert.That(Overlaps(writable, reserved), Is.False,
                            $"Division '{division}' may write '{writable}', which reaches into the human-exclusive path '{reserved}'.");
                    }
                }
            }
        }

        [Test]
        public void OverlapDetectionItselfWorks()
        {
            Assert.That(Overlaps("Studio/constitution/**", "Studio/constitution/**"), Is.True);
            Assert.That(Overlaps("Studio/**", "Studio/constitution/**"), Is.True);
            Assert.That(Overlaps("Studio/state/**", "Studio/constitution/**"), Is.False);
            Assert.That(Overlaps("Studio/stateless/**", "Studio/state/**"), Is.False);
        }

        [Test]
        public void EveryGateReferencedByAnAgentExists()
        {
            var known = new HashSet<string>(Gates["gates"].AsArray().Select(g => g["id"].GetValue<string>()));

            foreach (JsonNode agent in AllAgents())
            {
                foreach (string gate in Strings(agent["verifiedByGates"]))
                {
                    Assert.That(known, Contains.Item(gate),
                        $"{Id(agent)} is verified by unknown gate '{gate}'.");
                }
            }
        }

        [Test]
        public void TheCodeGateCanBeOverriddenByNobody()
        {
            JsonNode g2 = Gates["gates"].AsArray().First(g => g["id"].GetValue<string>() == "G2");

            Assert.That(Strings(g2["overridableBy"]), Is.Empty);
            Assert.That(g2["blocking"].GetValue<bool>(), Is.True);
        }

        [Test]
        public void NobodyMayDeclareTheirOwnWorkDone()
        {
            foreach (JsonNode agent in AllAgents())
            {
                Assert.That(agent["mayDeclareOwnWorkDone"].GetValue<bool>(), Is.False,
                    $"{Id(agent)} may mark its own work complete, which makes every gate below it decorative.");
            }
        }

        [Test]
        public void OnlySpecialistsTouchProductionCode()
        {
            foreach (JsonNode agent in AllAgents().Where(a => a["tier"].GetValue<string>() != "specialist"))
            {
                Assert.That(agent["mayEditProductionCode"].GetValue<bool>(), Is.False,
                    $"{Id(agent)} runs a division and also writes code, so it would be reviewing itself.");
            }
        }

        [Test]
        public void NoQualityAgentCanFixWhatItJudges()
        {
            foreach (JsonNode agent in AllAgents().Where(a => a["division"].GetValue<string>() == "quality"))
            {
                Assert.That(agent["mayEditProductionCode"].GetValue<bool>(), Is.False,
                    $"{Id(agent)} both judges and repairs production code. Its green verdict would mean nothing.");
            }
        }

        [Test]
        public void EveryMemoryWriterIsAKnownActor()
        {
            var known = new HashSet<string>(AllAgents().Select(Id)) { "human", "ci" };

            foreach (JsonNode layer in Memory["layers"].AsArray())
            {
                foreach (string writer in Strings(layer["writers"]))
                {
                    Assert.That(known, Contains.Item(writer),
                        $"Memory layer {layer["layer"]} names unknown writer '{writer}'.");
                }

                Assert.That(known, Contains.Item(layer["compactionOwner"].GetValue<string>()));
            }
        }

        // ---------- helpers ----------

        private static string Normalise(string text) =>
            string.Join(" ", Words(text));

        private static readonly HashSet<string> Stopwords = new HashSet<string>
        {
            "a", "an", "the", "of", "for", "and", "or", "to", "in", "on", "with", "per",
            "that", "which", "its", "it", "each", "every", "by", "at", "from", "as", "is", "are"
        };

        private static HashSet<string> Words(string text) =>
            new HashSet<string>(
                text.ToLowerInvariant()
                    .Split(new[] { ' ', ',', '.', ':', ';', '-', '(', ')', '\'', '"', '/', '\n', '\t' },
                        StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => !Stopwords.Contains(w)));

        private static double Jaccard(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 || b.Count == 0)
            {
                return 0;
            }

            double intersection = a.Intersect(b).Count();
            double union = a.Union(b).Count();
            return intersection / union;
        }

        private static bool Overlaps(string a, string b)
        {
            string na = Normalise2(a);
            string nb = Normalise2(b);
            return IsPrefix(na, nb) || IsPrefix(nb, na);
        }

        private static string Normalise2(string glob) => glob.TrimEnd('*').TrimEnd('/');

        private static bool IsPrefix(string prefix, string candidate) =>
            candidate.Equals(prefix, StringComparison.Ordinal) ||
            (candidate.StartsWith(prefix, StringComparison.Ordinal) &&
             candidate.Length > prefix.Length &&
             candidate[prefix.Length] == '/');
    }
}
