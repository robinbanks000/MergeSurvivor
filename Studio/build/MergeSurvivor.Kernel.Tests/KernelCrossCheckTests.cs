using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace MergeSurvivor.Kernel.Tests
{
    /// <summary>
    /// The invariants JSON Schema cannot express, because they span two documents or
    /// compare two sibling fields. These are the rules that keep the registries from
    /// drifting apart while each stays individually valid.
    /// </summary>
    [TestFixture]
    public class KernelCrossCheckTests
    {
        private static JsonNode Agents => Kernel.ReadRepoJson("Studio/constitution/agents.json");
        private static JsonNode Gates => Kernel.ReadRepoJson("Studio/constitution/gates.json");
        private static JsonNode Permissions => Kernel.ReadRepoJson("Studio/constitution/permissions.json");
        private static JsonNode Budgets => Kernel.ReadRepoJson("Studio/constitution/budgets.json");
        private static JsonNode Memory => Kernel.ReadRepoJson("Studio/constitution/memory.json");
        private static JsonNode ProjectState => Kernel.ReadRepoJson("Studio/state/project-state.json");

        private static List<string> AgentIds() =>
            Agents["agents"].AsArray().Select(a => a["id"].GetValue<string>()).ToList();

        private static IEnumerable<string> Strings(JsonNode node) =>
            node == null ? Enumerable.Empty<string>() : node.AsArray().Select(n => n.GetValue<string>());

        // ---- schema hygiene ----

        [Test]
        public void EverySchemaIdMatchesItsFileName()
        {
            // A mismatched $id breaks every $ref pointing at it, and the failure mode is
            // a schema that quietly validates nothing rather than an error.
            foreach (string file in Directory.GetFiles(Kernel.SchemaDir, "*.schema.json"))
            {
                JsonNode doc = Kernel.ReadJson(file);
                string id = doc["$id"].GetValue<string>();
                string expected = "https://mergesurvivor.studio/kernel/" + Path.GetFileName(file);

                Assert.That(id, Is.EqualTo(expected), $"{Path.GetFileName(file)} has the wrong $id.");
            }
        }

        // ---- agents <-> permissions ----

        [Test]
        public void EveryAgentHasExactlyOneGrant()
        {
            List<string> granted = Permissions["grants"].AsArray()
                .Select(g => g["agent"].GetValue<string>()).ToList();

            Assert.That(granted, Is.Unique);
            Assert.That(granted, Is.EquivalentTo(AgentIds()),
                "agents.json and permissions.json disagree about who exists.");
        }

        [Test]
        public void EveryAgentWriteScopeIsCoveredByItsGrant()
        {
            foreach (JsonNode agent in Agents["agents"].AsArray())
            {
                string id = agent["id"].GetValue<string>();
                JsonNode grant = Permissions["grants"].AsArray()
                    .First(g => g["agent"].GetValue<string>() == id);

                var allowed = new HashSet<string>(
                    Strings(grant["write"]).Concat(Strings(grant["append"])));

                foreach (string path in Strings(agent["writeScope"]))
                {
                    Assert.That(allowed, Contains.Item(path),
                        $"Agent '{id}' declares write scope '{path}' that its permission grant does not allow.");
                }
            }
        }

        [Test]
        public void CiWorkflowsAreHumanExclusive()
        {
            // The most dangerous permission in the system: an agent that can edit CI can
            // switch off the gates that judge it.
            Assert.That(Strings(Permissions["humanExclusivePaths"]),
                Contains.Item(".github/workflows/**"));
        }

        [Test]
        public void NoGrantOverlapsAHumanExclusivePath()
        {
            List<string> exclusive = Strings(Permissions["humanExclusivePaths"]).ToList();

            foreach (JsonNode grant in Permissions["grants"].AsArray())
            {
                string agent = grant["agent"].GetValue<string>();

                foreach (string writable in Strings(grant["write"]).Concat(Strings(grant["append"])))
                {
                    foreach (string reserved in exclusive)
                    {
                        Assert.That(Overlaps(writable, reserved), Is.False,
                            $"Agent '{agent}' may write '{writable}', which reaches into the human-exclusive path '{reserved}'.");
                    }
                }
            }
        }

        /// <summary>
        /// Two globs overlap when one's directory prefix contains the other's. Compared on
        /// a path-segment boundary so "Studio/state" does not look like a prefix of
        /// "Studio/stateless".
        /// </summary>
        private static bool Overlaps(string a, string b)
        {
            string na = Normalise(a);
            string nb = Normalise(b);

            return IsPrefix(na, nb) || IsPrefix(nb, na);
        }

        private static string Normalise(string glob) => glob.TrimEnd('*').TrimEnd('/');

        private static bool IsPrefix(string prefix, string candidate) =>
            candidate.Equals(prefix, StringComparison.Ordinal) ||
            (candidate.StartsWith(prefix, StringComparison.Ordinal) &&
             candidate.Length > prefix.Length &&
             candidate[prefix.Length] == '/');

        [Test]
        public void OverlapDetectionItselfWorks()
        {
            // A vacuous overlap check would make the permission test above pass forever.
            Assert.That(Overlaps("Studio/constitution/**", "Studio/constitution/**"), Is.True);
            Assert.That(Overlaps("Studio/**", "Studio/constitution/**"), Is.True);
            Assert.That(Overlaps("Studio/state/**", "Studio/constitution/**"), Is.False);
            Assert.That(Overlaps("Studio/stateless/**", "Studio/state/**"), Is.False);
        }

        // ---- budgets ----

        [Test]
        public void EveryAgentHasABudget()
        {
            List<string> budgeted = Budgets["budgets"].AsArray()
                .Where(b => b["scope"].GetValue<string>() == "agent")
                .Select(b => b["agent"].GetValue<string>())
                .ToList();

            Assert.That(budgeted, Is.EquivalentTo(AgentIds()),
                "An agent with no budget can spend without limit overnight.");
        }

        [Test]
        public void EveryBudgetHardStopIsAtLeastItsSoftWarning()
        {
            // JSON Schema cannot compare two sibling numbers, so this lives here.
            foreach (JsonNode budget in Budgets["budgets"].AsArray())
            {
                double soft = budget["softWarnUsd"].GetValue<double>();
                double hard = budget["hardStopUsd"].GetValue<double>();

                Assert.That(hard, Is.GreaterThanOrEqualTo(soft),
                    $"Budget {budget["id"].GetValue<string>()} would hard-stop before it warns.");
            }
        }

        [Test]
        public void ExactlyOneStudioWideBudgetExists()
        {
            int studioBudgets = Budgets["budgets"].AsArray()
                .Count(b => b["scope"].GetValue<string>() == "studio");

            Assert.That(studioBudgets, Is.EqualTo(1));
        }

        // ---- gates ----

        [Test]
        public void AllSixGatesAreRegistered()
        {
            List<string> ids = Gates["gates"].AsArray().Select(g => g["id"].GetValue<string>()).ToList();

            Assert.That(ids, Is.EquivalentTo(new[] { "G0", "G1", "G2", "G3", "G4", "G5" }));
        }

        [Test]
        public void TheCodeGateCanBeOverriddenByNobody()
        {
            JsonNode g2 = Gates["gates"].AsArray().First(g => g["id"].GetValue<string>() == "G2");

            Assert.That(Strings(g2["overridableBy"]), Is.Empty,
                "G2 must have no override. The moment a way around 'it compiles and the tests pass' exists, an autonomous system will find it.");
            Assert.That(g2["blocking"].GetValue<bool>(), Is.True);
        }

        [Test]
        public void EveryGateReferencedByAnAgentExists()
        {
            var known = new HashSet<string>(Gates["gates"].AsArray().Select(g => g["id"].GetValue<string>()));

            foreach (JsonNode agent in Agents["agents"].AsArray())
            {
                foreach (string gate in Strings(agent["verifiedByGates"]))
                {
                    Assert.That(known, Contains.Item(gate),
                        $"Agent '{agent["id"].GetValue<string>()}' is verified by unknown gate '{gate}'.");
                }
            }
        }

        // ---- memory ----

        [Test]
        public void EveryMemoryWriterIsAKnownActor()
        {
            var known = new HashSet<string>(AgentIds()) { "human", "ci" };

            foreach (JsonNode layer in Memory["layers"].AsArray())
            {
                foreach (string writer in Strings(layer["writers"]))
                {
                    Assert.That(known, Contains.Item(writer),
                        $"Memory layer {layer["layer"].GetValue<string>()} names unknown writer '{writer}'.");
                }

                Assert.That(known, Contains.Item(layer["compactionOwner"].GetValue<string>()));
            }
        }

        [Test]
        public void EveryMemoryLayerIsDeclaredExactlyOnce()
        {
            List<string> layers = Memory["layers"].AsArray()
                .Select(l => l["layer"].GetValue<string>()).ToList();

            Assert.That(layers, Is.EquivalentTo(new[] { "L0", "L1", "L2", "L3", "L4" }));
        }

        // ---- project state ----

        [Test]
        public void ProjectStateTracksExactlyTheRegisteredAgents()
        {
            List<string> tracked = ProjectState["agentStatus"].AsArray()
                .Select(a => a["agent"].GetValue<string>()).ToList();

            Assert.That(tracked, Is.EquivalentTo(AgentIds()));
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

        // ---- referential integrity across records ----

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

        // ---- acceptance criteria authorship ----

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
