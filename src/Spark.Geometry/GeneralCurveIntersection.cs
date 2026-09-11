using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// Curve/curve intersection for any two curves: bracket by sampling, then refine (<c>E2-T11</c>,
/// step B).
/// </summary>
/// <remarks>
/// <para>
/// <b>Bracket, then refine.</b> Each curve is sampled over its own domain into chords. The second
/// curve's chord boxes go into a <see cref="BoundingVolumeHierarchy"/> (<c>E2-T15</c>), each grown well
/// past how far a curve can stray from its chord, and each chord of the first curve asks it which
/// chords are near. Every near pair starts a Gauss-Newton refinement of <c>a(s) - b(t)</c> from the
/// chords' closest approach, kept only if it lands inside both domains within the linear tolerance.
/// </para>
/// <para>
/// <b>Points, touches and overlaps.</b> Candidates within the tolerance of one another are one point.
/// A run of candidates joined by curve that stays on the other curve is either a touch - when it
/// spans less than one sample spacing, which is what refinement near a tangency produces - reported as
/// its best point, or an overlap, whose ends are then found by bisection to the tolerance. An overlap
/// shorter than one sample spacing is therefore reported as a point; that is the price of sampling,
/// and it is stated rather than hidden.
/// </para>
/// <para>
/// <b>Checked against the closed forms.</b> Every pair <see cref="AnalyticCurveIntersection"/> answers
/// is also answered here, and the tests require the two to agree.
/// </para>
/// </remarks>
internal static class GeneralCurveIntersection
{
    /// <summary>Chords per curve. Enough that a chord's box, grown by a tenth of its length, holds its arc.</summary>
    internal const int Samples = 128;

    private const int MaximumIterations = 60;

    /// <summary>Intersects any two curves.</summary>
    /// <param name="a">The first curve.</param>
    /// <param name="b">The second curve.</param>
    /// <param name="tolerance">How close counts as meeting.</param>
    /// <returns>What they have in common.</returns>
    internal static CurveIntersections Intersect(Curve a, Curve b, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        double tol = tolerance.Linear;
        double[] onA = Parameters(a);
        double[] onB = Parameters(b);
        Point3d[] alongA = Points(a, onA);
        Point3d[] alongB = Points(b, onB);

        BoundingBox[] chords = new BoundingBox[onB.Length - 1];

        for (int j = 0; j < chords.Length; j++)
        {
            chords[j] = Chord(alongB[j], alongB[j + 1], tol);
        }

        BoundingVolumeHierarchy hierarchy = BoundingVolumeHierarchy.Build(chords.AsSpan());
        List<int> near = [];
        List<CurveIntersectionPoint> found = [];

        for (int i = 0; i + 1 < onA.Length; i++)
        {
            near.Clear();
            hierarchy.Query(Chord(alongA[i], alongA[i + 1], tol), near);

            foreach (int j in near)
            {
                (double u, double v) = ClosestOnChords(alongA[i], alongA[i + 1], alongB[j], alongB[j + 1]);
                double s = onA[i] + (u * (onA[i + 1] - onA[i]));
                double t = onB[j] + (v * (onB[j + 1] - onB[j]));

                if (Refine(a, b, ref s, ref t, tol) is Point3d point)
                {
                    found.Add(new CurveIntersectionPoint(point, s, t));
                }
            }
        }

        return Assemble(a, b, found, tol);
    }

    private static double[] Parameters(Curve curve)
    {
        Interval domain = curve.Domain;
        double[] parameters = new double[Samples + 1];

        for (int i = 0; i < Samples; i++)
        {
            parameters[i] = domain.Denormalise(i / (double)Samples);
        }

        parameters[Samples] = domain.Max;

        return parameters;
    }

    private static Point3d[] Points(Curve curve, double[] parameters)
    {
        Point3d[] points = new Point3d[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            points[i] = curve.PointAt(parameters[i]);
        }

        return points;
    }

    /// <summary>A chord's box, grown by a tenth of its length and the tolerance.</summary>
    private static BoundingBox Chord(in Point3d start, in Point3d end, double tol) =>
        new BoundingBox(start, end).Inflated((0.1 * start.DistanceTo(end)) + tol);

    /// <summary>Where two chords come closest, as fractions along each.</summary>
    private static (double U, double V) ClosestOnChords(in Point3d a0, in Point3d a1, in Point3d b0, in Point3d b1)
    {
        Vector3d da = a1 - a0;
        Vector3d db = b1 - b0;
        Vector3d r = a0 - b0;
        double aa = da.Dot(da);
        double bb = db.Dot(db);

        if (aa <= 0.0 || bb <= 0.0)
        {
            return (0.0, 0.0);
        }

        double ab = da.Dot(db);
        double denominator = (aa * bb) - (ab * ab);

        double u = denominator > 1e-12 * aa * bb
            ? Math.Clamp(((ab * db.Dot(r)) - (bb * da.Dot(r))) / denominator, 0.0, 1.0)
            : 0.5;

        double v = Math.Clamp(((a0 + (da * u)) - b0).Dot(db) / bb, 0.0, 1.0);

        return (u, v);
    }

    /// <summary>
    /// Gauss-Newton on <c>a(s) - b(t)</c>, clamped to both domains. Where the two tangents are
    /// parallel the step is taken along the first curve alone and the second follows by closest point,
    /// which is what carries a tangency or an overlap to a point on both.
    /// </summary>
    private static Point3d? Refine(Curve a, Curve b, ref double s, ref double t, double tol)
    {
        Interval domainA = a.Domain;
        Interval domainB = b.Domain;

        for (int iteration = 0; iteration < MaximumIterations; iteration++)
        {
            Point3d pa = a.PointAt(s);
            Point3d pb = b.PointAt(t);
            Vector3d r = pa - pb;

            if (r.Length <= tol * 1e-3)
            {
                break;
            }

            Vector3d ta = a.DerivativeAt(s);
            Vector3d tb = b.DerivativeAt(t);
            double aa = ta.Dot(ta);
            double bb = tb.Dot(tb);

            if (aa <= 1e-300 || bb <= 1e-300)
            {
                break;
            }

            double ab = ta.Dot(tb);
            double ar = ta.Dot(r);
            double br = tb.Dot(r);
            double determinant = (aa * bb) - (ab * ab);

            double nextS;
            double nextT;

            if (determinant <= 1e-12 * aa * bb)
            {
                nextS = domainA.Clamp(s - (ar / aa));
                nextT = b.ClosestParameter(a.PointAt(nextS));
            }
            else
            {
                nextS = domainA.Clamp(s + (((ab * br) - (bb * ar)) / determinant));
                nextT = domainB.Clamp(t + (((aa * br) - (ab * ar)) / determinant));
            }

            bool settled = Math.Abs(nextS - s) <= 1e-15 * (1.0 + Math.Abs(s))
                && Math.Abs(nextT - t) <= 1e-15 * (1.0 + Math.Abs(t));

            s = nextS;
            t = nextT;

            if (settled)
            {
                break;
            }
        }

        Point3d onA = a.PointAt(s);
        Point3d onB = b.PointAt(t);

        return onA.DistanceTo(onB) <= tol ? onA.Midpoint(onB) : null;
    }

    private static CurveIntersections Assemble(Curve a, Curve b, List<CurveIntersectionPoint> found, double tol)
    {
        found.Sort((x, y) => x.ParameterA.CompareTo(y.ParameterA));

        // An economy rather than a rule: duplicates sit next to each other along the first curve and
        // the runs below would merge them anyway, but a merge there costs three closest-point queries
        // and here it costs one distance.
        List<CurveIntersectionPoint> unique = [];

        foreach (CurveIntersectionPoint candidate in found)
        {
            bool seen = false;

            foreach (CurveIntersectionPoint kept in unique)
            {
                if (kept.Point.DistanceTo(candidate.Point) <= tol)
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
            {
                unique.Add(candidate);
            }
        }

        double spacing = a.Domain.Length / Samples;

        // Runs of candidates joined by curve that stays on the other.
        List<(int First, int Last)> runs = [];
        int first = 0;

        while (first < unique.Count)
        {
            int last = first;

            while (last + 1 < unique.Count
                && Joined(a, b, unique[last].ParameterA, unique[last + 1].ParameterA, tol))
            {
                last++;
            }

            runs.Add((first, last));
            first = last + 1;
        }

        // A closed curve has a seam, and a touch sitting on it is found from both sides - once near
        // the start of the domain and once near the end, a little apart, as a tangency always is.
        // Joined across the seam and short, the last run and the first are one touch.
        bool seamTouch = false;

        if (a.IsClosed && runs.Count > 1)
        {
            double before = unique[runs[^1].First].ParameterA;
            double after = unique[runs[0].Last].ParameterA + a.Domain.Length;

            seamTouch = after - before < spacing && JoinedAcrossSeam(a, b, before, after, tol);
        }

        List<CurveIntersectionPoint> points = [];
        List<CurveOverlap> overlaps = [];

        for (int r = 0; r < runs.Count; r++)
        {
            (int runFirst, int runLast) = runs[r];

            if (seamTouch && r == runs.Count - 1)
            {
                continue;
            }

            if (seamTouch && r == 0)
            {
                CurveIntersectionPoint start = Best(a, b, unique, runFirst, runLast);
                CurveIntersectionPoint end = Best(a, b, unique, runs[^1].First, runs[^1].Last);
                points.Add(Gap(a, b, start) <= Gap(a, b, end) ? start : end);
                continue;
            }

            double sFirst = unique[runFirst].ParameterA;
            double sLast = unique[runLast].ParameterA;

            if (runLast == runFirst || sLast - sFirst < spacing)
            {
                // One point, or a touch: refinement near a tangency settles a little apart each time.
                points.Add(Best(a, b, unique, runFirst, runLast));
            }
            else
            {
                double low = Edge(a, b, sFirst, a.Domain.Min, spacing, tol);
                double high = Edge(a, b, sLast, a.Domain.Max, spacing, tol);

                overlaps.Add(new CurveOverlap(
                    new Interval(low, high),
                    new Interval(b.ClosestParameter(a.PointAt(low)), b.ClosestParameter(a.PointAt(high)))));
            }
        }

        return points.Count == 0 && overlaps.Count == 0
            ? CurveIntersections.None
            : new CurveIntersections(points, overlaps);
    }

    /// <summary>The member of a run whose points on the two curves are closest together.</summary>
    private static CurveIntersectionPoint Best(Curve a, Curve b, List<CurveIntersectionPoint> run, int first, int last)
    {
        CurveIntersectionPoint best = run[first];
        double closest = double.PositiveInfinity;

        for (int k = first; k <= last; k++)
        {
            double gap = a.PointAt(run[k].ParameterA).DistanceTo(b.PointAt(run[k].ParameterB));

            if (gap < closest)
            {
                closest = gap;
                best = run[k];
            }
        }

        return best;
    }

    private static double Gap(Curve a, Curve b, CurveIntersectionPoint point) =>
        a.PointAt(point.ParameterA).DistanceTo(b.PointAt(point.ParameterB));

    /// <summary>
    /// Whether a closed first curve stays on the second from <paramref name="from"/>, across its seam,
    /// to <paramref name="to"/> - which is past the end of the domain by up to one domain length.
    /// </summary>
    private static bool JoinedAcrossSeam(Curve a, Curve b, double from, double to, double tol)
    {
        Interval domain = a.Domain;

        double Wrapped(double s) => s > domain.Max ? s - domain.Length : s;

        return On(a, b, Wrapped(from + (0.25 * (to - from))), tol)
            && On(a, b, Wrapped(from + (0.5 * (to - from))), tol)
            && On(a, b, Wrapped(from + (0.75 * (to - from))), tol);
    }

    private static bool On(Curve a, Curve b, double s, double tol)
    {
        Point3d point = a.PointAt(s);

        return b.ClosestPoint(point).DistanceTo(point) <= tol;
    }

    /// <summary>Whether the first curve stays on the second between two of its parameters.</summary>
    private static bool Joined(Curve a, Curve b, double from, double to, double tol) =>
        On(a, b, from + (0.25 * (to - from)), tol)
        && On(a, b, from + (0.5 * (to - from)), tol)
        && On(a, b, from + (0.75 * (to - from)), tol);

    /// <summary>
    /// How far an overlap reaches from a parameter on it towards a limit: stepped out by the sample
    /// spacing until the curve leaves the other, then bisected to the tolerance.
    /// </summary>
    private static double Edge(Curve a, Curve b, double on, double limit, double spacing, double tol)
    {
        double direction = Math.Sign(limit - on);
        double inside = on;

        while (Math.Abs(limit - inside) > 0.0)
        {
            double next = direction > 0 ? Math.Min(limit, inside + spacing) : Math.Max(limit, inside - spacing);

            if (!On(a, b, next, tol))
            {
                double outside = next;

                for (int k = 0; k < 60 && Math.Abs(outside - inside) > 1e-15 * (1.0 + Math.Abs(inside)); k++)
                {
                    double middle = (inside + outside) * 0.5;

                    if (On(a, b, middle, tol))
                    {
                        inside = middle;
                    }
                    else
                    {
                        outside = middle;
                    }
                }

                return inside;
            }

            inside = next;
        }

        return limit;
    }
}
