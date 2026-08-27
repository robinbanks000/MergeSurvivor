using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Json.Schema;
using NUnit.Framework;

namespace MergeSurvivor.Kernel.Tests
{
    /// <summary>
    /// Validates the kernel contracts. The invalid-fixture tests matter most: a schema
    /// that accepts everything passes every positive test while enforcing nothing, so
    /// each rejection case is written to trip exactly one rule.
    /// </summary>
    [TestFixture]
    public class KernelContractTests
    {
        private static IEnumerable<string> ValidFixtures() => Kernel.FixtureFiles("valid");

        private static IEnumerable<string> InvalidFixtures() => Kernel.FixtureFiles("invalid");

        private static IEnumerable<TestCaseData> ManifestDocuments()
        {
            JsonNode manifest = Kernel.ReadRepoJson("Studio/kernel/kernel-manifest.json");

            foreach (JsonNode entry in manifest["documents"].AsArray())
            {
                yield return new TestCaseData(
                    entry["path"].GetValue<string>(),
                    entry["schema"].GetValue<string>());
            }

            foreach (JsonNode entry in manifest["globs"].AsArray())
            {
                string schema = entry["schema"].GetValue<string>();
                foreach (string path in Kernel.ExpandGlob(entry["glob"].GetValue<string>()))
                {
                    yield return new TestCaseData(path, schema);
                }
            }
        }

        [Test]
        public void EverySchemaFileLoads()
        {
            // Catches a malformed schema before it silently starts accepting everything.
            Assert.That(Kernel.Schemas, Is.Not.Empty);
            Assert.That(Kernel.Schemas.Keys, Contains.Item("common.schema.json"));
        }

        [Test]
        public void EveryContractSchemaExists()
        {
            string[] expected =
            {
                "common.schema.json",
                "task.schema.json",
                "agent.schema.json",
                "org.schema.json",
                "event.schema.json",
                "message.schema.json",
                "memory.schema.json",
                "project-state.schema.json",
                "decision.schema.json",
                "evidence.schema.json",
                "gate.schema.json",
                "permission.schema.json",
                "escalation.schema.json",
                "failure.schema.json",
                "cost.schema.json",
                "proposal.schema.json",
                "challenge.schema.json",
                "division-report.schema.json"
            };

            Assert.That(Kernel.Schemas.Keys, Is.EquivalentTo(expected));
        }

        [TestCaseSource(nameof(ValidFixtures))]
        public void ValidFixtureIsAccepted(string fixturePath)
        {
            JsonSchema schema = Kernel.SchemaFor(Kernel.SchemaNameFromFixture(fixturePath));
            EvaluationResults results = schema.Evaluate(Kernel.ReadJson(fixturePath), Kernel.Options);

            Assert.That(
                results.IsValid,
                Is.True,
                $"{Path.GetFileName(fixturePath)} should be valid but was rejected:\n{Kernel.Describe(results)}");
        }

        [TestCaseSource(nameof(InvalidFixtures))]
        public void InvalidFixtureIsRejected(string fixturePath)
        {
            JsonSchema schema = Kernel.SchemaFor(Kernel.SchemaNameFromFixture(fixturePath));
            EvaluationResults results = schema.Evaluate(Kernel.ReadJson(fixturePath), Kernel.Options);

            Assert.That(
                results.IsValid,
                Is.False,
                $"{Path.GetFileName(fixturePath)} violates a kernel rule but the schema accepted it. " +
                "The contract is not enforcing what its name claims.");
        }

        [TestCaseSource(nameof(ManifestDocuments))]
        public void LiveKernelDocumentIsValid(string relativePath, string schemaName)
        {
            string absolute = Path.Combine(
                Kernel.RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

            Assert.That(File.Exists(absolute), Is.True, $"{relativePath} is in the manifest but missing on disk.");

            JsonSchema schema = Kernel.SchemaFor(schemaName);
            EvaluationResults results = schema.Evaluate(Kernel.ReadJson(absolute), Kernel.Options);

            Assert.That(
                results.IsValid,
                Is.True,
                $"{relativePath} does not satisfy {schemaName}:\n{Kernel.Describe(results)}");
        }

        [Test]
        public void EveryKernelDocumentIsCoveredByTheManifest()
        {
            // Stops an agent from adding an unvalidated state file and having it drift.
            JsonNode manifest = Kernel.ReadRepoJson("Studio/kernel/kernel-manifest.json");

            var covered = new HashSet<string>(
                manifest["documents"].AsArray().Select(e => e["path"].GetValue<string>()));

            foreach (JsonNode entry in manifest["globs"].AsArray())
            {
                foreach (string path in Kernel.ExpandGlob(entry["glob"].GetValue<string>()))
                {
                    covered.Add(path);
                }
            }

            // Only tracked files count. Scanning the filesystem would also pick up
            // gitignored scratch output — the simulation's metrics.json, for one —
            // and demand a manifest entry for something that is not kernel state at
            // all. If git does not track it, it is not part of the studio's memory.
            IEnumerable<string> found = Kernel.TrackedFiles(
                "Studio/constitution", "Studio/state", "Studio/decisions",
                "Studio/evidence", "Studio/orders")
                .Where(f => f.EndsWith(".json", System.StringComparison.Ordinal));

            IEnumerable<string> uncovered = found.Where(f => !covered.Contains(f)).OrderBy(f => f);

            Assert.That(
                uncovered,
                Is.Empty,
                "These kernel documents are validated by nothing. Add them to Studio/kernel/kernel-manifest.json.");
        }
    }
}
