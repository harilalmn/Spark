using System;

namespace Spark.Geometry;

/// <summary>
/// The sheet swept by a straight line joining two curves: <c>u</c> runs along them, <c>v</c>
/// across.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two curves are matched by <i>fraction of parameter domain</i>, not by arc length</b>, and
/// that is a decision worth naming. Matching by arc length would make the ruling lines feel more
/// even on curves of very different speeds, and it would also make the surface's parameterisation
/// depend on a numerically-integrated quantity — so an exactly-representable loft between two lines
/// would stop being exact, and the surface would change whenever the arc-length table did. Every
/// kernel matches by parameter for this reason; a user who wants even rulings reparameterises the
/// curves first, which is a thing they can see and control.
/// </para>
/// <para>
/// <b>Both <c>u</c> and <c>v</c> run over [0, 1]</b>, because there is no honest alternative: the
/// two curves have their own domains and neither is more the surface's than the other.
/// </para>
/// <para>
/// <b>A ruled surface between two curves that touch is degenerate where they touch.</b> That is how
/// a cone is built — rule between a circle and a degenerate point-curve — so it is allowed, and the
/// normal is undefined on that edge exactly as it is at a cone's apex.
/// </para>
/// </remarks>
public sealed class RuledSurface : Surface
{
    private readonly Curve _first;
    private readonly Curve _second;

    /// <summary>Creates a ruled surface between two curves.</summary>
    /// <param name="first">The curve at <c>v = 0</c>.</param>
    /// <param name="second">The curve at <c>v = 1</c>.</param>
    /// <exception cref="ArgumentNullException">Either curve is null.</exception>
    public RuledSurface(Curve first, Curve second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        _first = first;
        _second = second;
    }

    /// <summary>The curve at <c>v = 0</c>.</summary>
    public Curve First => _first;

    /// <summary>The curve at <c>v = 1</c>.</summary>
    public Curve Second => _second;

    /// <inheritdoc/>
    public override Interval DomainU => Interval.Unit;

    /// <inheritdoc/>
    public override Interval DomainV => Interval.Unit;

    /// <inheritdoc/>
    /// <remarks>Closed only when both curves are: one open edge leaves the sheet open.</remarks>
    public override bool IsClosedU => _first.IsClosed && _second.IsClosed;

    /// <inheritdoc/>
    public override bool IsClosedV => false;

    /// <inheritdoc/>
    public override Surface TransformedBy(in Transform transform) =>
        new RuledSurface(_first.TransformedBy(transform), _second.TransformedBy(transform));

    /// <inheritdoc/>
    protected override Point3d Evaluate(double u, double v) =>
        Point3d.Lerp(_first.PointAt(_first.Domain.Denormalise(u)), _second.PointAt(_second.Domain.Denormalise(u)), v);

    /// <inheritdoc/>
    /// <remarks>
    /// The chain rule is where the domain lengths come back: <c>u</c> is a fraction of each curve's
    /// domain, so each curve's derivative is multiplied by its own domain length. Dropping that
    /// factor is the mistake this comment exists to prevent — it gives a surface whose area and
    /// normals are right only when both curves happen to be parameterised over [0, 1].
    /// </remarks>
    protected override void EvaluateDerivatives(
        double u, double v, out Vector3d derivativeU, out Vector3d derivativeV)
    {
        Point3d a = _first.PointAt(_first.Domain.Denormalise(u));
        Point3d b = _second.PointAt(_second.Domain.Denormalise(u));

        Vector3d da = _first.DerivativeAt(_first.Domain.Denormalise(u)) * _first.Domain.Length;
        Vector3d db = _second.DerivativeAt(_second.Domain.Denormalise(u)) * _second.Domain.Length;

        derivativeU = da + ((db - da) * v);
        derivativeV = b - a;
    }

    /// <inheritdoc/>
    protected override void EvaluateSecondDerivatives(
        double u, double v, out Vector3d secondU, out Vector3d mixed, out Vector3d secondV)
    {
        double lengthA = _first.Domain.Length;
        double lengthB = _second.Domain.Length;

        Vector3d dda = _first.SecondDerivativeAt(_first.Domain.Denormalise(u)) * lengthA * lengthA;
        Vector3d ddb = _second.SecondDerivativeAt(_second.Domain.Denormalise(u)) * lengthB * lengthB;

        Vector3d da = _first.DerivativeAt(_first.Domain.Denormalise(u)) * lengthA;
        Vector3d db = _second.DerivativeAt(_second.Domain.Denormalise(u)) * lengthB;

        secondU = dda + ((ddb - dda) * v);
        mixed = db - da;

        // Zero: the rule is a straight line in v, whatever the curves do.
        secondV = Vector3d.Zero;
    }
}
