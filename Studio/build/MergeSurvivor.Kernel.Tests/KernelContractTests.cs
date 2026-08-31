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
                "division-report.schema.json",
                "ruling.schema.json"
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

        /// <summary>
        /// True when a file name claims a kernel record identifier: a known record prefix
        /// followed immediately by digits. Case-insensitive, because the first version of
        /// this rule matched Ordinal and so admitted `evd-0006-notes.md` -- and, as
        /// RUL-0004 pointed out, my own rename of the offending file passed it only by
        /// being lowercase, which meant the check was not drawing the boundary it meant
        /// to draw. Extracted from the test so it can be sprung against synthetic names
        /// rather than resting on a live impostor that no longer exists.
        /// </summary>
        internal static bool ClaimsARecordIdentifier(string fileName)
        {
            string[] prefixes = { "EVD", "PRO", "CHA", "RUL", "RPT", "WO", "ADR", "ESC", "FAIL", "EVT", "MSG", "GAP" };

            foreach (string prefix in prefixes)
            {
                if (fileName.Length > prefix.Length + 1
                    && fileName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)
                    && fileName[prefix.Length] == '-'
                    && char.IsDigit(fileName[prefix.Length + 1]))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<TestCaseData> RecordNameCases()
        {
            yield return new TestCaseData("EVD-0005-WO0009-Criterion11.md", true).SetName("the live impostor RUL-0004 was filed over");
            yield return new TestCaseData("evd-0006-notes.md", true).SetName("lowercase, which the Ordinal version admitted");
            yield return new TestCaseData("wo-0009-criterion-11-regression.md", true).SetName("my own rename, which passed only by case");
            yield return new TestCaseData("RUL-0004.json", true).SetName("a real record still claims an identifier");
            yield return new TestCaseData("Fail-0001-postmortem.md", true).SetName("mixed case");
            yield return new TestCaseData("LESSONS.md", false).SetName("prose with no identifier");
            yield return new TestCaseData("README.md", false).SetName("prose with no identifier");
            yield return new TestCaseData("workflow-notes.md", false).SetName("starts with WO but no digit follows the dash");
            yield return new TestCaseData("evidence-summary.md", false).SetName("starts with EV but is not a prefix");
            yield return new TestCaseData("GAP-0001.json", true).SetName("a capability gap claims an identifier too");
            yield return new TestCaseData("gap-analysis.md", false).SetName("starts with GAP but no digit follows the dash");
            yield return new TestCaseData("metrics_pre.json", false).SetName("scratch output");
        }

        [TestCaseSource(nameof(RecordNameCases))]
        public void ClaimsARecordIdentifierIsDecidedByPrefixAndDigit(string fileName, bool expected)
        {
            // The spring. Without it the rule below passes vacuously the moment the tree
            // is clean, and would keep passing if someone trimmed the prefix list --
            // RUL-0004's third finding against my own fix.
            Assert.That(ClaimsARecordIdentifier(fileName), Is.EqualTo(expected), fileName);
        }

        [Test]
        public void AFileNamedLikeAKernelRecordIsAKernelRecord()
        {
            // EveryKernelDocumentIsCoveredByTheManifest filters to *.json so a stray README
            // does not demand a manifest entry. That filter was a hole rather than a
            // convenience: a verifier filed Studio/evidence/EVD-0005-WO0009-Criterion11.md,
            // claiming an id already held by Studio/evidence/tests/EVD-0005.json, and being
            // markdown it was invisible to the manifest check, the schema validator and the
            // id-uniqueness cross-check alike. A document wearing a record's name while
            // escaping every check on records is worse than an unvalidated file: it reads
            // as authoritative.
            //
            // Narrow on purpose. Prose in the state tree is fine; prose wearing a record
            // identifier is not.
            IEnumerable<string> impostors = Kernel.TrackedFiles(
                "Studio/constitution", "Studio/state", "Studio/decisions",
                "Studio/evidence", "Studio/orders")
                .Where(f => !f.EndsWith(".json", System.StringComparison.Ordinal))
                .Where(f => ClaimsARecordIdentifier(Path.GetFileName(f)))
                .OrderBy(f => f);

            Assert.That(
                impostors,
                Is.Empty,
                "These files are named like kernel records but are not JSON, so no schema governs them "
                + "and the id-uniqueness check cannot see them. Either file the content as a real record "
                + "under its contract, or rename it so it does not claim a record identifier.");
        }
    }
}
