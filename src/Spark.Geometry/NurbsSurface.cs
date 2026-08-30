using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A non-uniform rational B-spline surface: a control net, two knot vectors, and optional weights.
/// </summary>
/// <remarks>
/// <para>
/// <b>A tensor product, which is why so little of this is new.</b> The basis functions in each
/// direction are exactly <see cref="NurbsCurve"/>'s, evaluated by <see cref="KnotVector"/>'s
/// recurrence — the surface's basis is their product, and its derivatives are the products of their
/// derivatives. That is the reason <c>BasisDerivatives</c> was moved onto <see cref="KnotVector"/>
/// when this type arrived: two callers of one implementation is right, and two copies of de Boor's
/// A2.3 would be a place for them to drift.
/// </para>
/// <para>
/// <b>Weights are held in homogeneous form, once, at construction.</b> Every evaluation is then a
/// weighted sum in four dimensions followed by one divide, rather than a rational sum that has to
/// carry the weights through every step. It is Piegl and Tiller's A4.3 and it is also the shape the
/// derivative quotient rule needs.
/// </para>
/// <para>
/// <b>The control net is <c>[u, v]</c>, u first.</b> Stated because it is the single most common
/// thing to get backwards, and a transposed net produces a surface that evaluates without
/// complaint and is the wrong shape.
/// </para>
/// </remarks>
public sealed class NurbsSurface : Surface
{
    private readonly double[,,] _homogeneous;
    private readonly int _countU;
    private readonly int _countV;
    private readonly bool _rational;

    /// <summary>Creates a non-rational NURBS surface.</summary>
    /// <param name="knotsU">The knot vector in <c>u</c>.</param>
    /// <param name="knotsV">The knot vector in <c>v</c>.</param>
    /// <param name="controlPoints">The control net, indexed <c>[u, v]</c>.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// The net's dimensions do not match what the knot vectors imply.
    /// </exception>
    public NurbsSurface(KnotVector knotsU, KnotVector knotsV, Point3d[,] controlPoints)
        : this(knotsU, knotsV, controlPoints, weights: null)
    {
    }

    /// <summary>Creates a NURBS surface, rational when weights are given.</summary>
    /// <param name="knotsU">The knot vector in <c>u</c>.</param>
    /// <param name="knotsV">The knot vector in <c>v</c>.</param>
    /// <param name="controlPoints">The control net, indexed <c>[u, v]</c>.</param>
    /// <param name="weights">
    /// One positive weight per control point, or null for a non-rational surface.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// The dimensions disagree, or a weight is not finite and positive.
    /// </exception>
    public NurbsSurface(KnotVector knotsU, KnotVector knotsV, Point3d[,] controlPoints, double[,]? weights)
    {
        ArgumentNullException.ThrowIfNull(knotsU);
        ArgumentNullException.ThrowIfNull(knotsV);
        ArgumentNullException.ThrowIfNull(controlPoints);

        KnotsU = knotsU;
        KnotsV = knotsV;
        _countU = controlPoints.GetLength(0);
        _countV = controlPoints.GetLength(1);

        if (_countU != knotsU.ControlPointCount || _countV != knotsV.ControlPointCount)
        {
            string dimensions = string.Create(
                CultureInfo.InvariantCulture,
                $"The control net is {_countU}x{_countV}, and the knot vectors call for {knotsU.ControlPointCount}x{knotsV.ControlPointCount}.");

            throw new ArgumentException(
                dimensions
                + " A knot vector's length fixes the control point count exactly: it is"
                + " degree + points + 1.",
                nameof(controlPoints));
        }

        if (weights is not null
            && (weights.GetLength(0) != _countU || weights.GetLength(1) != _countV))
        {
            throw new ArgumentException(
                "There must be exactly one weight per control point.", nameof(weights));
        }

        _rational = weights is not null;
        _homogeneous = new double[_countU, _countV, 4];

        for (int i = 0; i < _countU; i++)
        {
            for (int j = 0; j < _countV; j++)
            {
                double w = weights?[i, j] ?? 1.0;

                if (!double.IsFinite(w) || w <= 0.0)
                {
                    throw new ArgumentException(
                        "Every weight must be finite and positive; a zero or negative weight makes "
                        + "the surface undefined where the denominator vanishes.",
                        nameof(weights));
                }

                Point3d point = controlPoints[i, j];

                _homogeneous[i, j, 0] = point.X * w;
                _homogeneous[i, j, 1] = point.Y * w;
                _homogeneous[i, j, 2] = point.Z * w;
                _homogeneous[i, j, 3] = w;
            }
        }
    }

    /// <summary>The knot vector in <c>u</c>.</summary>
    public KnotVector KnotsU { get; }

    /// <summary>The knot vector in <c>v</c>.</summary>
    public KnotVector KnotsV { get; }

    /// <summary>The degree in <c>u</c>.</summary>
    public int DegreeU => KnotsU.Degree;

    /// <summary>The degree in <c>v</c>.</summary>
    public int DegreeV => KnotsV.Degree;

    /// <summary>How many control points there are along <c>u</c>.</summary>
    public int ControlPointCountU => _countU;

    /// <summary>How many control points there are along <c>v</c>.</summary>
    public int ControlPointCountV => _countV;

    /// <summary>Whether any weight differs from one.</summary>
    /// <remarks>
    /// <b>Recorded rather than inferred.</b> A surface constructed with all-equal weights is still
    /// rational in the sense that matters — a caller who asked for weights gets them back — and a
    /// conversion that quietly dropped them would round-trip to a different object.
    /// </remarks>
    public bool IsRational => _rational;

    /// <inheritdoc/>
    public override Interval DomainU => KnotsU.Domain;

    /// <inheritdoc/>
    public override Interval DomainV => KnotsV.Domain;

    /// <inheritdoc/>
    /// <remarks>
    /// Closed when the first and last rows of control points coincide, which is how a NURBS surface
    /// says it wraps. Comparing the *surface* at the two edges would be equivalent and far more
    /// expensive.
    /// </remarks>
    public override bool IsClosedU
    {
        get
        {
            for (int j = 0; j < _countV; j++)
            {
                if (!ControlPoint(0, j).EqualsWithin(ControlPoint(_countU - 1, j)))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public override bool IsClosedV
    {
        get
        {
            for (int i = 0; i < _countU; i++)
            {
                if (!ControlPoint(i, 0).EqualsWithin(ControlPoint(i, _countV - 1)))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>One control point of the net.</summary>
    /// <param name="i">Its index along <c>u</c>.</param>
    /// <param name="j">Its index along <c>v</c>.</param>
    /// <returns>The point, with any weight divided back out.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An index is outside the net.</exception>
    public Point3d ControlPoint(int i, int j)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(i);
        ArgumentOutOfRangeException.ThrowIfNegative(j);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(i, _countU);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(j, _countV);

        double w = _homogeneous[i, j, 3];

        return new Point3d(
            _homogeneous[i, j, 0] / w, _homogeneous[i, j, 1] / w, _homogeneous[i, j, 2] / w);
    }

    /// <summary>The weight of one control point.</summary>
    /// <param name="i">Its index along <c>u</c>.</param>
    /// <param name="j">Its index along <c>v</c>.</param>
    /// <returns>The weight, one on a non-rational surface.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An index is outside the net.</exception>
    public double Weight(int i, int j)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(i);
        ArgumentOutOfRangeException.ThrowIfNegative(j);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(i, _countU);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(j, _countV);

        return _homogeneous[i, j, 3];
    }

    /// <summary>A copy of the control net.</summary>
    /// <returns>The points, indexed <c>[u, v]</c>.</returns>
    public Point3d[,] ControlPoints()
    {
        Point3d[,] points = new Point3d[_countU, _countV];

        for (int i = 0; i < _countU; i++)
        {
            for (int j = 0; j < _countV; j++)
            {
                points[i, j] = ControlPoint(i, j);
            }
        }

        return points;
    }

    /// <summary>A copy of the weights.</summary>
    /// <returns>One weight per control point, indexed <c>[u, v]</c>.</returns>
    public double[,] Weights()
    {
        double[,] weights = new double[_countU, _countV];

        for (int i = 0; i < _countU; i++)
        {
            for (int j = 0; j < _countV; j++)
            {
                weights[i, j] = _homogeneous[i, j, 3];
            }
        }

        return weights;
    }

    /// <summary>
    /// The box around the control net, which is guaranteed to contain the surface.
    /// </summary>
    /// <remarks>
    /// <b>The convex-hull property, and it is why this overrides the sampled default.</b> Every
    /// point of a B-spline surface lies inside the convex hull of its control net, so the net's box
    /// contains the surface exactly — no sampling, no padding, and no possibility of a bulge
    /// between samples escaping it. It is not the *tightest* box; it is a correct one computed in
    /// the time it takes to read the net once.
    /// </remarks>
    public override BoundingBox BoundingBox
    {
        get
        {
            BoundingBox box = BoundingBox.Empty;

            for (int i = 0; i < _countU; i++)
            {
                for (int j = 0; j < _countV; j++)
                {
                    box = box.Union(ControlPoint(i, j));
                }
            }

            return box;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>A NURBS surface survives every affine transform</b>, which is the property that makes it
    /// the universal representation: transforming the control net transforms the surface exactly,
    /// because the basis functions do not depend on where the points are. It is also why the
    /// analytic types convert to this one rather than the other way round.
    /// </remarks>
    public override Surface TransformedBy(in Transform transform)
    {
        Point3d[,] moved = new Point3d[_countU, _countV];

        for (int i = 0; i < _countU; i++)
        {
            for (int j = 0; j < _countV; j++)
            {
                moved[i, j] = transform.OfPoint(ControlPoint(i, j));
            }
        }

        return new NurbsSurface(KnotsU, KnotsV, moved, _rational ? Weights() : null);
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"NurbsSurface(degree {DegreeU}×{DegreeV}, {_countU}×{_countV} points{(_rational ? ", rational" : string.Empty)})");

    /// <inheritdoc/>
    protected override Point3d Evaluate(double u, double v)
    {
        double[] homogeneous = Homogeneous(u, v, 0, 0);

        return new Point3d(
            homogeneous[0] / homogeneous[3],
            homogeneous[1] / homogeneous[3],
            homogeneous[2] / homogeneous[3]);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The quotient rule in each direction independently: <c>S = A/w</c>, so
    /// <c>S_u = (A_u − w_u·S) / w</c>. On a non-rational surface the weight derivative is zero and
    /// this reduces to the B-spline derivative, which is what makes one implementation serve both.
    /// </remarks>
    protected override void EvaluateDerivatives(
        double u, double v, out Vector3d derivativeU, out Vector3d derivativeV)
    {
        double[] a = Homogeneous(u, v, 0, 0);
        double[] au = Homogeneous(u, v, 1, 0);
        double[] av = Homogeneous(u, v, 0, 1);

        double w = a[3];
        Point3d point = new(a[0] / w, a[1] / w, a[2] / w);

        derivativeU = Quotient(au, point, w);
        derivativeV = Quotient(av, point, w);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Differentiating the quotient rule once more, written out rather than folded — the folded
    /// form is where a sign error hides and nothing downstream shows it except a wrong curvature,
    /// which is the same note <see cref="NurbsCurve"/> carries for the same reason.
    /// </remarks>
    protected override void EvaluateSecondDerivatives(
        double u, double v, out Vector3d secondU, out Vector3d mixed, out Vector3d secondV)
    {
        double[] a = Homogeneous(u, v, 0, 0);
        double[] au = Homogeneous(u, v, 1, 0);
        double[] av = Homogeneous(u, v, 0, 1);
        double[] auu = Homogeneous(u, v, 2, 0);
        double[] auv = Homogeneous(u, v, 1, 1);
        double[] avv = Homogeneous(u, v, 0, 2);

        double w = a[3];
        Point3d point = new(a[0] / w, a[1] / w, a[2] / w);

        Vector3d su = Quotient(au, point, w);
        Vector3d sv = Quotient(av, point, w);

        secondU = Second(auu, au, au, su, su, point, w);
        secondV = Second(avv, av, av, sv, sv, point, w);
        mixed = Second(auv, au, av, sv, su, point, w);
    }

    /// <summary>The first-order quotient rule, in three components.</summary>
    private static Vector3d Quotient(double[] derivative, in Point3d point, double w) =>
        new(
            (derivative[0] - (derivative[3] * point.X)) / w,
            (derivative[1] - (derivative[3] * point.Y)) / w,
            (derivative[2] - (derivative[3] * point.Z)) / w);

    /// <summary>
    /// The second-order quotient rule, general enough for the mixed term as well as the pure ones.
    /// </summary>
    /// <remarks>
    /// For <c>S = A/w</c>, differentiating twice in directions <c>s</c> and <c>t</c> gives
    /// <c>S_st = (A_st − w_s·S_t − w_t·S_s − w_st·S) / w</c>. Setting <c>s = t</c> recovers the
    /// pure second derivative, with the two middle terms becoming <c>2 w_s S_s</c> — which is why
    /// there is one method here rather than two.
    /// </remarks>
    private static Vector3d Second(
        double[] second,
        double[] firstS,
        double[] firstT,
        in Vector3d derivativeT,
        in Vector3d derivativeS,
        in Point3d point,
        double w) =>
        new(
            (second[0] - (firstS[3] * derivativeT.X) - (firstT[3] * derivativeS.X) - (second[3] * point.X)) / w,
            (second[1] - (firstS[3] * derivativeT.Y) - (firstT[3] * derivativeS.Y) - (second[3] * point.Y)) / w,
            (second[2] - (firstS[3] * derivativeT.Z) - (firstT[3] * derivativeS.Z) - (second[3] * point.Z)) / w);

    /// <summary>
    /// The homogeneous point and one mixed partial derivative of it, by the tensor product of the
    /// two bases.
    /// </summary>
    /// <param name="u">A parameter in <see cref="DomainU"/>.</param>
    /// <param name="v">A parameter in <see cref="DomainV"/>.</param>
    /// <param name="orderU">How many times to differentiate in <c>u</c>.</param>
    /// <param name="orderV">How many times to differentiate in <c>v</c>.</param>
    /// <returns>Four homogeneous components.</returns>
    /// <remarks>
    /// A derivative above a direction's degree is identically zero, and asking the basis recurrence
    /// for it divides by zero — so it is answered with zeros rather than computed, exactly as the
    /// curve does.
    /// </remarks>
    private double[] Homogeneous(double u, double v, int orderU, int orderV)
    {
        double[] result = new double[4];

        if (orderU > DegreeU || orderV > DegreeV)
        {
            return result;
        }

        double clampedU = Math.Clamp(u, DomainU.Min, DomainU.Max);
        double clampedV = Math.Clamp(v, DomainV.Min, DomainV.Max);

        int spanU = KnotsU.FindSpan(clampedU);
        int spanV = KnotsV.FindSpan(clampedV);

        double[][] basisU = KnotsU.BasisDerivatives(spanU, clampedU, orderU);
        double[][] basisV = KnotsV.BasisDerivatives(spanV, clampedV, orderV);

        for (int i = 0; i <= DegreeU; i++)
        {
            int indexU = spanU - DegreeU + i;
            double weightU = basisU[orderU][i];

            if (weightU == 0.0)
            {
                continue;
            }

            for (int j = 0; j <= DegreeV; j++)
            {
                int indexV = spanV - DegreeV + j;
                double product = weightU * basisV[orderV][j];

                for (int component = 0; component < 4; component++)
                {
                    result[component] += product * _homogeneous[indexU, indexV, component];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// A flat rectangular surface through four corners, which is the smallest useful NURBS surface.
    /// </summary>
    /// <param name="corners">
    /// The four corners in net order: <c>(0,0)</c>, <c>(0,1)</c>, <c>(1,0)</c>, <c>(1,1)</c>.
    /// </param>
    /// <returns>A bilinear surface.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="corners"/> is null.</exception>
    /// <exception cref="ArgumentException">There are not exactly four corners.</exception>
    public static NurbsSurface ByCorners(IReadOnlyList<Point3d> corners)
    {
        ArgumentNullException.ThrowIfNull(corners);

        if (corners.Count != 4)
        {
            throw new ArgumentException("A bilinear surface has four corners.", nameof(corners));
        }

        Point3d[,] net = new Point3d[2, 2];
        net[0, 0] = corners[0];
        net[0, 1] = corners[1];
        net[1, 0] = corners[2];
        net[1, 1] = corners[3];

        return new NurbsSurface(KnotVector.CreateClamped(1, 2), KnotVector.CreateClamped(1, 2), net);
    }
}
