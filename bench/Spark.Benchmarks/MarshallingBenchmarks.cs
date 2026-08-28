using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Benchmarks;

/// <summary>
/// `SparkList` in and out of the CLR types a port declares.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the performance-critical path of the entire engine</b> (`E4-T3`). Every element of
/// every replicated call crosses it twice — once to hand a node its arguments in the types its
/// signature asks for, and once to put the answer back into the graph's own representation — so a
/// regression here is not felt as one slow node but as every node in the graph being slower at
/// once.
/// </para>
/// <para>
/// The sizes are chosen to bracket the shape real graphs have rather than to flatter the numbers:
/// ten is a hand-typed list, a thousand is a divided curve, and a hundred thousand is the point at
/// which somebody notices. All three matter because the costs are different in kind — allocation
/// dominates at the top, per-call overhead at the bottom.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class MarshallingBenchmarks
{
    private SparkList _numbers = SparkList.Empty(1);
    private SparkList _points = SparkList.Empty(1);
    private double[] _clrNumbers = [];
    private Point3d[] _clrPoints = [];

    /// <summary>How many elements the list under test holds.</summary>
    [Params(10, 1_000, 100_000)]
    public int Count { get; set; }

    /// <summary>Builds the lists once per size, outside the measured region.</summary>
    [GlobalSetup]
    public void Setup()
    {
        object?[] numbers = new object?[Count];
        object?[] points = new object?[Count];
        _clrNumbers = new double[Count];
        _clrPoints = new Point3d[Count];

        for (int index = 0; index < Count; index++)
        {
            double value = index * 0.5;
            numbers[index] = value;
            points[index] = new Point3d(value, value, value);
            _clrNumbers[index] = value;
            _clrPoints[index] = new Point3d(value, value, value);
        }

        _numbers = new SparkList(numbers, 1);
        _points = new SparkList(points, 1);
    }

    /// <summary>A list of numbers into the `IReadOnlyList&lt;double&gt;` a port declares.</summary>
    /// <returns>The converted value.</returns>
    [Benchmark(Description = "SparkList -> IReadOnlyList<double>")]
    public object? NumbersToClr() => ValueMarshal.ToClr(_numbers, typeof(IReadOnlyList<double>));

    /// <summary>The same, for a geometry type — where the elements are structs rather than boxed doubles.</summary>
    /// <returns>The converted value.</returns>
    [Benchmark(Description = "SparkList -> IReadOnlyList<Point3d>")]
    public object? PointsToClr() => ValueMarshal.ToClr(_points, typeof(IReadOnlyList<Point3d>));

    /// <summary>
    /// A node's answer back into the graph's representation.
    /// </summary>
    /// <remarks>
    /// <b>The declared rank is 1 and that is the whole benchmark.</b> A port returning
    /// <c>IReadOnlyList&lt;double&gt;</c> declares rank 1; at rank 0 <see cref="ValueMarshal.FromClr"/>
    /// returns the value untouched, which measured 0.6 ns at every size — a benchmark that could
    /// not regress, written by getting one argument wrong. It was caught by the number being
    /// flat across three orders of magnitude, which is what those sizes are for.
    /// </remarks>
    /// <returns>The converted value.</returns>
    [Benchmark(Description = "double[] -> SparkList")]
    public object? NumbersFromClr() => ValueMarshal.FromClr(_clrNumbers, declaredRank: 1);

    /// <summary>The same, for geometry.</summary>
    /// <returns>The converted value.</returns>
    [Benchmark(Description = "Point3d[] -> SparkList")]
    public object? PointsFromClr() => ValueMarshal.FromClr(_clrPoints, declaredRank: 1);
}
