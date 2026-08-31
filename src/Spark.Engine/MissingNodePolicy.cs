namespace Spark.Engine;

/// <summary>
/// What <see cref="GraphDocument.Restore"/> does about a node whose definition is not in the
/// library — almost always because the package that defines it is not installed.
/// </summary>
/// <remarks>
/// <b>The default is <see cref="Placeholder"/>, and the direction of that default is the
/// decision.</b> A caller who wanted strictness and gets a placeholder sees a graph that reports
/// errors, which is visible and recoverable. A user who wanted their graph open and gets a refusal
/// loses access to everything else in it. The first failure is an inconvenience; the second is the
/// thing <c>E7-T6</c> exists to prevent.
/// </remarks>
public enum MissingNodePolicy
{
    /// <summary>
    /// Substitute a <see cref="PlaceholderNode"/> that preserves the key, every literal and every
    /// wire verbatim, and refuses to evaluate. The default.
    /// </summary>
    Placeholder,

    /// <summary>
    /// Refuse to open the document, naming the node. For tools that must not proceed on an
    /// incomplete graph — a headless <c>spark check</c> rather than a person at a canvas.
    /// </summary>
    Refuse,
}
