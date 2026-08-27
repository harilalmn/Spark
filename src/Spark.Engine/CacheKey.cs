using System;
using System.Collections.Generic;
using System.Globalization;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Engine;

/// <summary>
/// The provenance of one node's result, reduced to 64 bits.
/// </summary>
/// <remarks>
/// <para>
/// <b>The key hashes provenance, never values.</b> It is built from the node definition, its
/// version, the effective lacing, the document tolerance, the run epoch if the node is impure, and
/// — recursively — the cache keys of everything upstream. It never touches the values themselves,
/// because hashing a two-million-triangle mesh costs more than recomputing it, and a cache that
/// costs more than the computation is not a cache.
/// </para>
/// <para>
/// The consequence users notice is that undo, redo, toggling a wire back and forth and dragging a
/// slider back to where it was are all instant: the old key is still resident, so the old result is
/// still there. That falls out of content-addressing; it is not a separate feature.
/// </para>
/// <para>
/// A literal typed into an unwired port is the one thing hashed by value, and that is safe because a
/// literal is something a person typed.
/// </para>
/// </remarks>
public readonly struct CacheKey : IEquatable<CacheKey>
{
    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    private CacheKey(ulong value) => Value = value;

    /// <summary>The 64-bit key.</summary>
    public ulong Value { get; }

    /// <summary>The key no node has, used for a node whose upstream produced nothing.</summary>
    public static CacheKey None => new(0);

    /// <summary>
    /// Builds the key for one node.
    /// </summary>
    /// <param name="definition">The node definition.</param>
    /// <param name="effectiveLacing">
    /// The lacing after <see cref="LacingMode.Auto"/> has been resolved. The resolved value is what
    /// goes in, so that changing a definition's default invalidates every instance that was relying
    /// on it.
    /// </param>
    /// <param name="tolerance">
    /// The document tolerance. It is in the key because a tolerance change must invalidate exactly
    /// the affected nodes, which is the decisive argument against an ambient tolerance: an ambient
    /// one would be invisible here and the graph would go on serving geometry computed at the old
    /// value.
    /// </param>
    /// <param name="runEpoch">
    /// The run counter. Mixed in only when the definition declares a side effect, so an impure node
    /// re-evaluates every run and poisons the keys of everything downstream of it.
    /// </param>
    /// <param name="inputs">
    /// One entry per input port: the upstream key and source port when the port is wired, and the
    /// literal when it is not.
    /// </param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> or <paramref name="inputs"/> is <see langword="null"/>.</exception>
    public static CacheKey For(
        NodeDefinition definition,
        LacingMode effectiveLacing,
        in Tolerance tolerance,
        long runEpoch,
        IReadOnlyList<CacheKeyInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(inputs);

        ulong hash = FnvOffsetBasis;

        hash = MixString(hash, definition.Key.Value);
        hash = MixInt64(hash, definition.Version);
        hash = MixInt64(hash, (long)effectiveLacing);
        hash = MixString(hash, tolerance.Linear.ToString("R", CultureInfo.InvariantCulture));
        hash = MixString(hash, tolerance.Angular.Radians.ToString("R", CultureInfo.InvariantCulture));

        if (definition.IsSideEffect)
        {
            hash = MixInt64(hash, runEpoch);
        }

        foreach (CacheKeyInput input in inputs)
        {
            hash = input.UpstreamKey.HasValue
                ? MixInt64(MixInt64(hash, unchecked((long)input.UpstreamKey.Value.Value)), input.SourcePort)
                : MixInt64(hash, HashLiteral(input.Literal));
        }

        return new CacheKey(hash);
    }

    /// <summary>Whether two keys are the same.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    public static bool operator ==(CacheKey left, CacheKey right) => left.Equals(right);

    /// <summary>Whether two keys differ.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(CacheKey left, CacheKey right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(CacheKey other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => Value.ToString("X16", CultureInfo.InvariantCulture);

    private static long HashLiteral(object? literal)
    {
        switch (literal)
        {
            case null:
                return unchecked((long)FnvPrime);

            case SparkList list:
                {
                    ulong hash = MixInt64(FnvOffsetBasis, list.Rank);
                    foreach (object? item in list)
                    {
                        hash = MixInt64(hash, HashLiteral(item));
                    }

                    return unchecked((long)hash);
                }

            case string text:
                return unchecked((long)MixString(FnvOffsetBasis, text));

            case IFormattable formattable:
                return unchecked((long)MixString(
                    FnvOffsetBasis, formattable.ToString("R", CultureInfo.InvariantCulture)));

            default:
                return literal.GetHashCode();
        }
    }

    private static ulong MixString(ulong hash, string text)
    {
        foreach (char character in text)
        {
            hash = unchecked((hash ^ character) * FnvPrime);
        }

        return unchecked((hash ^ 0xFF) * FnvPrime);
    }

    private static ulong MixInt64(ulong hash, long value)
    {
        ulong bits = unchecked((ulong)value);
        for (int shift = 0; shift < 64; shift += 8)
        {
            hash = unchecked((hash ^ ((bits >> shift) & 0xFF)) * FnvPrime);
        }

        return hash;
    }
}

/// <summary>
/// What one input port contributes to a cache key: an upstream node's key, or a literal.
/// </summary>
/// <param name="UpstreamKey">The key of the node feeding this port, or <see langword="null"/> when unwired.</param>
/// <param name="SourcePort">The upstream output port index. Ignored when unwired.</param>
/// <param name="Literal">The literal value on an unwired port.</param>
public readonly record struct CacheKeyInput(CacheKey? UpstreamKey, int SourcePort, object? Literal)
{
    /// <summary>Describes a wired port.</summary>
    /// <param name="upstreamKey">The upstream node's cache key.</param>
    /// <param name="sourcePort">The upstream output port index.</param>
    /// <returns>The contribution.</returns>
    public static CacheKeyInput Wired(CacheKey upstreamKey, int sourcePort) => new(upstreamKey, sourcePort, null);

    /// <summary>Describes an unwired port.</summary>
    /// <param name="literal">The literal typed into the port.</param>
    /// <returns>The contribution.</returns>
    public static CacheKeyInput Unwired(object? literal) => new(null, 0, literal);
}
