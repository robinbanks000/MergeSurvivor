using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Json.Schema;

namespace MergeSurvivor.Kernel.Tests
{
    /// <summary>
    /// Loads the kernel: the schemas, the manifest and the live documents. Shared by the
    /// contract tests and the cross-check tests so both see exactly one view of the kernel.
    /// </summary>
    internal static class Kernel
    {
        private const string SchemaBaseUri = "https://mergesurvivor.studio/kernel/";

        private static readonly Lazy<string> RepoRootLazy = new Lazy<string>(FindRepoRoot);
        private static readonly Lazy<Dictionary<string, JsonSchema>> SchemasLazy =
            new Lazy<Dictionary<string, JsonSchema>>(LoadSchemas);

        public static string RepoRoot => RepoRootLazy.Value;

        /// <summary>Schemas keyed by file name, e.g. "task.schema.json".</summary>
        public static IReadOnlyDictionary<string, JsonSchema> Schemas => SchemasLazy.Value;

        public static string SchemaDir => Path.Combine(RepoRoot, "Studio", "kernel", "schemas");

        public static string FixtureDir => Path.Combine(RepoRoot, "Studio", "kernel", "fixtures");

        public static EvaluationOptions Options => new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,

            // Without this, "format": "date-time" is an annotation that never fails, and
            // every timestamp in the kernel would be unchecked.
            RequireFormatValidation = true
        };

        private static string FindRepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Studio", "kernel", "schemas")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                $"Could not find the repo root above {AppContext.BaseDirectory}.");
        }

        private static Dictionary<string, JsonSchema> LoadSchemas()
        {
            var loaded = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);

            foreach (string file in Directory.GetFiles(SchemaDir, "*.schema.json").OrderBy(f => f))
            {
                JsonSchema schema = JsonSchema.FromFile(file);
                string name = Path.GetFileName(file);

                // Registering by $id is what lets one schema $ref another by absolute URI.
                SchemaRegistry.Global.Register(new Uri(SchemaBaseUri + name), schema);
                loaded[name] = schema;
            }

            return loaded;
        }

        public static JsonSchema SchemaFor(string schemaFileName)
        {
            if (!Schemas.TryGetValue(schemaFileName, out JsonSchema schema))
            {
                throw new InvalidOperationException(
                    $"No schema named '{schemaFileName}' in {SchemaDir}.");
            }

            return schema;
        }

        public static JsonNode ReadJson(string absolutePath)
        {
            string text = File.ReadAllText(absolutePath);
            JsonNode node = JsonNode.Parse(text);
            if (node == null)
            {
                throw new InvalidOperationException($"{absolutePath} parsed to null.");
            }

            return node;
        }

        public static JsonNode ReadRepoJson(string relativePath) =>
            ReadJson(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>
        /// A fixture's schema is named by everything before the first dot, so
        /// "task.work-order.json" is governed by "task.schema.json".
        /// </summary>
        public static string SchemaNameFromFixture(string fixturePath)
        {
            string name = Path.GetFileName(fixturePath);
            int firstDot = name.IndexOf('.');
            if (firstDot <= 0)
            {
                throw new InvalidOperationException(
                    $"Fixture '{name}' must be named <schema>.<case>.json.");
            }

            return name.Substring(0, firstDot) + ".schema.json";
        }

        public static IEnumerable<string> FixtureFiles(string validOrInvalid) =>
            Directory.GetFiles(Path.Combine(FixtureDir, validOrInvalid), "*.json").OrderBy(f => f);

        /// <summary>
        /// Expands a manifest glob such as "Studio/decisions/ADR-*.json" or
        /// "Studio/evidence/**/EVD-*.json" into concrete repo-relative paths.
        /// </summary>
        public static IEnumerable<string> ExpandGlob(string glob)
        {
            string pattern = glob.Substring(glob.LastIndexOf('/') + 1);
            string dirPart = glob.Substring(0, glob.LastIndexOf('/'));

            SearchOption search = SearchOption.TopDirectoryOnly;
            int doubleStar = dirPart.IndexOf("/**", StringComparison.Ordinal);
            if (doubleStar >= 0)
            {
                dirPart = dirPart.Substring(0, doubleStar);
                search = SearchOption.AllDirectories;
            }

            string absoluteDir = Path.Combine(
                RepoRoot, dirPart.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(absoluteDir))
            {
                yield break;
            }

            foreach (string file in Directory.GetFiles(absoluteDir, pattern, search).OrderBy(f => f))
            {
                yield return Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            }
        }

        public static string Describe(EvaluationResults results)
        {
            IEnumerable<string> lines = results.Details
                .Where(d => d.HasErrors)
                .SelectMany(d => d.Errors.Select(e => $"  {d.InstanceLocation}: {e.Key} {e.Value}"));

            string joined = string.Join(Environment.NewLine, lines);
            return string.IsNullOrWhiteSpace(joined) ? "  (no detail reported)" : joined;
        }
    }
}
