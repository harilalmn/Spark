using System;
using System.Threading.Tasks;
using Spark.Api;

namespace Spark.Engine.Tests;

// Fixtures for NodeImporterTests. They are top-level types rather than nested ones because the
// importer excludes nested types in this slice, and a fixture that exercises nothing is worse
// than no fixture. Spark.Nodes.Core is deliberately all static classes, so these are the only
// place the constructor, receiver, property, operator and dedup paths are reached at all.

/// <summary>A type whose constructor is shadowed by a factory with the same parameter types.</summary>
public sealed class ImportedCircle
{
    /// <summary>Creates a circle.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radius">The radius.</param>
    public ImportedCircle(double center, double radius)
    {
        Center = center;
        Radius = radius;
    }

    /// <summary>The center.</summary>
    public double Center { get; }

    /// <summary>The radius.</summary>
    public double Radius { get; }

    /// <summary>The By* facade that shadows the constructor.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radius">The radius.</param>
    /// <returns>The circle.</returns>
    public static ImportedCircle FromCenterRadius(double center, double radius) => new(center, radius);
}

/// <summary>
/// A type whose constructor survives, because its only factory has a different parameter type
/// sequence. Also carries an instance method and a property.
/// </summary>
public readonly struct ImportedSegment : IEquatable<ImportedSegment>
{
    /// <summary>Creates a segment.</summary>
    /// <param name="start">The start.</param>
    /// <param name="end">The end.</param>
    public ImportedSegment(double start, double end)
    {
        Start = start;
        End = end;
    }

    /// <summary>The start.</summary>
    public double Start { get; }

    /// <summary>The end.</summary>
    public double End { get; }

    /// <summary>The length.</summary>
    public double Length => Math.Abs(End - Start);

    /// <summary>One parameter, so the parameter type sequence does not match the constructor's.</summary>
    /// <param name="length">The length.</param>
    /// <returns>A segment from zero.</returns>
    public static ImportedSegment ByLength(double length) => new(0, length);

    /// <summary>The segment the other way round.</summary>
    /// <returns>The reversed segment.</returns>
    public ImportedSegment Reversed() => new(End, Start);

    /// <inheritdoc/>
    public bool Equals(ImportedSegment other) => Start == other.Start && End == other.End;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ImportedSegment other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Start, End);
}

/// <summary>Members the importer refuses, one per rule.</summary>
public static class ImportedAwkward
{
    /// <summary>
    /// A generic method the importer still refuses: its type parameter is constrained, so closing it
    /// over <see cref="object"/> would break the constraint. An unconstrained one is imported since
    /// `E5-T10`, which is why this is not the plain identity it once was.
    /// </summary>
    /// <typeparam name="T">A comparable type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The value.</returns>
    public static T Identity<T>(T value)
        where T : System.IComparable<T> => value;

    /// <summary>A ref parameter, which is both an input and an output.</summary>
    /// <param name="value">The value.</param>
    public static void Mutate(ref double value) => value += 1;

    /// <summary>Void with no out parameter, so it produces nothing a graph can carry.</summary>
    public static void DoNothing()
    {
    }

    /// <summary>Excluded by its author.</summary>
    /// <returns>Nothing anyone should call.</returns>
    [NodeIgnore("plumbing, not an operation")]
    public static double Internal() => 0;

    /// <summary>An ordinary method, so the fixture is not entirely exclusions.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value.</returns>
    public static double Keep(double value) => value;
}

/// <summary>A value type carrying a real operator, so <c>op_Addition</c> is genuinely special-name.</summary>
public readonly struct ImportedAmount : IEquatable<ImportedAmount>
{
    /// <summary>Creates an amount.</summary>
    /// <param name="value">The value.</param>
    public ImportedAmount(double value) => Value = value;

    /// <summary>The value.</summary>
    public double Value { get; }

    /// <summary>Adds two amounts.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>The sum.</returns>
    public static ImportedAmount operator +(ImportedAmount left, ImportedAmount right) =>
        new(left.Value + right.Value);

    /// <summary>The named form of the operator, which is the node.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>The sum.</returns>
    public static ImportedAmount Add(ImportedAmount left, ImportedAmount right) => left + right;

    /// <inheritdoc/>
    public bool Equals(ImportedAmount other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ImportedAmount other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>Two overloads of one name.</summary>
public static class ImportedOverloads
{
    /// <summary>Two arguments.</summary>
    /// <param name="a">The first.</param>
    /// <param name="b">The second.</param>
    /// <returns>The sum.</returns>
    public static double Combine(double a, double b) => a + b;

    /// <summary>Three arguments.</summary>
    /// <param name="a">The first.</param>
    /// <param name="b">The second.</param>
    /// <param name="c">The third.</param>
    /// <returns>The sum.</returns>
    public static double Combine(double a, double b, double c) => a + b + c;
}

/// <summary>A type the author says is not a library type.</summary>
[NodeIgnore("a test fixture, not a library type")]
public static class ImportedNothing
{
    /// <summary>Never a node.</summary>
    /// <returns>Zero.</returns>
    public static double Whatever() => 0;
}

/// <summary>
/// The four asynchronous shapes a node importer meets (<c>E5-T3</c>).
/// </summary>
/// <remarks>
/// <b>Written as real <c>async</c> members rather than as methods returning a completed task</b>,
/// so that the awaited call really does go through a state machine and a continuation. A method
/// returning <c>Task.FromResult</c> completes synchronously and would pass a test that blocked
/// forever on anything that did not.
/// </remarks>
public static class ImportedAsync
{
    /// <summary>A number, eventually.</summary>
    /// <param name="a">What to double.</param>
    /// <returns>Twice it.</returns>
    public static async Task<double> Twice(double a)
    {
        await Task.Yield();
        return a * 2;
    }

    /// <summary>A number from a value task, eventually.</summary>
    /// <param name="a">What to treble.</param>
    /// <returns>Three times it.</returns>
    public static async ValueTask<double> Treble(double a)
    {
        await Task.Yield();
        return a * 3;
    }

    /// <summary>Produces nothing, eventually.</summary>
    /// <param name="a">Ignored.</param>
    /// <returns>Nothing.</returns>
    public static async Task Nothing(double a)
    {
        await Task.Yield();
        _ = a;
    }

    /// <summary>Produces nothing through a value task, eventually.</summary>
    /// <param name="a">Ignored.</param>
    /// <returns>Nothing.</returns>
    public static async ValueTask NothingAtAll(double a)
    {
        await Task.Yield();
        _ = a;
    }
}
