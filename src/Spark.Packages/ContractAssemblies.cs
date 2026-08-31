using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Spark.Packages;

/// <summary>
/// The assemblies that must always resolve from the default load context, whatever a package
/// happens to have shipped alongside its own binaries (<c>E7-T4</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule this enforces is the one that makes packages usable at all.</b> A
/// <c>Circle</c> produced by a node from package A has to be the <i>same</i>
/// <see cref="System.Type"/> as a <c>Circle</c> consumed by a node from package B, or no wire
/// between them can exist. Two copies of <c>Spark.Geometry</c> loaded from two package folders
/// would produce two types with the same name, the same shape, and no assignment between them —
/// the single most confusing failure this layer can produce, and one whose error message names
/// the same type twice.
/// </para>
/// <para>
/// <b>Why it matters in practice rather than in theory:</b> NuGet packages routinely ship copies
/// of everything they were compiled against. A package built against <c>Spark.Api</c> will very
/// often carry <c>Spark.Api.dll</c> in its own <c>lib</c> folder, so a load context that decided
/// purely by file existence would pick up that copy every time. This check therefore runs
/// <b>before</b> the file check in <see cref="PackageLoadContext"/>, and the order is the
/// mechanism rather than an optimisation.
/// </para>
/// <para>
/// <b>The corollary is recorded as ADR-0019:</b> because these cannot be side-by-sided, a
/// breaking change to any of them is a deliberate act with a release note naming who has to
/// recompile, not a routine refactor.
/// </para>
/// </remarks>
public static class ContractAssemblies
{
    /// <summary>
    /// The simple assembly names that are always shared. Deliberately short: every name here is a
    /// permanent compatibility obligation, so a type belongs on this list only when instances of
    /// it genuinely cross the boundary between Spark and a package.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>Spark.Api</c> — node attributes, <c>SparkList</c>, diagnostics, the kernel seam.</item>
    /// <item><c>Spark.Geometry</c> — every geometric value a node takes or returns.</item>
    /// <item><c>Spark.Geometry.Io</c> — the interchange types those values serialise through.</item>
    /// <item>
    /// <c>Spark.Engine</c> — a package that defines nodes by hand rather than by reflection
    /// touches <c>NodeDefinition</c> and <c>PortDefinition</c> directly.
    /// </item>
    /// </list>
    /// <b>Not on the list, deliberately:</b> <c>Spark.Nodes.Core</c>, <c>Spark.Viewport</c>,
    /// <c>Spark.UI</c> and <c>Spark.Scripting</c>. Nothing a package legitimately does requires
    /// its types, and adding one here would convert an internal detail into a public promise.
    /// </remarks>
    public static ImmutableArray<string> Names { get; } =
    [
        "Spark.Api",
        "Spark.Geometry",
        "Spark.Geometry.Io",
        "Spark.Engine",
    ];

    private static readonly HashSet<string> Lookup =
        new(Names, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether an assembly name must resolve from the default context.</summary>
    /// <param name="simpleName">
    /// The simple assembly name — <c>Spark.Api</c>, not a full display name and not a file name.
    /// </param>
    /// <returns>
    /// True when the assembly is a contract assembly. A null or blank name returns false, because
    /// an unnamed assembly is not one of ours and the caller's next step handles it.
    /// </returns>
    public static bool IsContract(string? simpleName) =>
        !string.IsNullOrEmpty(simpleName) && Lookup.Contains(simpleName);
}
