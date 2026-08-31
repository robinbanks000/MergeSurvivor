using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace MergeSurvivor.Kernel.Tests
{
    /// <summary>
    /// The checks that make a variable-size workforce safe, and the checks that keep the
    /// studio layer and its products apart.
    ///
    /// ADR-0005 removed the fixed roster size. What stops that becoming sprawl is not a
    /// number but a sequence: a capability gap is filed with evidence, the existing
    /// roster is examined and found unable to absorb the work, and only then may a
    /// specialist be proposed. These tests hold that sequence to its own claims.
    ///
    /// The most load-bearing one is DuplicateOutputsAreCaughtWhileStillAProposal.
    /// OrgCrossCheckTests catches a duplicate measurable output after the agent exists,
    /// by which point a definition has been written, ratified in a constitution edit and
    /// emitted as a prompt. Catching it while it is still a field inside a gap record
    /// costs nothing to fix.
    /// </summary>
    [TestFixture]
    public class WorkforceCrossCheckTests
    {
        private static JsonNode Org => Kernel.ReadRepoJson("Studio/constitution/org.json");
        private static JsonNode Projects => Kernel.ReadRepoJson("Studio/constitution/projects.json");
        private static JsonNode Budgets => Kernel.ReadRepoJson("Studio/constitution/budgets.json");

        private static string GapDir => Path.Combine(Kernel.RepoRoot, "Studio", "state", "gaps");

        private static IEnumerable<string> Strings(JsonNode node) =>
            node == null ? Enumerable.Empty<string>() : node.AsArray().Select(n => n.GetValue<string>());

        private static List<JsonNode> Gaps()
        {
            if (!Directory.Exists(GapDir))
            {
                return new List<JsonNode>();
            }

            return Directory.GetFiles(GapDir, "GAP-*.json")
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(Kernel.ReadJson)
                .ToList();
        }

        private static List<JsonNode> AllAgents()
        {
            var agents = new List<JsonNode>();
            string dir = Path.Combine(Kernel.RepoRoot, "Studio", "constitution", "agents");

            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                agents.AddRange(Kernel.ReadJson(file)["agents"].AsArray());
            }

            return agents;
        }

        private static Dictionary<string, JsonNode> AgentsById() =>
            AllAgents().ToDictionary(a => a["id"].GetValue<string>(), a => a);

        private static string Normalise(string value) =>
            new string(value.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray())
                .Trim();

        // ---------- a capability gap is evidence, not enthusiasm ----------

        [Test]
        public void EveryGapConsidersAgentsThatActuallyExist()
        {
            var known = AgentsById();

            foreach (JsonNode gap in Gaps())
            {
                string id = gap["id"].GetValue<string>();

                foreach (JsonNode considered in gap["consideredExisting"].AsArray())
                {
                    string agent = considered["agent"].GetValue<string>();
                    Assert.That(known.ContainsKey(agent), Is.True,
                        $"{id} rules out '{agent}' as an owner, but no such agent is in the roster. "
                        + "A gap justified against imaginary alternatives justifies nothing.");
                }
            }
        }

        [Test]
        public void DuplicateOutputsAreCaughtWhileStillAProposal()
        {
            // The whole point of filing a gap before hiring. A proposed measurable output
            // that already belongs to a staffed agent means the work has an owner and the
            // gap is a routing problem, not a staffing one.
            var owned = new Dictionary<string, string>();

            foreach (JsonNode agent in AllAgents().Where(a => a["status"].GetValue<string>() != "retired"))
            {
                foreach (string output in Strings(agent["measurableOutput"]))
                {
                    owned[Normalise(output)] = agent["id"].GetValue<string>();
                }
            }

            foreach (JsonNode gap in Gaps())
            {
                JsonNode proposed = gap["proposedSpecialist"];
                if (proposed == null)
                {
                    continue;
                }

                foreach (string output in Strings(proposed["measurableOutput"]))
                {
                    Assert.That(owned.ContainsKey(Normalise(output)), Is.False,
                        $"{gap["id"]} proposes {proposed["id"]} to produce \"{output}\", which "
                        + $"{(owned.TryGetValue(Normalise(output), out string other) ? other : "?")} already produces. "
                        + "Two agents producing the same artifact are one agent.");
                }
            }
        }

        [Test]
        public void AProposedSpecialistDoesNotAlreadyExist()
        {
            var known = AgentsById();

            foreach (JsonNode gap in Gaps())
            {
                JsonNode proposed = gap["proposedSpecialist"];
                if (proposed == null)
                {
                    continue;
                }

                string id = proposed["id"].GetValue<string>();
                Assert.That(known.ContainsKey(id), Is.False,
                    $"{gap["id"]} proposes to create {id}, who is already in the roster. "
                    + "If the intent is to activate or rescope them, that is not a new hire.");
            }
        }

        [Test]
        public void AProposedSpecialistFitsTheOrgChartItWouldJoin()
        {
            var known = AgentsById();
            var divisions = new HashSet<string>(
                Org["divisions"].AsArray().Select(d => d["id"].GetValue<string>()));
            var budgets = new HashSet<string>(
                Budgets["budgets"].AsArray().Select(b => b["id"].GetValue<string>()));

            foreach (JsonNode gap in Gaps())
            {
                JsonNode proposed = gap["proposedSpecialist"];
                if (proposed == null)
                {
                    continue;
                }

                string id = proposed["id"].GetValue<string>();
                string division = proposed["division"].GetValue<string>();
                string boss = proposed["reportsTo"].GetValue<string>();

                Assert.That(divisions, Contains.Item(division),
                    $"{gap["id"]} would file {id} under division '{division}', which the org chart does not define.");
                Assert.That(known.ContainsKey(boss), Is.True,
                    $"{gap["id"]} would have {id} report to '{boss}', who does not exist.");
                Assert.That(known[boss]["division"].GetValue<string>(), Is.EqualTo(division),
                    $"{gap["id"]} would have {id} report outside its own division, so its boss would be "
                    + "accountable for work they do not own.");
                Assert.That(known[boss]["status"].GetValue<string>(), Is.Not.EqualTo("retired"),
                    $"{gap["id"]} would have {id} report to {boss}, who is retired.");

                if (proposed["budgetId"] != null)
                {
                    Assert.That(budgets, Contains.Item(proposed["budgetId"].GetValue<string>()),
                        $"{gap["id"]} names a budget for {id} that does not exist, so its spend would be untracked.");
                }
            }
        }

        [Test]
        public void AFilledGapNamesAnAgentThatExistsAndIsNotRetired()
        {
            var known = AgentsById();

            foreach (JsonNode gap in Gaps().Where(g => g["status"].GetValue<string>() == "filled"))
            {
                string owner = gap["resolvedBy"].GetValue<string>();

                Assert.That(known.ContainsKey(owner), Is.True,
                    $"{gap["id"]} is filled by '{owner}', who is not in the roster.");
                Assert.That(known[owner]["status"].GetValue<string>(), Is.Not.EqualTo("retired"),
                    $"{gap["id"]} is filled by {owner}, who is retired. The gap is open again.");
            }
        }

        [Test]
        public void EveryGapPointsAtProjectsThatExist()
        {
            var known = new HashSet<string>(
                Projects["projects"].AsArray().Select(p => p["id"].GetValue<string>()));

            foreach (JsonNode gap in Gaps())
            {
                foreach (string project in Strings(gap["affectedProjects"]))
                {
                    Assert.That(known, Contains.Item(project),
                        $"{gap["id"]} names project '{project}', which is not in the project registry.");
                }
            }
        }

        // ---------- the studio layer and its products stay apart ----------

        [Test]
        public void NoTwoProjectsClaimTheSamePath()
        {
            var claimed = new Dictionary<string, string>();

            foreach (JsonNode project in Projects["projects"].AsArray())
            {
                string id = project["id"].GetValue<string>();

                foreach (string path in Strings(project["owns"]))
                {
                    Assert.That(claimed.ContainsKey(path), Is.False,
                        $"{id} and {(claimed.TryGetValue(path, out string other) ? other : "?")} both claim '{path}'. "
                        + "Two projects owning one path means neither owns it.");

                    claimed[path] = id;
                }
            }
        }

        [Test]
        public void NoProjectClaimsAPathInsideTheStudioLayer()
        {
            // The founder's rule, made checkable in one direction: a product may not own
            // any part of the machinery that governs it. The other direction is below.
            foreach (string studioPath in Strings(Projects["studioPaths"]))
            {
                string studioPrefix = Prefix(studioPath);

                foreach (JsonNode project in Projects["projects"].AsArray())
                {
                    foreach (string owned in Strings(project["owns"]))
                    {
                        Assert.That(Prefix(owned).StartsWith(studioPrefix, StringComparison.Ordinal), Is.False,
                            $"Project {project["id"]} claims '{owned}', which is inside the studio layer's '{studioPath}'.");
                    }
                }
            }
        }

        [Test]
        public void NoProjectFileReachesIntoTheStudioLayer()
        {
            // The other direction, and the one that matters most in practice: studio
            // tooling must never ship inside a product. JARVIS is a page generated from
            // the studio's own records and has no business in a game build; a game that
            // imports it would carry the studio's branding, its navigation and its state
            // into a shipped artifact.
            //
            // Textual on purpose. A C# using-directive, a Unity asset reference and an
            // asmdef entry look nothing alike, and the thing they would have in common is
            // the path. Tracked files only, so a local scratch file cannot fail the build.
            var studioPrefixes = Strings(Projects["studioPaths"]).Select(Prefix).ToList();
            var offenders = new List<string>();

            foreach (JsonNode project in Projects["projects"].AsArray())
            {
                foreach (string ownedGlob in Strings(project["owns"]))
                {
                    string root = Prefix(ownedGlob).TrimEnd('/');
                    if (root.Length == 0)
                    {
                        continue;
                    }

                    foreach (string file in Kernel.TrackedFiles(root))
                    {
                        string absolute = Path.Combine(Kernel.RepoRoot, file.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(absolute) || IsBinary(absolute))
                        {
                            continue;
                        }

                        string text = File.ReadAllText(absolute);

                        foreach (string studioPrefix in studioPrefixes)
                        {
                            if (text.Contains(studioPrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                offenders.Add($"{file} references '{studioPrefix}'");
                            }
                        }
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "Project files referencing the studio layer:\n" + string.Join("\n", offenders)
                + "\nThe studio operates the products; it does not ship inside them.");
        }

        [Test]
        public void EveryProjectIsVerifiedByGatesThatExist()
        {
            var known = new HashSet<string>(
                Kernel.ReadRepoJson("Studio/constitution/gates.json")["gates"].AsArray()
                    .Select(g => g["id"].GetValue<string>()));

            foreach (JsonNode project in Projects["projects"].AsArray())
            {
                foreach (string gate in Strings(project["verifiedByGates"]))
                {
                    Assert.That(known, Contains.Item(gate),
                        $"Project {project["id"]} claims gate '{gate}', which the constitution does not define.");
                }
            }
        }

        [Test]
        public void EveryProjectDivisionExistsInTheOrgChart()
        {
            var known = new HashSet<string>(
                Org["divisions"].AsArray().Select(d => d["id"].GetValue<string>()));

            foreach (JsonNode project in Projects["projects"].AsArray())
            {
                foreach (string division in Strings(project["divisions"]))
                {
                    Assert.That(known, Contains.Item(division),
                        $"Project {project["id"]} names division '{division}', which the org chart does not define.");
                }
            }
        }

        /// <summary>
        /// The fixed leading part of a glob: everything before the first wildcard. Two
        /// globs overlap when one's prefix contains the other's, which is all the
        /// comparison these checks need and avoids importing a glob matcher to answer it.
        /// </summary>
        private static string Prefix(string glob)
        {
            int star = glob.IndexOf('*');
            return star < 0 ? glob : glob.Substring(0, star);
        }

        private static bool IsBinary(string path)
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[512];
            int read = stream.Read(head);

            return head.Slice(0, read).IndexOf((byte)0) >= 0;
        }
    }
}
