using System;
using System.Collections.Generic;
using System.Globalization;
using Spark.Api;

namespace Spark.Engine.Tests;

/// <summary>
/// Comparison for graph values in assertions.
/// </summary>
/// <remarks>
/// It compares shape and contents, and it deliberately does <b>not</b> compare rank — rank is
/// asserted separately, by its own <c>Assert</c> call, because a comparison that folds the two
/// together is one a rank bug can pass. A flat list of a hundred and a ten-by-ten nested list have
/// different ranks and can have identical leaves.
/// </remarks>
public static class GraphValues
{
    /// <summary>Asserts two graph values are equal in shape and contents, saying nothing about rank.</summary>
    /// <param name="expected">The expected value.</param>
    /// <param name="actual">The produced value.</param>
    public static void AssertEqual(object? expected, object? actual)
    {
        string? difference = Compare(expected, actual, string.Empty);
        Assert.True(difference is null, $"{difference}\n  expected: {Describe(expected)}\n  actual:   {Describe(actual)}");
    }

    /// <summary>Renders a graph value the way the lacing table writes it.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The rendered value.</returns>
    public static string Describe(object? value) => value switch
    {
        null => "null",
        SparkList list => list.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };

    private static string? Compare(object? expected, object? actual, string path)
    {
        string where = path.Length == 0 ? "the value" : $"the value at {path}";

        if (expected is null || actual is null)
        {
            return expected is null && actual is null ? null : $"{where} differs: one side is null.";
        }

        if (expected is SparkList expectedList)
        {
            if (actual is not SparkList actualList)
            {
                return $"{where} should be a list and is not.";
            }

            if (expectedList.Count != actualList.Count)
            {
                return $"{where} should have {expectedList.Count} items and has {actualList.Count}.";
            }

            for (int index = 0; index < expectedList.Count; index++)
            {
                string? difference = Compare(expectedList[index], actualList[index], $"{path}[{index}]");
                if (difference is not null)
                {
                    return difference;
                }
            }

            return null;
        }

        if (actual is SparkList)
        {
            return $"{where} should be a scalar and is a list.";
        }

        if (IsNumber(expected) && IsNumber(actual))
        {
            double left = Convert.ToDouble(expected, CultureInfo.InvariantCulture);
            double right = Convert.ToDouble(actual, CultureInfo.InvariantCulture);

            return Math.Abs(left - right) <= 1e-9 ? null : $"{where} should be {left} and is {right}.";
        }

        return expected.Equals(actual) ? null : $"{where} should be {Describe(expected)} and is {Describe(actual)}.";
    }

    private static bool IsNumber(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}
