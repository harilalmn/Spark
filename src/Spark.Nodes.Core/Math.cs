using System;
using Spark.Api;

namespace Spark.Nodes.Core;

/// <summary>
/// Arithmetic and trigonometry nodes.
/// </summary>
/// <remarks>
/// This type shadows <see cref="System.Math"/> inside this namespace on purpose: the importer names
/// a node <c>Type.Member</c>, so the type has to be called <c>Math</c> for the node to be called
/// <c>Math.Sin</c>. Members here therefore call <c>System.Math</c> by its full name.
/// </remarks>
[SparkNode(Category = NodeCategories.Math)]
public static class Math
{
    /// <summary>Adds two numbers.</summary>
    /// <param name="a">The first number.</param>
    /// <param name="b">The second number.</param>
    /// <returns>The sum.</returns>
    [return: NodePort("result")]
    public static double Add(double a = 0, double b = 0) => a + b;

    /// <summary>Subtracts one number from another.</summary>
    /// <param name="a">The number to subtract from.</param>
    /// <param name="b">The number to subtract.</param>
    /// <returns>The difference.</returns>
    [return: NodePort("result")]
    public static double Subtract(double a = 0, double b = 0) => a - b;

    /// <summary>Multiplies two numbers.</summary>
    /// <param name="a">The first number.</param>
    /// <param name="b">The second number.</param>
    /// <returns>The product.</returns>
    [return: NodePort("result")]
    public static double Multiply(double a = 1, double b = 1) => a * b;

    /// <summary>Divides one number by another.</summary>
    /// <remarks>
    /// A zero divisor throws rather than returning an infinity. The infinity would flow downstream
    /// and turn into geometry nobody can see at a coordinate nobody can find; the exception stops
    /// at this node, which is then the only node on the canvas wearing an error ring.
    /// </remarks>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <returns>The quotient.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="b"/> is zero.</exception>
    [return: NodePort("result")]
    public static double Divide(double a = 0, double b = 1)
    {
        if (b == 0)
        {
            throw new DivideByZeroException("Divide was given a divisor of zero.");
        }

        return a / b;
    }

    /// <summary>The sine of an angle in degrees.</summary>
    /// <param name="degrees">The angle, in degrees.</param>
    /// <returns>The sine.</returns>
    [return: NodePort("result")]
    public static double Sin(double degrees = 0) =>
        System.Math.Sin(degrees * System.Math.PI / 180.0);

    /// <summary>The cosine of an angle in degrees.</summary>
    /// <param name="degrees">The angle, in degrees.</param>
    /// <returns>The cosine.</returns>
    [return: NodePort("result")]
    public static double Cos(double degrees = 0) =>
        System.Math.Cos(degrees * System.Math.PI / 180.0);

    /// <summary>The constant π.</summary>
    /// <returns>3.14159265358979…</returns>
    [return: NodePort("result")]
    public static double Pi() => System.Math.PI;
}
