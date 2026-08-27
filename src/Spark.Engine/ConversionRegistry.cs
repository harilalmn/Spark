using System;
using System.Collections.Generic;

namespace Spark.Engine;

/// <summary>
/// One registered conversion between two types.
/// </summary>
/// <param name="Source">The type converted from.</param>
/// <param name="Target">The type converted to.</param>
/// <param name="Convert">The conversion.</param>
/// <param name="IsLossy">
/// Whether information may be lost. A lossy conversion is still allowed, but the wire is drawn
/// yellow and its tooltip says so — the user gets to decide, having been told.
/// </param>
public sealed record ConversionRule(Type Source, Type Target, Func<object?, object?> Convert, bool IsLossy);

/// <summary>
/// The conversions one session will apply inside a wire.
/// </summary>
/// <remarks>
/// This is an instance, deliberately, and there is no static default anybody can add to. A global
/// registry would mean one package changing what another package's wires do, at a distance, with
/// nothing in either graph recording that it happened.
/// </remarks>
public sealed class ConversionRegistry
{
    private readonly Dictionary<(Type Source, Type Target), ConversionRule> _rules = [];

    /// <summary>Registers a conversion, replacing any existing one between the same two types.</summary>
    /// <param name="rule">The conversion.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
    public void Register(ConversionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules[(rule.Source, rule.Target)] = rule;
    }

    /// <summary>Registers a typed conversion.</summary>
    /// <typeparam name="TSource">The type converted from.</typeparam>
    /// <typeparam name="TTarget">The type converted to.</typeparam>
    /// <param name="convert">The conversion.</param>
    /// <param name="isLossy">Whether information may be lost.</param>
    /// <exception cref="ArgumentNullException"><paramref name="convert"/> is <see langword="null"/>.</exception>
    public void Register<TSource, TTarget>(Func<TSource, TTarget> convert, bool isLossy = false)
    {
        ArgumentNullException.ThrowIfNull(convert);
        Register(new ConversionRule(
            typeof(TSource),
            typeof(TTarget),
            value => value is TSource source ? convert(source) : null,
            isLossy));
    }

    /// <summary>Looks up a conversion.</summary>
    /// <param name="source">The type converted from.</param>
    /// <param name="target">The type converted to.</param>
    /// <param name="rule">The conversion, when one is registered.</param>
    /// <returns><see langword="true"/> when a conversion is registered.</returns>
    public bool TryGet(Type source, Type target, out ConversionRule? rule) =>
        _rules.TryGetValue((source, target), out rule);
}
