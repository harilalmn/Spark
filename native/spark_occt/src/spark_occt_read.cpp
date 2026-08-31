// spark_occt — reading an OpenCascade shape out into Spark's flat tables.
//
// Copyright (c) Spark contributors. MIT.
//
// This is the half that decides how much of the exactness survives. Every analytic surface
// OpenCascade recognises is emitted as the analytic surface Spark has — a cylinder stays a
// cylinder, and does not become a spline that happens to be round — and only what has no Spark
// equivalent is converted to NURBS, which for these types is an exact rational conversion rather
// than an approximation. That is the whole reason for taking an exact kernel rather than a mesh
// one, so it is worth the switch statement.

#define SPARK_OCCT_BUILD 1

#include "spark_occt_internal.hpp"

#include <BRepTools.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <BRep_Tool.hxx>
#include <GeomConvert.hxx>
#include <Geom_BSplineCurve.hxx>
#include <Geom_BSplineSurface.hxx>
#include <Geom_Circle.hxx>
#include <Geom_ConicalSurface.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <Geom_Ellipse.hxx>
#include <Geom_Line.hxx>
#include <Geom_Plane.hxx>
#include <Geom_RectangularTrimmedSurface.hxx>
#include <Geom_SphericalSurface.hxx>
#include <Geom_ToroidalSurface.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Vertex.hxx>
#include <TopoDS_Wire.hxx>
#include <gp_Ax2.hxx>
#include <gp_Ax3.hxx>

#include <cmath>
#include <string>
#include <vector>

using spark::ax3_to_frame;
using spark::fail;

namespace
{
    constexpr double kTwoPi = 6.283185307179586476925286766559;
    constexpr double kHalfPi = 1.5707963267948966192313216916398;

    void push(std::vector<double>& into, const double* values, int count)
    {
        for (int i = 0; i < count; i++)
        {
            into.push_back(values[i]);
        }
    }

    void push_domain(std::vector<double>& into, double low, double high)
    {
        into.push_back(low);
        into.push_back(high);
    }

    // Spark's knot vectors are the full form and OpenCascade's are compressed, so this is the
    // return trip of `compress` in spark_occt_import.cpp.
    void expand(const Handle(Geom_BSplineCurve)& curve, std::vector<double>& into)
    {
        for (int i = 1; i <= curve->NbKnots(); i++)
        {
            for (int m = 0; m < curve->Multiplicity(i); m++)
            {
                into.push_back(curve->Knot(i));
            }
        }
    }

    void expand_u(const Handle(Geom_BSplineSurface)& surface, std::vector<double>& into)
    {
        for (int i = 1; i <= surface->NbUKnots(); i++)
        {
            for (int m = 0; m < surface->UMultiplicity(i); m++)
            {
                into.push_back(surface->UKnot(i));
            }
        }
    }

    void expand_v(const Handle(Geom_BSplineSurface)& surface, std::vector<double>& into)
    {
        for (int i = 1; i <= surface->NbVKnots(); i++)
        {
            for (int m = 0; m < surface->VMultiplicity(i); m++)
            {
                into.push_back(surface->VKnot(i));
            }
        }
    }

    gp_Ax3 as_ax3(const gp_Ax2& axes)
    {
        return gp_Ax3(axes);
    }

    // Appends one curve to the model and returns its index.
    int32_t emit_curve(spark_model& model, int32_t kind, const std::vector<int32_t>& ints, const std::vector<double>& values)
    {
        const int32_t index = static_cast<int32_t>(model.curve_kinds.size());

        model.curve_kinds.push_back(kind);
        model.curve_ints.insert(model.curve_ints.end(), ints.begin(), ints.end());
        model.curve_doubles.insert(model.curve_doubles.end(), values.begin(), values.end());
        model.curve_int_offsets.push_back(static_cast<int32_t>(model.curve_ints.size()));
        model.curve_double_offsets.push_back(static_cast<int32_t>(model.curve_doubles.size()));

        return index;
    }

    int32_t emit_surface(spark_model& model, int32_t kind, const std::vector<int32_t>& ints, const std::vector<double>& values)
    {
        const int32_t index = static_cast<int32_t>(model.surface_kinds.size());

        model.surface_kinds.push_back(kind);
        model.surface_ints.insert(model.surface_ints.end(), ints.begin(), ints.end());
        model.surface_doubles.insert(model.surface_doubles.end(), values.begin(), values.end());
        model.surface_int_offsets.push_back(static_cast<int32_t>(model.surface_ints.size()));
        model.surface_double_offsets.push_back(static_cast<int32_t>(model.surface_doubles.size()));

        return index;
    }

    void nurbs_curve(const Handle(Geom_BSplineCurve)& spline, std::vector<int32_t>& ints, std::vector<double>& values)
    {
        const bool rational = spline->IsRational();
        const int32_t poles = static_cast<int32_t>(spline->NbPoles());

        expand(spline, values);

        ints.push_back(static_cast<int32_t>(spline->Degree()));
        ints.push_back(poles);
        ints.push_back(static_cast<int32_t>(values.size()));
        ints.push_back(rational ? 1 : 0);

        for (int i = 1; i <= spline->NbPoles(); i++)
        {
            const gp_Pnt pole = spline->Pole(i);
            values.push_back(pole.X());
            values.push_back(pole.Y());
            values.push_back(pole.Z());

            if (rational)
            {
                values.push_back(spline->Weight(i));
            }
        }
    }

    // Every curve OpenCascade may hand back, mapped onto the five Spark has. `first` and `last`
    // are the edge's own range, which is what makes a circle an arc.
    void read_curve(
        spark_model& model,
        const Handle(Geom_Curve)& raw,
        double first,
        double last,
        int32_t& index)
    {
        Handle(Geom_Curve) curve = raw;

        // A trimmed curve is a range wrapped round a basis curve, and the edge already carries the
        // range, so unwrapping loses nothing and saves a case in every branch below.
        while (!curve.IsNull() && curve->IsKind(STANDARD_TYPE(Geom_TrimmedCurve)))
        {
            curve = Handle(Geom_TrimmedCurve)::DownCast(curve)->BasisCurve();
        }

        std::vector<int32_t> ints;
        std::vector<double> values;

        if (!curve.IsNull() && curve->IsKind(STANDARD_TYPE(Geom_Line)))
        {
            const gp_Pnt start = curve->Value(first);
            const gp_Pnt end = curve->Value(last);

            values.push_back(start.X());
            values.push_back(start.Y());
            values.push_back(start.Z());
            values.push_back(end.X());
            values.push_back(end.Y());
            values.push_back(end.Z());

            index = emit_curve(model, SPARK_CURVE_LINE, ints, values);
            return;
        }

        if (!curve.IsNull() && curve->IsKind(STANDARD_TYPE(Geom_Circle)))
        {
            const Handle(Geom_Circle) circle = Handle(Geom_Circle)::DownCast(curve);
            double frame[9];
            const bool direct = ax3_to_frame(as_ax3(circle->Position()), frame);

            const double sweep = last - first;
            const double start = direct ? first : -last;
            const double turn = direct ? sweep : sweep;

            push(values, frame, 9);
            values.push_back(circle->Radius());

            if (std::abs(sweep) >= kTwoPi - 1.0e-9)
            {
                index = emit_curve(model, SPARK_CURVE_CIRCLE, ints, values);
                return;
            }

            values.push_back(start);
            values.push_back(turn);
            index = emit_curve(model, SPARK_CURVE_ARC, ints, values);
            return;
        }

        if (!curve.IsNull() && curve->IsKind(STANDARD_TYPE(Geom_Ellipse)))
        {
            const Handle(Geom_Ellipse) ellipse = Handle(Geom_Ellipse)::DownCast(curve);
            double frame[9];
            const bool direct = ax3_to_frame(as_ax3(ellipse->Position()), frame);

            push(values, frame, 9);
            values.push_back(ellipse->MajorRadius());
            values.push_back(ellipse->MinorRadius());
            values.push_back(direct ? first : -last);
            values.push_back(last - first);

            index = emit_curve(model, SPARK_CURVE_ELLIPSE, ints, values);
            return;
        }

        // Everything else. The conversion is exact for the conics and the analytic curves, and is
        // OpenCascade's own approximation for anything genuinely approximate.
        Handle(Geom_Curve) trimmed = curve.IsNull() ? curve : new Geom_TrimmedCurve(curve, first, last);
        Handle(Geom_BSplineCurve) spline =
            trimmed.IsNull() ? Handle(Geom_BSplineCurve)() : GeomConvert::CurveToBSplineCurve(trimmed);

        if (spline.IsNull())
        {
            // Nothing we can say about it. A straight segment between the ends is a poor curve and
            // an honest one, and it keeps the tables consistent rather than leaving a hole.
            const gp_Pnt start = raw.IsNull() ? gp_Pnt() : raw->Value(first);
            const gp_Pnt end = raw.IsNull() ? gp_Pnt(1.0, 0.0, 0.0) : raw->Value(last);

            values.push_back(start.X());
            values.push_back(start.Y());
            values.push_back(start.Z());
            values.push_back(end.X());
            values.push_back(end.Y());
            values.push_back(end.Z());

            index = emit_curve(model, SPARK_CURVE_LINE, ints, values);
            return;
        }

        nurbs_curve(spline, ints, values);
        index = emit_curve(model, SPARK_CURVE_NURBS, ints, values);
    }

    void nurbs_surface(
        const Handle(Geom_BSplineSurface)& spline, std::vector<int32_t>& ints, std::vector<double>& values)
    {
        const bool rational = spline->IsURational() || spline->IsVRational();

        std::vector<double> knots_u;
        std::vector<double> knots_v;
        expand_u(spline, knots_u);
        expand_v(spline, knots_v);

        ints.push_back(static_cast<int32_t>(spline->UDegree()));
        ints.push_back(static_cast<int32_t>(spline->VDegree()));
        ints.push_back(static_cast<int32_t>(spline->NbUPoles()));
        ints.push_back(static_cast<int32_t>(spline->NbVPoles()));
        ints.push_back(static_cast<int32_t>(knots_u.size()));
        ints.push_back(static_cast<int32_t>(knots_v.size()));
        ints.push_back(rational ? 1 : 0);

        values.insert(values.end(), knots_u.begin(), knots_u.end());
        values.insert(values.end(), knots_v.begin(), knots_v.end());

        for (int i = 1; i <= spline->NbUPoles(); i++)
        {
            for (int j = 1; j <= spline->NbVPoles(); j++)
            {
                const gp_Pnt pole = spline->Pole(i, j);
                values.push_back(pole.X());
                values.push_back(pole.Y());
                values.push_back(pole.Z());

                if (rational)
                {
                    values.push_back(spline->Weight(i, j));
                }
            }
        }
    }

    void read_surface(
        spark_model& model,
        const Handle(Geom_Surface)& raw,
        double u0,
        double u1,
        double v0,
        double v1,
        int32_t& index)
    {
        Handle(Geom_Surface) surface = raw;

        while (!surface.IsNull() && surface->IsKind(STANDARD_TYPE(Geom_RectangularTrimmedSurface)))
        {
            surface = Handle(Geom_RectangularTrimmedSurface)::DownCast(surface)->BasisSurface();
        }

        std::vector<int32_t> ints;
        std::vector<double> values;
        double frame[9];

        // A left-handed placement is a thing OpenCascade can say and Spark cannot: Spark's normal
        // is always x cross y. `ax3_to_frame` negates the y-axis so the normal comes out right,
        // which mirrors the surface in u — so the u-domain is mirrored back here. Getting this
        // wrong produces a surface that is correct everywhere except which way round it runs.
        auto mirrored = [&](bool direct, double low, double high, double& out_low, double& out_high)
        {
            out_low = direct ? low : -high;
            out_high = direct ? high : -low;
        };

        if (!surface.IsNull() && surface->IsKind(STANDARD_TYPE(Geom_Plane)))
        {
            const bool direct = ax3_to_frame(Handle(Geom_Plane)::DownCast(surface)->Position(), frame);
            double a = 0.0;
            double b = 0.0;
            mirrored(direct, u0, u1, a, b);

            push(values, frame, 9);
            push_domain(values, a, b);
            push_domain(values, v0, v1);

            index = emit_surface(model, SPARK_SURFACE_PLANE, ints, values);
            return;
        }

        if (!surface.IsNull() && surface->IsKind(STANDARD_TYPE(Geom_CylindricalSurface)))
        {
            const Handle(Geom_CylindricalSurface) cylinder =
                Handle(Geom_CylindricalSurface)::DownCast(surface);
            const bool direct = ax3_to_frame(cylinder->Position(), frame);
            double a = 0.0;
            double b = 0.0;
            mirrored(direct, u0, u1, a, b);

            push(values, frame, 9);
            values.push_back(cylinder->Radius());
            push_domain(values, a, b);
            push_domain(values, v0, v1);

            index = emit_surface(model, SPARK_SURFACE_CYLINDER, ints, values);
            return;
        }

        if (!surface.IsNull() && surface->IsKind(STANDARD_TYPE(Geom_ConicalSurface)))
        {
            const Handle(Geom_ConicalSurface) cone = Handle(Geom_ConicalSurface)::DownCast(surface);
            const double half_angle = cone->SemiAngle();

            // Spark's cone measures v along the axis and OpenCascade's along the slant, and Spark
            // has no way to spell a negative half-angle. Both are conversions rather than
            // obstacles, and only the second can fail — a converging cone falls through to NURBS,
            // which is exact for a cone rather than an approximation of one.
            if (half_angle > 0.0 && half_angle < kHalfPi)
            {
                const bool direct = ax3_to_frame(cone->Position(), frame);
                const double slant = std::cos(half_angle);
                double a = 0.0;
                double b = 0.0;
                mirrored(direct, u0, u1, a, b);

                push(values, frame, 9);
                values.push_back(cone->RefRadius());
                values.push_back(half_angle);
                push_domain(values, a, b);
                push_domain(values, v0 * slant, v1 * slant);

                index = emit_surface(model, SPARK_SURFACE_CONE, ints, values);
                return;
            }
        }

        if (!surface.IsNull() && surface->IsKind(STANDARD_TYPE(Geom_SphericalSurface)))
        {
            const Handle(Geom_SphericalSurface) sphere = Handle(Geom_SphericalSurface)::DownCast(surface);
            const bool direct = ax3_to_frame(sphere->Position(), frame);
            double a = 0.0;
            double b = 0.0;
            mirrored(direct, u0, u1, a, b);

            push(values, frame, 9);
            values.push_back(sphere->Radius());
            push_domain(values, a, b);
            push_domain(values, v0, v1);

            index = emit_surface(model, SPARK_SURFACE_SPHERE, ints, values);
            return;
        }

        if (!surface.IsNull() && surface->IsKind(STANDARD_TYPE(Geom_ToroidalSurface)))
        {
            const Handle(Geom_ToroidalSurface) torus = Handle(Geom_ToroidalSurface)::DownCast(surface);
            const bool direct = ax3_to_frame(torus->Position(), frame);
            double a = 0.0;
            double b = 0.0;
            mirrored(direct, u0, u1, a, b);

            push(values, frame, 9);
            values.push_back(torus->MajorRadius());
            values.push_back(torus->MinorRadius());
            push_domain(values, a, b);
            push_domain(values, v0, v1);

            index = emit_surface(model, SPARK_SURFACE_TORUS, ints, values);
            return;
        }

        Handle(Geom_Surface) bounded =
            surface.IsNull() ? surface : new Geom_RectangularTrimmedSurface(surface, u0, u1, v0, v1);
        Handle(Geom_BSplineSurface) spline =
            bounded.IsNull() ? Handle(Geom_BSplineSurface)() : GeomConvert::SurfaceToBSplineSurface(bounded);

        if (spline.IsNull())
        {
            // A plane through the corner is the only thing left to say, and saying it keeps the
            // tables consistent. It is reported as such by the face count rather than pretended.
            gp_Ax3 fallback;
            ax3_to_frame(fallback, frame);
            push(values, frame, 9);
            push_domain(values, u0, u1);
            push_domain(values, v0, v1);
            index = emit_surface(model, SPARK_SURFACE_PLANE, ints, values);
            return;
        }

        nurbs_surface(spline, ints, values);
        index = emit_surface(model, SPARK_SURFACE_NURBS, ints, values);
    }
}

namespace spark
{
    void ordered_faces(const TopoDS_Shape& shape, SparkShapeMap& into)
    {
        for (TopExp_Explorer shells(shape, TopAbs_SHELL); shells.More(); shells.Next())
        {
            for (TopExp_Explorer faces(shells.Value(), TopAbs_FACE); faces.More(); faces.Next())
            {
                into.Add(faces.Value());
            }
        }

        // Faces that belong to no shell — a bare sheet, or what a failed sew leaves behind. They
        // come last so that a shell's faces stay contiguous, which is what Spark's layout needs.
        for (TopExp_Explorer faces(shape, TopAbs_FACE); faces.More(); faces.Next())
        {
            into.Add(faces.Value());
        }
    }

    void ordered_edges(const TopoDS_Shape& shape, SparkShapeMap& into)
    {
        for (TopExp_Explorer edges(shape, TopAbs_EDGE); edges.More(); edges.Next())
        {
            if (!BRep_Tool::Degenerated(TopoDS::Edge(edges.Value())))
            {
                into.Add(edges.Value());
            }
        }
    }

    spark_status read_model(const TopoDS_Shape& shape, double tolerance, spark_model& into)
    {
        (void)tolerance;

        SparkShapeMap vertices;
        TopExp::MapShapes(shape, TopAbs_VERTEX, vertices);

        SparkShapeMap edges;
        ordered_edges(shape, edges);

        SparkShapeMap faces;
        ordered_faces(shape, faces);

        // Vertices and their points, one to one. Spark separates them because a point is a value
        // and a vertex is a place in a topology, and two vertices may sit on one point.
        for (int i = 1; i <= vertices.Extent(); i++)
        {
            const gp_Pnt position = BRep_Tool::Pnt(TopoDS::Vertex(vertices(i)));
            into.points.push_back(position.X());
            into.points.push_back(position.Y());
            into.points.push_back(position.Z());
            into.vertices.push_back(i - 1);
        }

        into.curve_int_offsets.push_back(0);
        into.curve_double_offsets.push_back(0);
        into.surface_int_offsets.push_back(0);
        into.surface_double_offsets.push_back(0);

        // Edges, each with its own curve. One curve per edge rather than a shared table, because
        // OpenCascade's curves are already per-edge and inventing sharing here would mean
        // comparing geometry for equality, which is exactly the sort of thing that is nearly
        // right until it is not.
        for (int i = 1; i <= edges.Extent(); i++)
        {
            const TopoDS_Edge edge = TopoDS::Edge(edges(i));
            TopoDS_Edge forward = edge;
            forward.Orientation(TopAbs_FORWARD);

            TopoDS_Vertex start;
            TopoDS_Vertex end;
            TopExp::Vertices(forward, start, end);

            const int32_t startIndex = start.IsNull() ? 0 : vertices.FindIndex(start) - 1;
            const int32_t endIndex = end.IsNull() ? 0 : vertices.FindIndex(end) - 1;

            double first = 0.0;
            double last = 1.0;
            const Handle(Geom_Curve) curve = BRep_Tool::Curve(forward, first, last);

            int32_t curveIndex = 0;
            read_curve(into, curve, first, last, curveIndex);

            into.edges.push_back(startIndex < 0 ? 0 : startIndex);
            into.edges.push_back(endIndex < 0 ? 0 : endIndex);
            into.edges.push_back(curveIndex);
        }

        // Faces, their loops and their trims — each contiguous inside the next, which is Spark's
        // whole BRep layout and the reason this walk is one pass rather than three.
        for (int i = 1; i <= faces.Extent(); i++)
        {
            const TopoDS_Face face = TopoDS::Face(faces(i));
            const int32_t firstLoop = static_cast<int32_t>(into.loops.size() / 3);

            double u0 = 0.0;
            double u1 = 0.0;
            double v0 = 0.0;
            double v1 = 0.0;
            BRepTools::UVBounds(face, u0, u1, v0, v1);

            TopoDS_Face upright = face;
            upright.Orientation(TopAbs_FORWARD);

            int32_t surfaceIndex = 0;
            read_surface(into, BRep_Tool::Surface(upright), u0, u1, v0, v1, surfaceIndex);

            const TopoDS_Wire outer = BRepTools::OuterWire(upright);
            int32_t loopCount = 0;

            for (TopExp_Explorer wires(upright, TopAbs_WIRE); wires.More(); wires.Next())
            {
                const TopoDS_Wire wire = TopoDS::Wire(wires.Value());
                const int32_t firstTrim = static_cast<int32_t>(into.trims.size() / 2);
                int32_t trimCount = 0;

                for (BRepTools_WireExplorer along(wire, upright); along.More(); along.Next())
                {
                    const TopoDS_Edge edge = along.Current();

                    if (BRep_Tool::Degenerated(edge))
                    {
                        continue;
                    }

                    const int found = edges.FindIndex(edge);

                    if (found <= 0)
                    {
                        continue;
                    }

                    into.trims.push_back(static_cast<int32_t>(found) - 1);
                    into.trims.push_back(edge.Orientation() == TopAbs_REVERSED ? 1 : 0);
                    trimCount++;
                }

                if (trimCount == 0)
                {
                    continue;
                }

                into.loops.push_back(firstTrim);
                into.loops.push_back(trimCount);
                into.loops.push_back(wire.IsSame(outer) ? SPARK_LOOP_OUTER : SPARK_LOOP_INNER);
                loopCount++;
            }

            into.faces.push_back(surfaceIndex);
            into.faces.push_back(firstLoop);
            into.faces.push_back(loopCount);
            into.faces.push_back(face.Orientation() == TopAbs_REVERSED ? 1 : 0);
        }

        // Shells last, over the face order that was built shell by shell, so a shell's faces are
        // the contiguous run its two numbers describe.
        int seen = 0;

        for (TopExp_Explorer shells(shape, TopAbs_SHELL); shells.More(); shells.Next())
        {
            int count = 0;

            for (TopExp_Explorer within(shells.Value(), TopAbs_FACE); within.More(); within.Next())
            {
                count++;
            }

            into.shells.push_back(seen);
            into.shells.push_back(count);
            seen += count;
        }

        if (into.shells.empty() && !into.faces.empty())
        {
            into.shells.push_back(0);
            into.shells.push_back(static_cast<int32_t>(into.faces.size() / 4));
        }

        if (into.faces.empty())
        {
            return fail(SPARK_ERR_REFUSED, "The shape has no faces to read.");
        }

        return SPARK_OK;
    }
}
