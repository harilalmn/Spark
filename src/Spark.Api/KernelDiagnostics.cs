namespace Spark.Api;

/// <summary>
/// The diagnostic codes a BRep kernel raises.
/// </summary>
/// <remarks>
/// <b>Here rather than in <c>Spark.Engine</c>'s registry, because the seam is a contract.</b>
/// <see cref="IBrepKernel"/> is implemented by a provider assembly that references this one and not
/// the engine, so its refusals have to be nameable from here. The engine's <c>DiagnosticCodes</c>
/// re-exports both constants and registers their help topic, which is what keeps the whole
/// <c>SPK####</c> space visible to the coverage test that insists every code resolves to a topic.
/// </remarks>
public static class KernelDiagnostics
{
    /// <summary>The help topic for exact solid operations and the kernel that performs them.</summary>
    public const string SolidsTopic = "concepts.solids";

    /// <summary>
    /// An exact solid operation was asked for and no kernel provider is installed.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Refused"/> on purpose: *nothing here can do this* and *the geometry
    /// does not permit this* call for entirely different responses from a user, and a single code
    /// would make the help topic have to say both.
    /// </remarks>
    public const string Unavailable = "SPK1080";

    /// <summary>
    /// A kernel provider refused an operation because the geometry does not permit it.
    /// </summary>
    /// <remarks>
    /// <b>Ordinary rather than exceptional.</b> A fillet whose radius does not fit, a boolean of
    /// two solids that do not touch, a loft between profiles that cannot be matched — an exact
    /// kernel refuses these constantly and correctly, which is why they arrive as a value rather
    /// than as an exception.
    /// </remarks>
    public const string Refused = "SPK1081";
}
