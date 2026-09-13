using System;

namespace Spark.Geometry;

/// <summary>
/// A circular helix: a point turning about an axis at a constant radius while rising along it at a
/// constant rate. Parameterised over [0, sweep] in radians, measured from the helix's own start.
/// </summary>
/// <remarks>
/// <para>
/// <b>A helix travels at a constant speed, and that is the whole of why it is cheap.</b> Its
/// derivative has length <c>sqrt(radius² + (pitch/2π)²)</c> at every parameter, so
/// <see cref="Curve.LengthAt(double)"/>, <see cref="Curve.ParameterAtLength(double)"/>,
/// <see cref="Curve.PointAtLength(double)"/>, <see cref="Curve.DivideEqually(int)"/> and
/// <see cref="Curve.DivideByLength(double)"/> are all closed form and exactly right rather than
/// iterative. The same expression falls out of unrolling the cylinder the helix lies on: over a
/// sweep of <c>θ</c> the curve becomes the hypotenuse of a right triangle whose legs are the arc
/// <c>radius·θ</c> and the rise <c>pitch·θ/2π</c>.
/// </para>
/// <para>
/// <b>The sweep is always positive, and a negative one flips the plane instead</b>, exactly as
/// <see cref="Arc"/> does and for the same reason: the whole of <see cref="Curve"/> relies on an
/// increasing domain. Turning the other way about an axis is turning the same way about the
/// opposite axis, and the pitch is unchanged by that flip because the rise reverses with it — so
/// <see cref="AxisDirection"/> may report the opposite of the direction the caller passed in.
/// </para>
/// <para>
/// <b><see cref="AxisPoint"/> is the point on the axis beside the helix's start, not the origin the
/// caller gave.</b> Only the axis <i>line</i> is part of the curve: the start point fixes the
/// height at angle zero, so an origin at any other height along the same line names the same helix
/// and there is nothing to preserve. This is <see cref="Arc.StartAngle"/>'s convention — reported in
/// the frame the curve actually holds rather than the one it was handed.
/// </para>
/// <para>
/// <b>A zero pitch is refused, and the refusal points at <see cref="Arc"/>.</b> A helix that does
/// not rise is an arc, and Spark has one. A <i>negative</i> pitch is not refused: that is
/// handedness — a left-handed helix — and unlike a negative sweep it cannot be reached by flipping
/// anything, because flipping the axis reverses the turn and the rise together.
/// </para>
/// </remarks>
public sealed class Helix : Curve
{
    private const double FullTurn = Math.PI * 2.0;

    private readonly Plane _plane;
    private readonly double _radius;
    private readonly double _pitch;
    private readonly double _sweep;

    private Helix(in Plane plane, double radius, double pitch, double sweep)
    {
        _plane = plane;
        _radius = radius;
        _pitch = pitch;
        _sweep = sweep;
    }

    /// <summary>Lifts an instance's state into a new one, so a constructor can call a factory.</summary>
    /// <param name="other">The instance to copy. Already validated by whatever produced it.</param>
    /// <remarks>
    /// <b>`E2-T59` asked for a constructor beside every library factory, and a class constructor
    /// cannot return.</b> This is what lets the public one below read <c>: this(SomeFactory(x))</c>.
    /// The arithmetic stays in the factory, which remains its only copy, so the constructor cannot
    /// drift away from the method it mirrors.
    /// </remarks>
    private Helix(Helix other)
        : this(other._plane, other._radius, other._pitch, other._sweep)
    {
    }

    /// <summary>Creates a helix about an axis.</summary>
    /// <param name="origin">A point on the axis. Any point on it names the same axis.</param>
    /// <param name="direction">The axis direction. Need not be unit length.</param>
    /// <param name="startPoint">
    /// Where the helix begins. Its distance from the axis is the radius, and its height along the
    /// axis is where the rise is measured from.
    /// </param>
    /// <param name="pitch">The rise per full turn. Must be non-zero and finite; may be negative.</param>
    /// <param name="sweepAngle">How far the helix turns in total. Must be non-zero. May be negative.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the axis has no length, or when the start point lies on the axis.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the pitch is zero or not finite, or the sweep is zero or not finite.
    /// </exception>
    /// <remarks>Forwards to <see cref="FromAxis"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public Helix(
        in Point3d origin, in Vector3d direction, in Point3d startPoint, double pitch, Angle sweepAngle)
        : this(FromAxis(origin, direction, startPoint, pitch, sweepAngle))
    {
    }

    /// <inheritdoc/>
    public override Interval Domain => new(0.0, _sweep);

    /// <summary>
    /// Always <see langword="false"/>. A helix rises by <see cref="Pitch"/> every turn and a
    /// non-zero pitch is the one thing this type insists on, so its ends can never meet.
    /// </summary>
    public override bool IsClosed => false;

    /// <summary>
    /// The point on the axis level with <see cref="Curve.StartPoint"/>, which is not in general the
    /// origin the caller passed — see the remarks on <see cref="Helix"/>.
    /// </summary>
    public Point3d AxisPoint => _plane.Origin;

    /// <summary>
    /// The unit direction of the axis, in the sense the helix rises when <see cref="Pitch"/> is
    /// positive. A helix built with a negative sweep reports the opposite of the direction it was
    /// given — see the remarks on <see cref="Helix"/>.
    /// </summary>
    public Vector3d AxisDirection => _plane.Normal;

    /// <summary>The distance from the axis, which is constant along the curve.</summary>
    public double Radius => _radius;

    /// <summary>
    /// The rise along <see cref="AxisDirection"/> per full turn. Negative on a left-handed helix.
    /// </summary>
    public double Pitch => _pitch;

    /// <summary>The angle the helix turns through in total. Always positive.</summary>
    public Angle SweepAngle => Angle.FromRadians(_sweep);

    /// <summary>Creates a helix about an axis.</summary>
    /// <param name="origin">
    /// A point on the axis. Any point on the same line gives the same helix, because
    /// <paramref name="startPoint"/> is what fixes the height the rise is measured from.
    /// </param>
    /// <param name="direction">The axis direction. Need not be unit length.</param>
    /// <param name="startPoint">
    /// Where the helix begins. Its perpendicular distance from the axis is the radius.
    /// </param>
    /// <param name="pitch">
    /// The rise per full turn. Must be non-zero and finite. A negative pitch is a left-handed helix
    /// and is allowed; a zero one is an <see cref="Arc"/> and is refused.
    /// </param>
    /// <param name="sweepAngle">
    /// How far the helix turns in total, with no upper bound — many turns is the ordinary case.
    /// Must be non-zero and finite. A negative sweep is normalised by flipping the axis, as
    /// described on <see cref="Helix"/>.
    /// </param>
    /// <returns>The helix.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="origin"/> or <paramref name="startPoint"/> is not finite, when
    /// <paramref name="direction"/> is zero-length or not finite, or when
    /// <paramref name="startPoint"/> lies on the axis, which would leave the radius zero.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pitch"/> or <paramref name="sweepAngle"/> is zero or not finite.
    /// </exception>
    public static Helix FromAxis(
        in Point3d origin, in Vector3d direction, in Point3d startPoint, double pitch, Angle sweepAngle)
    {
        if (!double.IsFinite(pitch) || pitch == 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pitch),
                pitch,
                "A helix's pitch must be non-zero and finite. A helix that does not rise is a "
                + "circular arc: use Arc.");
        }

        double sweep = sweepAngle.Radians;
        if (!double.IsFinite(sweep) || sweep == 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sweepAngle), sweepAngle, "A helix's sweep must be non-zero and finite.");
        }

        // Plane.FromOriginNormal validates the origin and the direction and picks some x axis; the
        // one it picks is discarded below, because the start point decides where angle zero is.
        Plane axis = Plane.FromOriginNormal(origin, direction);
        Vector3d normal = axis.Normal;

        Vector3d fromAxis = startPoint - origin;
        Vector3d radial = fromAxis - (normal * fromAxis.Dot(normal));
        double radius = radial.Length;

        if (!double.IsFinite(radius) || radius <= 0.0)
        {
            throw new ArgumentException(
                "A helix's start point must lie off its axis, or it has no radius to turn at.",
                nameof(startPoint));
        }

        // The frame's origin is the foot of the start point on the axis rather than the caller's
        // origin: only the axis LINE is in the curve, and the start point fixes the height at angle
        // zero. See the remarks on this type.
        Point3d foot = origin + (normal * fromAxis.Dot(normal));

        // A negative sweep turns the other way about the same axis, which is the same curve turning
        // the same way about the opposite axis. Flipping the y axis flips the normal with it, and
        // the pitch survives unchanged because the rise reverses along with the direction it is
        // measured in.
        Vector3d x = radial / radius;
        Vector3d y = normal.Cross(x);

        return sweep > 0.0
            ? new Helix(Plane.FromOriginXAxisYAxis(foot, x, y), radius, pitch, sweep)
            : new Helix(Plane.FromOriginXAxisYAxis(foot, x, -y), radius, pitch, -sweep);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Exact rather than integrated: the speed is <c>sqrt(radius² + (pitch/2π)²)</c> at every
    /// parameter, so the length to a parameter is that speed times the parameter.
    /// </remarks>
    public override double LengthAt(double parameter) => CheckParameter(parameter) * Speed;

    /// <inheritdoc/>
    /// <remarks>Exact rather than iterative, for the reason given on <see cref="LengthAt"/>.</remarks>
    public override double ParameterAtLength(double distance)
    {
        if (!double.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance), distance, "A distance along a curve must be finite.");
        }

        return Math.Clamp(distance / Speed, 0.0, _sweep);
    }

    /// <inheritdoc/>
    public override Curve Reversed() => Reframe(_sweep, 0.0);

    /// <summary>Returns the part of the helix between two parameters.</summary>
    /// <param name="domain">
    /// The sub-domain to keep, in radians from the helix's start. Must lie within
    /// <see cref="Domain"/> and have non-zero length; a decreasing interval gives the piece
    /// traversed backwards.
    /// </param>
    /// <returns>
    /// A shorter <see cref="Helix"/> — unlike <see cref="Circle.Trimmed"/>, which has to change
    /// type, a piece of a helix is still a helix.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="domain"/> is not finite, has zero length, or leaves
    /// <see cref="Domain"/>.
    /// </exception>
    public override Curve Trimmed(in Interval domain)
    {
        if (!domain.IsValid || domain.Length == 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(domain), domain, "A trim domain must be finite and of non-zero length.");
        }

        Interval increasing = domain.MakeIncreasing();
        double slack = Math.Max(_sweep, 1.0) * 1e-12;
        if (increasing.Min < -slack || increasing.Max > _sweep + slack)
        {
            throw new ArgumentOutOfRangeException(
                nameof(domain), domain, $"A trim domain must lie within the helix's domain, [0, {_sweep}].");
        }

        return Reframe(
            Math.Clamp(domain.Min, 0.0, _sweep),
            Math.Clamp(domain.Max, 0.0, _sweep));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>The pitch is scaled by the <i>axial</i> factor and the radius by the radial one, and they
    /// are not the same number.</b> A transform that stretches along the axis leaves a helix a
    /// helix and changes only its rise; one that stretches unevenly <i>across</i> the axis does not,
    /// which is why the in-plane test is <see cref="Circle"/>'s. Taking the radial factor for both —
    /// the easy mistake, because a uniform scale makes them equal and hides it — gives a helix of
    /// the right shape and the wrong height.
    /// </para>
    /// <para>
    /// A mirror makes the axial factor negative, and the result is a helix of the opposite hand.
    /// That is correct rather than a case to refuse, and it is why <see cref="Pitch"/> is allowed to
    /// be negative.
    /// </para>
    /// </remarks>
    public override Curve TransformedBy(in Transform transform)
    {
        Plane plane = CircularArcs.TransformFrame(transform, _plane, out double radial);
        Vector3d axis = transform.OfVector(_plane.Normal);
        double axial = axis.Dot(plane.Normal);

        // The axial factor is the component along the mapped normal, so a transform that shears the
        // axis INTO the plane would silently lose the rest of it. That result is not a helix, and
        // the length test is what says so.
        if (Math.Abs(Math.Abs(axial) - axis.Length) > Math.Max(axis.Length, 1.0) * 1e-9)
        {
            throw new ArgumentException(
                "The transform shears this helix's axis out of perpendicular with its turn, which "
                + "would give a curve this type cannot represent.",
                nameof(transform));
        }

        return new Helix(plane, _radius * radial, _pitch * axial, _sweep);
    }

    /// <summary>A readable description of the helix, for diagnostics.</summary>
    /// <returns>The axis point, the radius, the pitch and the number of turns.</returns>
    public override string ToString() =>
        $"Helix(axis {AxisPoint} {AxisDirection}, radius {_radius}, pitch {_pitch}, turns {_sweep / FullTurn})";

    /// <inheritdoc/>
    /// <remarks>
    /// Four spans a turn, as <see cref="Circle"/> uses for one — the seed has to scale with the
    /// sweep or a twenty-turn helix starts its adaptive subdivision from four chords that each cut
    /// straight through the axis, and the deviation test at the midpoint of such a chord cannot see
    /// how wrong it is.
    /// </remarks>
    protected override int TessellationSeedSpans =>
        (int)Math.Clamp(Math.Ceiling(_sweep / FullTurn * 4.0), 4.0, 8192.0);

    /// <inheritdoc/>
    protected override double ComputeLength() => Speed * _sweep;

    /// <inheritdoc/>
    /// <remarks>
    /// The turn is bounded exactly by <see cref="CircularArcs.Bounds"/> and the rise is then added
    /// as an interval along the axis. That is exact whenever the axis is parallel to a world axis —
    /// the ordinary case, and the one a viewport culls — and a little generous otherwise, which is
    /// the safe direction: a box slightly too large costs nothing and one slightly too small is
    /// geometry vanishing at the edge of the screen.
    /// </remarks>
    protected override BoundingBox ComputeBoundingBox()
    {
        BoundingBox turn = CircularArcs.Bounds(_plane, _radius, _radius, 0.0, _sweep);
        Vector3d rise = _plane.Normal * (_pitch * _sweep / FullTurn);
        return turn.Union(turn.Min + rise).Union(turn.Max + rise);
    }

    /// <inheritdoc/>
    protected override Point3d Evaluate(double parameter) =>
        CircularArcs.PointAt(_plane, _radius, _radius, parameter) + RiseAt(parameter);

    /// <inheritdoc/>
    protected override Vector3d EvaluateDerivative(double parameter) =>
        CircularArcs.DerivativeAt(_plane, _radius, _radius, parameter)
        + (_plane.Normal * (_pitch / FullTurn));

    /// <inheritdoc/>
    /// <remarks>
    /// The rise is linear in the parameter, so it contributes nothing here and the second
    /// derivative is the turn's alone — which is why it points at the axis and is never zero.
    /// </remarks>
    protected override Vector3d EvaluateSecondDerivative(double parameter) =>
        CircularArcs.SecondDerivativeAt(_plane, _radius, _radius, parameter);

    /// <summary>The speed, which is the same at every parameter and is the reason this type is cheap.</summary>
    private double Speed => Math.Sqrt((_radius * _radius) + (_pitch / FullTurn * (_pitch / FullTurn)));

    /// <summary>The displacement along the axis at a parameter.</summary>
    /// <param name="parameter">The parameter, in radians from the start.</param>
    /// <returns>The rise.</returns>
    private Vector3d RiseAt(double parameter) => _plane.Normal * (_pitch * parameter / FullTurn);

    /// <summary>
    /// Returns the piece of the helix from one parameter to another, as a helix in its own frame.
    /// </summary>
    /// <remarks>
    /// <b><see cref="Reversed"/> and <see cref="Trimmed"/> are one derivation, so they are one
    /// method.</b> Reversing is the trim from the end back to the start, and writing the algebra
    /// twice is how the two come to disagree. The pitch survives both directions: going backwards
    /// flips the axis, and the rise measured along a flipped axis flips with it.
    /// </remarks>
    /// <param name="from">The parameter the piece starts at.</param>
    /// <param name="to">The parameter it ends at. May be less than <paramref name="from"/>.</param>
    /// <returns>The piece.</returns>
    private Helix Reframe(double from, double to)
    {
        double cos = Math.Cos(from);
        double sin = Math.Sin(from);
        Vector3d x = (_plane.XAxis * cos) + (_plane.YAxis * sin);
        Vector3d tangential = (_plane.XAxis * -sin) + (_plane.YAxis * cos);
        Point3d origin = _plane.Origin + RiseAt(from);

        return to > from
            ? new Helix(Plane.FromOriginXAxisYAxis(origin, x, tangential), _radius, _pitch, to - from)
            : new Helix(Plane.FromOriginXAxisYAxis(origin, x, -tangential), _radius, _pitch, from - to);
    }
}
