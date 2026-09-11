using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Spark.Docs.Verify;

/// <summary>
/// The Dynamo parity manifest, checked against the coverage document and against
/// <c>Spark.Geometry</c> itself (<c>E11-T23</c>, <c>docs/DYNAMO-COVERAGE.md</c> §7).
/// </summary>
/// <remarks>
/// <para>
/// <b>A register that drifts is worse than no register, because it is consulted with
/// confidence.</b> The coverage document counted 837 members by hand, in prose and tables; this is the
/// mechanism its §7 proposed so that the counts, and every claim that a member is done, are checked on
/// every build. <c>tests/corpus/dynamo-parity.tsv</c> holds one row per public ProtoGeometry member,
/// generated once from the library's metadata and edited by hand as decisions land.
/// </para>
/// <para>
/// <b>What a green run means is narrow, and the failure message says so.</b> <i>Done</i> means the
/// named member is present in <c>Spark.Geometry</c>. It never means the member does what Dynamo's does
/// - proving that would need the dependency Spark exists to remove (ADR-0016) - and a green run is not a
/// review.
/// </para>
/// <para>
/// <b><c>Spark.Geometry</c> is read as metadata from its build output</b>, not loaded and not referenced:
/// this harness references no Spark project, so that it cannot constrain what it observes.
/// </para>
/// </remarks>
public sealed class DynamoParityChecks
{
    private static readonly string[] Statuses = ["Done", "Planned", "Not planned", "Needs a decision", "Unassessed"];

    private static readonly string Root = RepositoryRoot();

    private static readonly IReadOnlyList<ParityRow> Rows = ReadManifest(Path.Combine(Root, "tests", "corpus", "dynamo-parity.tsv"));

    /// <summary>
    /// Every row has a known status; a refusal or an open question says why; a done row names what in
    /// Spark it is; and no member appears twice.
    /// </summary>
    [Fact]
    public void EveryRowIsWellFormed()
    {
        List<string> problems = [];

        foreach (ParityRow row in Rows)
        {
            if (!Statuses.Contains(row.Status, StringComparer.Ordinal))
            {
                problems.Add($"line {row.Line}: status '{row.Status}' is not one of {string.Join(", ", Statuses)}.");
            }

            if (row.Status is "Not planned" or "Needs a decision" && row.Reason.Length == 0)
            {
                problems.Add($"line {row.Line}: {row.DynamoType}.{row.Member} is '{row.Status}' and gives no reason.");
            }

            if (row.Status == "Done" && row.SparkMember.Length == 0)
            {
                problems.Add($"line {row.Line}: {row.DynamoType}.{row.Member} is Done and names no Spark member.");
            }
        }

        foreach (IGrouping<string, ParityRow> twice in Rows
            .GroupBy(row => row.DynamoType + "." + row.Member, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            problems.Add($"{twice.Key} appears on lines {string.Join(", ", twice.Select(row => row.Line))}.");
        }

        Assert.Empty(problems);
    }

    /// <summary>
    /// The manifest and the coverage document agree: the headline type and member counts in §2, and
    /// every per-type count in §3's tables. Arithmetic rot is the likeliest failure of a document like
    /// that one, and the cheapest to catch.
    /// </summary>
    [Fact]
    public void TheManifestAgreesWithTheCoverageDocument()
    {
        string coverage = File.ReadAllText(Path.Combine(Root, "docs", "DYNAMO-COVERAGE.md"));
        Match headline = Regex.Match(coverage, @"\*\*(\d+) public types\*\* carrying \*\*(\d+) public members\*\*");

        Assert.True(headline.Success, "DYNAMO-COVERAGE §2 no longer states '**N public types** carrying **M public members**', which this check reads.");

        List<string> problems = [];
        int types = Rows.Select(row => row.DynamoType).Distinct(StringComparer.Ordinal).Count();

        if (types != int.Parse(headline.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
        {
            problems.Add($"§2 says {headline.Groups[1].Value} public types; the manifest has {types}.");
        }

        if (Rows.Count != int.Parse(headline.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))
        {
            problems.Add($"§2 says {headline.Groups[2].Value} public members; the manifest has {Rows.Count}.");
        }

        Dictionary<string, int> perType = Rows
            .GroupBy(row => ShortName(row.DynamoType), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (Match stated in Regex.Matches(coverage, @"^\| `(\w+)`(?: \(base\))? \| (\d+) \|", RegexOptions.Multiline))
        {
            string type = stated.Groups[1].Value;
            int count = int.Parse(stated.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);

            if (!perType.TryGetValue(type, out int actual))
            {
                problems.Add($"§3 lists `{type}` with {count} members, and the manifest has no rows for it.");
            }
            else if (actual != count)
            {
                problems.Add($"§3 says `{type}` has {count} members; the manifest has {actual}.");
            }
        }

        Assert.Empty(problems);
    }

    /// <summary>
    /// <b>The rename-catcher.</b> Every row marked Done names a member that <c>Spark.Geometry</c>
    /// declares, so a rename in the kernel turns this red instead of leaving the register claiming a
    /// capability under a name that no longer exists.
    /// </summary>
    [Fact]
    public void EveryDoneRowNamesASparkMemberThatExists()
    {
        List<ParityRow> done = [.. Rows.Where(row => row.Status == "Done")];

        // A check with nothing to check passes by doing nothing, which this harness does not allow.
        Assert.NotEmpty(done);

        Dictionary<string, HashSet<string>> declared = SparkGeometryMembers();
        List<string> missing = [];

        foreach (ParityRow row in done)
        {
            int dot = row.SparkMember.LastIndexOf('.');
            bool exists = declared.ContainsKey(row.SparkMember)
                || (dot > 0
                    && declared.TryGetValue(row.SparkMember[..dot], out HashSet<string>? members)
                    && members.Contains(row.SparkMember[(dot + 1)..]));

            if (!exists)
            {
                missing.Add($"line {row.Line}: {row.DynamoType}.{row.Member} is Done as {row.SparkMember}, which Spark.Geometry does not declare.");
            }
        }

        Assert.True(
            missing.Count == 0,
            "Done means present in Spark.Geometry - never equivalent to Dynamo's member (DYNAMO-COVERAGE §1). "
            + "These rows name members that are not there, most likely because one was renamed:\n  "
            + string.Join("\n  ", missing));
    }

    private static string ShortName(string dynamoType) => dynamoType[(dynamoType.LastIndexOf('.') + 1)..];

    private static List<ParityRow> ReadManifest(string path)
    {
        Assert.True(File.Exists(path), $"The Dynamo parity manifest is missing: {path}.");

        List<ParityRow> rows = [];
        string[] lines = File.ReadAllLines(path);
        bool header = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!header)
            {
                Assert.Equal("DynamoType\tMember\tStatus\tSparkMember\tReason", line);
                header = true;
                continue;
            }

            string[] cells = line.Split('\t');
            Assert.True(cells.Length == 5, $"line {i + 1} of the parity manifest has {cells.Length} cells, not 5.");
            rows.Add(new ParityRow(i + 1, cells[0], cells[1], cells[2], cells[3], cells[4]));
        }

        return rows;
    }

    /// <summary>
    /// The public types of <c>Spark.Geometry</c> and the names of their public members, read from the
    /// newest build of the assembly as metadata - nothing is loaded.
    /// </summary>
    private static Dictionary<string, HashSet<string>> SparkGeometryMembers()
    {
        string? assembly = new[] { "Debug", "Release" }
            .Select(configuration => Path.Combine(Root, "src", "Spark.Geometry", "bin", configuration, "net10.0", "Spark.Geometry.dll"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        Assert.True(assembly is not null, "Spark.Geometry.dll has not been built, so the parity manifest cannot be checked against it. Build the solution first.");

        using FileStream stream = File.OpenRead(assembly!);
        using PEReader reader = new(stream);
        MetadataReader metadata = reader.GetMetadataReader();
        Dictionary<string, HashSet<string>> types = new(StringComparer.Ordinal);

        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);

            if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
            {
                continue;
            }

            HashSet<string> members = new(StringComparer.Ordinal);

            foreach (MethodDefinitionHandle method in type.GetMethods())
            {
                MethodDefinition definition = metadata.GetMethodDefinition(method);

                if ((definition.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public)
                {
                    members.Add(metadata.GetString(definition.Name));
                }
            }

            foreach (PropertyDefinitionHandle property in type.GetProperties())
            {
                members.Add(metadata.GetString(metadata.GetPropertyDefinition(property).Name));
            }

            foreach (FieldDefinitionHandle field in type.GetFields())
            {
                FieldDefinition definition = metadata.GetFieldDefinition(field);

                if ((definition.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public)
                {
                    members.Add(metadata.GetString(definition.Name));
                }
            }

            string ns = metadata.GetString(type.Namespace);
            types[(ns.Length > 0 ? ns + "." : string.Empty) + metadata.GetString(type.Name)] = members;
        }

        return types;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private sealed record ParityRow(int Line, string DynamoType, string Member, string Status, string SparkMember, string Reason);
}
