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
