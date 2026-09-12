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
/// The Dynamo parity manifest, checked against the coverage document and against the assemblies that
/// deliver Spark's geometry (<c>E11-T23</c>, <c>E11-T30</c>, <c>docs/DYNAMO-COVERAGE.md</c> §7).
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
/// named member is present in Spark. It never means the member does what Dynamo's does
/// - proving that would need the dependency Spark exists to remove (ADR-0016) - and a green run is not a
/// review.
/// </para>
/// <para>
/// <b>The assemblies are read as metadata from their build output</b>, not loaded and not referenced:
/// this harness references no Spark project, so that it cannot constrain what it observes.
/// </para>
/// <para>
/// <b>The two directions deliberately read different things</b> (<c>E11-T30</c>). The rename-catcher
/// reads <b>every assembly that delivers geometry</b> - <c>Spark.Geometry</c>, <c>Spark.Api</c> and
/// <c>Spark.Nodes.Core</c> - because §1 and FR-81 make the register's subject *capability*, and loft,
/// sweep, thicken and the booleans are delivered through <c>Spark.Api.IBrepKernel</c>. Scoping it to
/// one assembly made twenty §3.3 rows say <i>Planned</i> about capabilities that already worked, which
/// is the register lying in the safe direction (<c>docs/NOTES.md</c> N158).
/// </para>
/// <para>
/// <b>The reverse direction stays on <c>Spark.Geometry</c> alone</b>, and that is not an oversight.
/// It asks *has a member drifted away from the plan it was meant to satisfy*, which is a question
/// about the kernel's own surface; its residue budget is a measure of that surface against the
/// register. <c>Spark.Nodes.Core</c> is a node library whose public surface exists to be imported by
/// reflection, and counting it here would swamp the number that makes the budget worth checking.
/// </para>
/// </remarks>
public sealed class DynamoParityChecks
{
    private static readonly string[] Statuses = ["Done", "Planned", "Not planned", "Needs a decision", "Unassessed"];

    /// <summary>
    /// <b>The assemblies that deliver geometry to a Spark user</b>, in the order a reader should think
    /// of them (<c>E11-T30</c>). <c>Spark.Api</c> is here because <c>IBrepKernel</c> is, and with it
    /// loft, sweep, thicken and the booleans; <c>Spark.Nodes.Core</c> because the node families over
    /// the kernel are how a graph reaches them. Adding an assembly here widens what <i>Done</i> may
    /// name and nothing else - the reverse direction is scoped separately, on purpose.
    /// </summary>
    private static readonly string[] Delivering = ["Spark.Geometry", "Spark.Api", "Spark.Nodes.Core"];

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
    /// <b>The rename-catcher.</b> Every row marked Done names a member that one of the delivering
    /// assemblies declares, so a rename anywhere in that surface turns this red instead of leaving the
    /// register claiming a capability under a name that no longer exists.
    /// </summary>
    [Fact]
    public void EveryDoneRowNamesASparkMemberThatExists()
    {
        List<ParityRow> done = [.. Rows.Where(row => row.Status == "Done")];

        // A check with nothing to check passes by doing nothing, which this harness does not allow.
        Assert.NotEmpty(done);

        Dictionary<string, HashSet<string>> declared = DeliveredMembers();
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
                missing.Add($"line {row.Line}: {row.DynamoType}.{row.Member} is Done as {row.SparkMember}, which no delivering assembly declares.");
            }
        }

        Assert.True(
            missing.Count == 0,
            "Done means present in Spark - never equivalent to Dynamo's member (DYNAMO-COVERAGE §1). "
            + "These rows name members that are not there, most likely because one was renamed:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// <b>The reverse direction.</b> Every public member of <c>Spark.Geometry</c> is named by a parity
    /// row, excused by a rule in the exclusions file, or counted against the residue budget - so a
    /// member cannot drift away from the plan it was meant to satisfy without somebody noticing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The budget is checked exactly, and that is deliberate.</b> A ceiling alone would let the
    /// number sit where it is for ever; requiring the file to agree with the assembly means the count
    /// falls as rows are assessed and cannot quietly climb back. It is the same rule
    /// <c>bench/budgets.jsonc</c> applies to performance: a measurement with no budget beside it is
    /// not a guard.
    /// </para>
    /// <para>
    /// <b>A type a parity row names may not be excused wholesale</b>, which is the rule that stops this
    /// file becoming a way to make the check green. It is enforced here rather than trusted.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryPublicMemberIsNamedExcusedOrCounted()
    {
        // Spark.Geometry alone, and deliberately - see the remarks on this type.
        Dictionary<string, HashSet<string>> declared = PublicMembers("Spark.Geometry");
        List<Exclusion> exclusions = ReadExclusions();
        List<string> problems = [];

        HashSet<string> memberRules = [.. exclusions.Where(e => e.Scope == "MemberName").Select(e => e.Name)];
        HashSet<string> excusedTypes = [.. exclusions.Where(e => e.Scope == "Type").Select(e => e.Name)];
        bool operators = memberRules.Remove("op_*");

        Dictionary<string, HashSet<string>> named = new(StringComparer.Ordinal);
        HashSet<string> namedTypes = new(StringComparer.Ordinal);

        foreach (ParityRow row in Rows.Where(row => row.SparkMember.Length > 0))
        {
            // A row may name a TYPE rather than a member, and a Done row legitimately does when the
            // capability is a construction: Dynamo's `Surface.ByRevolve` is Spark's
            // `new RevolutionSurface(...)`, and a constructor is not a member this inventory counts.
            // Such a row still claims the type, so the type may not then be excused wholesale -
            // without this, naming a bare type was a silent way past that rule (E2-T42 found it).
            if (declared.ContainsKey(row.SparkMember))
            {
                namedTypes.Add(row.SparkMember);
                continue;
            }

            int dot = row.SparkMember.LastIndexOf('.');

            if (dot > 0)
            {
                if (!named.TryGetValue(row.SparkMember[..dot], out HashSet<string>? members))
                {
                    named[row.SparkMember[..dot]] = members = new HashSet<string>(StringComparer.Ordinal);
                }

                members.Add(row.SparkMember[(dot + 1)..]);
            }
        }

        foreach (Exclusion exclusion in exclusions)
        {
            if (exclusion.Reason.Length == 0)
            {
                problems.Add($"line {exclusion.Line}: {exclusion.Scope} '{exclusion.Name}' gives no reason.");
            }

            if (exclusion.Scope == "Type" && !declared.ContainsKey(exclusion.Name))
            {
                problems.Add($"line {exclusion.Line}: {exclusion.Name} is excused and Spark.Geometry does not declare it - delete the row.");
            }

            if (exclusion.Scope == "Type" && named.TryGetValue(exclusion.Name, out HashSet<string>? claimed) && claimed.Count > 0)
            {
                problems.Add($"line {exclusion.Line}: {exclusion.Name} is excused wholesale and a parity row names {claimed.Count} of its members. A type a row names cannot be excused.");
            }

            if (exclusion.Scope == "Type" && namedTypes.Contains(exclusion.Name))
            {
                problems.Add($"line {exclusion.Line}: {exclusion.Name} is excused wholesale and a parity row names the type itself. A type a row names cannot be excused.");
            }
        }

        List<string> residue = [];
        HashSet<string> rulesUsed = new(StringComparer.Ordinal);

        foreach ((string type, HashSet<string> members) in declared)
        {
            foreach (string member in members)
            {
                if (named.TryGetValue(type, out HashSet<string>? rows) && rows.Contains(member))
                {
                    continue;
                }

                if (memberRules.Contains(member))
                {
                    rulesUsed.Add(member);
                    continue;
                }

                if (operators && member.StartsWith("op_", StringComparison.Ordinal))
                {
                    rulesUsed.Add("op_*");
                    continue;
                }

                if (excusedTypes.Contains(type))
                {
                    continue;
                }

                residue.Add($"{ShortName(type)}.{member}");
            }
        }

        foreach (string rule in memberRules.Concat(operators ? ["op_*"] : []))
        {
            if (!rulesUsed.Contains(rule))
            {
                problems.Add($"the member rule '{rule}' excuses nothing any more - delete it.");
            }
        }

        Exclusion budget = exclusions.SingleOrDefault(e => e.Scope == "Budget" && e.Name == "residue")
            ?? throw new InvalidOperationException("The exclusions file has no residue budget.");

        int allowed = int.Parse(
            new string([.. budget.Reason.TakeWhile(char.IsDigit)]),
            System.Globalization.CultureInfo.InvariantCulture);

        if (residue.Count != allowed)
        {
            residue.Sort(StringComparer.Ordinal);

            problems.Add(
                $"the residue budget says {allowed} and the assembly has {residue.Count}. "
                + (residue.Count > allowed
                    ? "A member was added to a type that maps to a Dynamo type without a thought for the register: name it from the row it satisfies, or raise the budget and say why."
                    : "Rows have been assessed, which is the point - lower the budget to match.")
                + "\n  " + string.Join("\n  ", residue.Take(12))
                + (residue.Count > 12 ? $"\n  ... and {residue.Count - 12} more" : string.Empty));
        }

        Assert.Empty(problems);
    }

    private static List<Exclusion> ReadExclusions()
    {
        string path = Path.Combine(Root, "tests", "corpus", "dynamo-parity-exclusions.tsv");
        Assert.True(File.Exists(path), $"The parity exclusions file is missing: {path}.");

        List<Exclusion> exclusions = [];
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
                Assert.Equal("Scope\tName\tReason", line);
                header = true;
                continue;
            }

            string[] cells = line.Split('\t');
            Assert.True(cells.Length == 3, $"line {i + 1} of the parity exclusions has {cells.Length} cells, not 3.");
            Assert.Contains(cells[0], (string[])["MemberName", "Type", "Budget"], StringComparer.Ordinal);
            exclusions.Add(new Exclusion(i + 1, cells[0], cells[1], cells[2]));
        }

        return exclusions;
    }

    private static string ShortName(string dynamoType) => dynamoType[(dynamoType.LastIndexOf('.') + 1)..];

    private static bool IsPublic(MetadataReader metadata, MethodDefinitionHandle accessor) =>
        !accessor.IsNil
        && (metadata.GetMethodDefinition(accessor).Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;

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
    /// The public types of every assembly that delivers geometry, and the names of their public
    /// members (<c>E11-T30</c>). Where two assemblies declare the same type name the members are
    /// merged, which is right: a row names a member, and the question is whether *Spark* declares it.
    /// </summary>
    private static Dictionary<string, HashSet<string>> DeliveredMembers()
    {
        Dictionary<string, HashSet<string>> all = new(StringComparer.Ordinal);

        foreach (string project in Delivering)
        {
            foreach ((string type, HashSet<string> members) in PublicMembers(project))
            {
                if (all.TryGetValue(type, out HashSet<string>? existing))
                {
                    existing.UnionWith(members);
                }
                else
                {
                    all[type] = members;
                }
            }
        }

        return all;
    }

    /// <summary>
    /// The public types of one assembly and the names of their public members, read from its newest
    /// build as metadata - nothing is loaded.
    /// </summary>
    private static Dictionary<string, HashSet<string>> PublicMembers(string project)
    {
        string? assembly = new[] { "Debug", "Release" }
            .Select(configuration => Path.Combine(Root, "src", project, "bin", configuration, "net10.0", project + ".dll"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        Assert.True(assembly is not null, $"{project}.dll has not been built, so the parity manifest cannot be checked against it. Build the solution first.");

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

                if ((definition.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                {
                    continue;
                }

                string name = metadata.GetString(definition.Name);

                // A constructor is not a member the inventory counts, and an accessor is counted
                // through its property. Operators are counted, which is why the special-name test
                // lets `op_` through: they are members a user calls.
                if (name is ".ctor" or ".cctor"
                    || ((definition.Attributes & MethodAttributes.SpecialName) != 0
                        && !name.StartsWith("op_", StringComparison.Ordinal)))
                {
                    continue;
                }

                members.Add(name);
            }

            foreach (PropertyDefinitionHandle property in type.GetProperties())
            {
                PropertyDefinition definition = metadata.GetPropertyDefinition(property);
                PropertyAccessors accessors = definition.GetAccessors();

                if (IsPublic(metadata, accessors.Getter) || IsPublic(metadata, accessors.Setter))
                {
                    members.Add(metadata.GetString(definition.Name));
                }
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

    private sealed record Exclusion(int Line, string Scope, string Name, string Reason);
}
