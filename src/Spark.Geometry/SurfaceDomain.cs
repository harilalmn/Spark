using System;

namespace Spark.Geometry;

/// <summary>
/// The domain checks every surface constructor makes, in one place.
/// </summary>
/// <remarks>
/// <b>Internal, and deliberately not a method on <see cref="Interval"/>.</b> "Finite, increasing
/// and of non-zero length" is what a *surface domain* has to be; an interval in general is
/// perfectly entitled to be empty or decreasing, and putting a surface's rule on the value type
/// would make it look like a rule for everybody.
/// </remarks>
internal static class SurfaceDomain
{
    /// <summary>Checks a domain and returns it increasing.</summary>
    /// <param name="domain">The domain as the caller gave it.</param>
    /// <param name="name">The parameter name, for the message.</param>
    /// <returns>The same interval, increasing.</returns>
    /// <exception cref="ArgumentException">It is not finite, or has no length.</exception>
    internal static Interval Nonempty(in Interval domain, string name)
    {
        Interval increasing = domain.MakeIncreasing();

        if (!increasing.IsValid || increasing.Length <= 0.0)
        {
            throw new ArgumentException(
                "A surface domain must be finite and have a non-zero length; a side of zero width "
                + "has no area and no normal, and every operation would have to special-case it.",
                name);
        }

        return increasing;
    }
}
