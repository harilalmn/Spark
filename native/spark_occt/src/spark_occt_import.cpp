// spark_occt — building an OpenCascade shape out of Spark's flat tables.
//
// Copyright (c) Spark contributors. MIT.
//
// A FACE IS BUILT FROM ITS LOOPS, and the pcurves are computed here rather than sent.
//
// Spark's trims carry no parameter-space curve — `Spark.Geometry` has no pcurves — so a wire
// arrives as 3D edges and nothing else, and OpenCascade needs a pcurve per edge per face before
// it will do anything with the result. `ShapeFix_Face` computes them by projection, which is
// exactly the path an IGES or STL import takes and is the reason that path exists.
//
// The first version of this file skipped the loops and bounded each face by its surface's own
// domain instead. It produced a correct box and a WRONG CYLINDER: a tube with two square plates
// where the round caps should be, which sews into something that looks plausible in a mesh and
// refuses every boolean. The demo graph is what found it. A face with no loops still takes that
// path, because an untrimmed patch is what it means.

#define SPARK_OCCT_BUILD 1

#include "spark_occt_internal.hpp"

#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeVertex.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_Sewing.hxx>
#include <BRep_Builder.hxx>
#include <ShapeFix_Face.hxx>
#include <ShapeFix_Solid.hxx>
#include <GC_MakeSegment.hxx>
#include <Geom_BSplineCurve.hxx>
#include <Geom_BSplineSurface.hxx>
#include <Geom_Circle.hxx>
#include <Geom_ConicalSurface.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <Geom_Ellipse.hxx>
#include <Geom_Plane.hxx>
#include <Geom_SphericalSurface.hxx>
#include <Geom_ToroidalSurface.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Shell.hxx>
#include <TopoDS_Solid.hxx>
#include <TopoDS_Vertex.hxx>
#include <TopoDS_Wire.hxx>
#include <BRep_Builder.hxx>
#include <gp_Ax2.hxx>
#include <gp_Ax3.hxx>

#include <cmath>
#include <string>
#include <vector>

using spark::fail;
using spark::frame_to_ax3;

namespace
{
    constexpr double kTwoPi = 6.283185307179586476925286766559;

    // Spark's knot vectors are the full form — one entry per multiplicity — and OpenCascade's are
    // the compressed form. This is the only place the two conventions meet, and it is the sort of
    // difference that produces a curve that is *almost* right if it is got wrong.
    bool compress(
        const double* knots,
        int32_t count,
        std::vector<double>& values,
        std::vector<int32_t>& multiplicities)
    {
        if (count <= 0)
        {
            return false;
        }

        for (int32_t i = 0; i < count; i++)
        {
            if (!std::isfinite(knots[i]))
            {
                return false;
            }

            if (!values.empty() && knots[i] == values.back())
            {
                multiplicities.back()++;
            }
            else if (!values.empty() && knots[i] < values.back())
            {
                return false;
            }
            else
            {
                values.push_back(knots[i]);
                multiplicities.push_back(1);
            }
        }

        return values.size() >= 2;
    }

    struct Reader
    {
        const double* doubles;
        int32_t count;
        int32_t at;

        bool take(int32_t many, const double** into)
        {
            if (at + many > count)
            {
                return false;
            }

            *into = doubles + at;
            at += many;
            return true;
        }

        bool take(double* into)
        {
            if (at >= count)
            {
                return false;
            }

            *into = doubles[at++];
            return true;
        }
    };

    Handle(Geom_Curve) make_curve(
        int32_t kind, const int32_t* ints, int32_t int_count, const double* values, int32_t value_count)
    {
        Reader reader{ values, value_count, 0 };

        switch (kind)
        {
            case SPARK_CURVE_LINE:
            {
                const double* start = nullptr;
                const double* end = nullptr;

                if (!reader.take(3, &start) || !reader.take(3, &end))
                {
                    return Handle(Geom_Curve)();
                }

                return GC_MakeSegment(spark::point(start), spark::point(end)).Value();
            }

            case SPARK_CURVE_CIRCLE:
            case SPARK_CURVE_ARC:
            {
                const double* frame = nullptr;
                double radius = 0.0;

                if (!reader.take(9, &frame) || !reader.take(&radius) || radius <= 0.0)
                {
                    return Handle(Geom_Curve)();
                }

                const gp_Ax3 axes = frame_to_ax3(frame);
                Handle(Geom_Circle) circle =
                    new Geom_Circle(gp_Ax2(axes.Location(), axes.Direction(), axes.XDirection()), radius);

                if (kind == SPARK_CURVE_CIRCLE)
                {
                    return circle;
                }

                double start = 0.0;
                double sweep = 0.0;

                if (!reader.take(&start) || !reader.take(&sweep) || sweep == 0.0)
                {
                    return Handle(Geom_Curve)();
                }

                if (std::abs(sweep) >= kTwoPi - 1.0e-12)
                {
                    return circle;
                }

                const double low = sweep > 0.0 ? start : start + sweep;
                const double high = sweep > 0.0 ? start + sweep : start;

                return new Geom_TrimmedCurve(circle, low, high, true);
            }

            case SPARK_CURVE_ELLIPSE:
            {
                const double* frame = nullptr;
                double major = 0.0;
                double minor = 0.0;

                if (!reader.take(9, &frame) || !reader.take(&major) || !reader.take(&minor)
                    || major <= 0.0 || minor <= 0.0 || minor > major)
                {
                    return Handle(Geom_Curve)();
                }

                const gp_Ax3 axes = frame_to_ax3(frame);
                Handle(Geom_Ellipse) ellipse = new Geom_Ellipse(
                    gp_Ax2(axes.Location(), axes.Direction(), axes.XDirection()), major, minor);

                double start = 0.0;
                double sweep = 0.0;

                if (!reader.take(&start) || !reader.take(&sweep) || sweep == 0.0)
                {
                    return Handle(Geom_Curve)();
                }

                if (std::abs(sweep) >= kTwoPi - 1.0e-12)
                {
                    return ellipse;
                }

                const double low = sweep > 0.0 ? start : start + sweep;
                const double high = sweep > 0.0 ? start + sweep : start;

                return new Geom_TrimmedCurve(ellipse, low, high, true);
            }

            case SPARK_CURVE_NURBS:
            {
                if (int_count < 4)
                {
                    return Handle(Geom_Curve)();
                }

                const int32_t degree = ints[0];
                const int32_t poles = ints[1];
                const int32_t knot_count = ints[2];
                const bool rational = ints[3] != 0;
                const int32_t stride = rational ? 4 : 3;

                if (degree < 1 || poles < degree + 1 || knot_count != poles + degree + 1
                    || value_count != knot_count + (poles * stride))
                {
                    return Handle(Geom_Curve)();
                }

                std::vector<double> knot_values;
                std::vector<int32_t> knot_multiplicities;

                if (!compress(values, knot_count, knot_values, knot_multiplicities))
                {
                    return Handle(Geom_Curve)();
                }

                NCollection_Array1<gp_Pnt> control(1, poles);
                NCollection_Array1<double> weights(1, poles);
                const double* pole = values + knot_count;

                for (int32_t i = 0; i < poles; i++)
                {
                    control.SetValue(i + 1, gp_Pnt(pole[0], pole[1], pole[2]));
                    weights.SetValue(i + 1, rational ? pole[3] : 1.0);
                    pole += stride;
                }

                NCollection_Array1<double> knots(1, static_cast<int>(knot_values.size()));
                NCollection_Array1<int> mults(1, static_cast<int>(knot_values.size()));

                for (size_t i = 0; i < knot_values.size(); i++)
                {
                    knots.SetValue(static_cast<int>(i) + 1, knot_values[i]);
                    mults.SetValue(static_cast<int>(i) + 1, knot_multiplicities[i]);
                }

                return new Geom_BSplineCurve(control, weights, knots, mults, degree);
            }

            default:
                return Handle(Geom_Curve)();
        }
    }

    // A surface and the domain the face on it occupies. The domain is part of the surface in
    // Spark's model and is a face bound in OpenCascade's, so it travels beside the surface here.
    struct Patch
    {
        Handle(Geom_Surface) surface;
        double u0 = 0.0;
        double u1 = 0.0;
        double v0 = 0.0;
        double v1 = 0.0;
    };

    bool make_surface(
        int32_t kind,
        const int32_t* ints,
        int32_t int_count,
        const double* values,
        int32_t value_count,
        Patch& patch)
    {
        Reader reader{ values, value_count, 0 };

        auto domain = [&]() -> bool
        {
            return reader.take(&patch.u0) && reader.take(&patch.u1)
                && reader.take(&patch.v0) && reader.take(&patch.v1);
        };

        switch (kind)
        {
            case SPARK_SURFACE_PLANE:
            {
                const double* frame = nullptr;

                if (!reader.take(9, &frame) || !domain())
                {
                    return false;
                }

                patch.surface = new Geom_Plane(frame_to_ax3(frame));
                return true;
            }

            case SPARK_SURFACE_CYLINDER:
            {
                const double* frame = nullptr;
                double radius = 0.0;

                if (!reader.take(9, &frame) || !reader.take(&radius) || !domain() || radius <= 0.0)
                {
                    return false;
                }

                patch.surface = new Geom_CylindricalSurface(frame_to_ax3(frame), radius);
                return true;
            }

            case SPARK_SURFACE_CONE:
            {
                const double* frame = nullptr;
                double radius = 0.0;
                double half_angle = 0.0;

                if (!reader.take(9, &frame) || !reader.take(&radius) || !reader.take(&half_angle)
                    || !domain() || radius < 0.0)
                {
                    return false;
                }

                // OpenCascade measures a cone's v along the *slant* and Spark measures it along
                // the axis, so the domain converts even though the surface is the same one. It
                // also insists on a half-angle in ]0, pi/2[; a Spark cone that narrows upwards is
                // converted to a NURBS surface on the managed side rather than fudged here.
                if (!(half_angle > 0.0) || half_angle >= 1.5707963267948966)
                {
                    return false;
                }

                const double slant = std::cos(half_angle);

                if (slant <= 0.0)
                {
                    return false;
                }

                patch.v0 /= slant;
                patch.v1 /= slant;
                patch.surface = new Geom_ConicalSurface(frame_to_ax3(frame), half_angle, radius);
                return true;
            }

            case SPARK_SURFACE_SPHERE:
            {
                const double* frame = nullptr;
                double radius = 0.0;

                if (!reader.take(9, &frame) || !reader.take(&radius) || !domain() || radius <= 0.0)
                {
                    return false;
                }

                patch.surface = new Geom_SphericalSurface(frame_to_ax3(frame), radius);
                return true;
            }

            case SPARK_SURFACE_TORUS:
            {
                const double* frame = nullptr;
                double major = 0.0;
                double minor = 0.0;

                if (!reader.take(9, &frame) || !reader.take(&major) || !reader.take(&minor)
                    || !domain() || major <= 0.0 || minor <= 0.0)
                {
                    return false;
                }

                patch.surface = new Geom_ToroidalSurface(frame_to_ax3(frame), major, minor);
                return true;
            }

            case SPARK_SURFACE_NURBS:
            {
                if (int_count < 7)
                {
                    return false;
                }

                const int32_t degree_u = ints[0];
                const int32_t degree_v = ints[1];
                const int32_t count_u = ints[2];
                const int32_t count_v = ints[3];
                const int32_t knots_u = ints[4];
                const int32_t knots_v = ints[5];
                const bool rational = ints[6] != 0;
                const int32_t stride = rational ? 4 : 3;

                if (degree_u < 1 || degree_v < 1 || count_u < degree_u + 1 || count_v < degree_v + 1
                    || knots_u != count_u + degree_u + 1 || knots_v != count_v + degree_v + 1
                    || value_count != knots_u + knots_v + (count_u * count_v * stride))
                {
                    return false;
                }

                std::vector<double> values_u;
                std::vector<int32_t> mults_u;
                std::vector<double> values_v;
                std::vector<int32_t> mults_v;

                if (!compress(values, knots_u, values_u, mults_u)
                    || !compress(values + knots_u, knots_v, values_v, mults_v))
                {
                    return false;
                }

                NCollection_Array2<gp_Pnt> control(1, count_u, 1, count_v);
                NCollection_Array2<double> weights(1, count_u, 1, count_v);
                const double* pole = values + knots_u + knots_v;

                for (int32_t i = 0; i < count_u; i++)
                {
                    for (int32_t j = 0; j < count_v; j++)
                    {
                        control.SetValue(i + 1, j + 1, gp_Pnt(pole[0], pole[1], pole[2]));
                        weights.SetValue(i + 1, j + 1, rational ? pole[3] : 1.0);
                        pole += stride;
                    }
                }

                NCollection_Array1<double> knot_u(1, static_cast<int>(values_u.size()));
                NCollection_Array1<int> mult_u(1, static_cast<int>(values_u.size()));

                for (size_t i = 0; i < values_u.size(); i++)
                {
                    knot_u.SetValue(static_cast<int>(i) + 1, values_u[i]);
                    mult_u.SetValue(static_cast<int>(i) + 1, mults_u[i]);
                }

                NCollection_Array1<double> knot_v(1, static_cast<int>(values_v.size()));
                NCollection_Array1<int> mult_v(1, static_cast<int>(values_v.size()));

                for (size_t i = 0; i < values_v.size(); i++)
                {
                    knot_v.SetValue(static_cast<int>(i) + 1, values_v[i]);
                    mult_v.SetValue(static_cast<int>(i) + 1, mults_v[i]);
                }

                patch.surface = new Geom_BSplineSurface(
                    control, weights, knot_u, knot_v, mult_u, mult_v, degree_u, degree_v);

                patch.u0 = knot_u.Value(1);
                patch.u1 = knot_u.Value(knot_u.Upper());
                patch.v0 = knot_v.Value(1);
                patch.v1 = knot_v.Value(knot_v.Upper());
                return true;
            }

            default:
                return false;
        }
    }

    int32_t span(const int32_t* offsets, int32_t index)
    {
        return offsets[index + 1] - offsets[index];
    }

    // Every edge in the model, as a TopoDS_Edge sharing its vertices with its neighbours. An edge
    // that cannot be built is left null rather than failing the whole import: a loop that needed
    // it falls back to the surface's own rectangle, and a loop that did not is unaffected.
    spark_status build_edges(
        const spark_model_desc& desc, double tolerance, std::vector<TopoDS_Edge>& into)
    {
        BRep_Builder builder;
        std::vector<TopoDS_Vertex> vertices;
        vertices.reserve(static_cast<size_t>(desc.vertex_count));

        for (int32_t i = 0; i < desc.vertex_count; i++)
        {
            const int32_t point = desc.vertices == nullptr ? -1 : desc.vertices[i];

            if (point < 0 || point >= desc.point_count || desc.points == nullptr)
            {
                vertices.push_back(TopoDS_Vertex());
                continue;
            }

            TopoDS_Vertex vertex;
            builder.MakeVertex(vertex, spark::point(desc.points + (point * 3)), tolerance);
            vertices.push_back(vertex);
        }

        into.reserve(static_cast<size_t>(desc.edge_count));

        for (int32_t i = 0; i < desc.edge_count; i++)
        {
            const int32_t start = desc.edges[(i * 3) + 0];
            const int32_t end = desc.edges[(i * 3) + 1];
            const int32_t curveIndex = desc.edges[(i * 3) + 2];

            if (curveIndex < 0 || curveIndex >= desc.curve_count)
            {
                into.push_back(TopoDS_Edge());
                continue;
            }

            const Handle(Geom_Curve) curve = make_curve(
                desc.curve_kinds[curveIndex],
                desc.curve_ints + desc.curve_int_offsets[curveIndex],
                span(desc.curve_int_offsets, curveIndex),
                desc.curve_doubles + desc.curve_double_offsets[curveIndex],
                span(desc.curve_double_offsets, curveIndex));

            if (curve.IsNull())
            {
                return fail(
                    SPARK_ERR_UNSUPPORTED,
                    "Curve " + std::to_string(curveIndex) + " is of a kind ("
                        + std::to_string(desc.curve_kinds[curveIndex])
                        + ") this build cannot rebuild, or its data did not describe one.");
            }

            const bool haveVertices = start >= 0 && start < static_cast<int32_t>(vertices.size())
                && end >= 0 && end < static_cast<int32_t>(vertices.size())
                && !vertices[static_cast<size_t>(start)].IsNull()
                && !vertices[static_cast<size_t>(end)].IsNull();

            TopoDS_Edge edge;

            if (haveVertices)
            {
                BRepBuilderAPI_MakeEdge maker(
                    curve,
                    vertices[static_cast<size_t>(start)],
                    vertices[static_cast<size_t>(end)],
                    curve->FirstParameter(),
                    curve->LastParameter());

                if (maker.IsDone())
                {
                    edge = maker.Edge();
                }
            }

            if (edge.IsNull())
            {
                // The named vertices did not sit on the curve within tolerance, so the curve is
                // trusted over the table and brings its own ends. Sewing merges them afterwards.
                BRepBuilderAPI_MakeEdge maker(curve);
                edge = maker.IsDone() ? maker.Edge() : TopoDS_Edge();
            }

            into.push_back(edge);
        }

        return SPARK_OK;
    }

    // Builds one face bounded by its loops, leaving `into` null if the loops did not produce a
    // usable wire. `ShapeFix_Face` is what computes the parameter-space curves Spark does not
    // carry, and what orients the wires relative to each other.
    spark_status face_from_loops(
        const spark_model_desc& desc,
        int32_t faceIndex,
        int32_t firstLoop,
        int32_t loopCount,
        const Patch& patch,
        const std::vector<TopoDS_Edge>& edges,
        double tolerance,
        TopoDS_Face& into)
    {
        BRep_Builder builder;
        TopoDS_Face shell;
        builder.MakeFace(shell, patch.surface, tolerance);

        int32_t wires = 0;

        for (int32_t l = firstLoop; l < firstLoop + loopCount; l++)
        {
            if (l < 0 || l >= desc.loop_count)
            {
                return fail(
                    SPARK_ERR_ARGUMENT,
                    "Face " + std::to_string(faceIndex) + " names loop " + std::to_string(l)
                        + ", which does not exist.");
            }

            const int32_t firstTrim = desc.loops[(l * 3) + 0];
            const int32_t trimCount = desc.loops[(l * 3) + 1];

            if (trimCount <= 0 || desc.trims == nullptr)
            {
                continue;
            }

            BRepBuilderAPI_MakeWire wire;
            bool usable = true;

            for (int32_t t = firstTrim; t < firstTrim + trimCount; t++)
            {
                if (t < 0 || t >= desc.trim_count)
                {
                    return fail(
                        SPARK_ERR_ARGUMENT,
                        "Loop " + std::to_string(l) + " names trim " + std::to_string(t)
                            + ", which does not exist.");
                }

                const int32_t edgeIndex = desc.trims[(t * 2) + 0];
                const bool backwards = desc.trims[(t * 2) + 1] != 0;

                if (edgeIndex < 0 || edgeIndex >= static_cast<int>(edges.size())
                    || edges[static_cast<size_t>(edgeIndex)].IsNull())
                {
                    usable = false;
                    break;
                }

                TopoDS_Edge edge = edges[static_cast<size_t>(edgeIndex)];

                if (backwards)
                {
                    edge.Reverse();
                }

                wire.Add(edge);

                if (!wire.IsDone())
                {
                    usable = false;
                    break;
                }
            }

            if (!usable || !wire.IsDone())
            {
                return SPARK_OK;
            }

            builder.Add(shell, wire.Wire());
            wires++;
        }

        if (wires == 0)
        {
            return SPARK_OK;
        }

        // This is the call that earns the whole approach: it projects each edge onto the surface
        // to make the pcurve OpenCascade needs, decides which wire is outer, and orients the rest
        // against it. Without it the face has wires and no parameter space, and every algorithm
        // downstream refuses it.
        ShapeFix_Face fix(shell);
        fix.SetPrecision(tolerance);
        fix.FixOrientationMode() = 1;
        fix.FixMissingSeamMode() = 1;
        fix.Perform();

        TopoDS_Face fixed = fix.Face();

        if (!fixed.IsNull())
        {
            into = fixed;
        }

        return SPARK_OK;
    }

    bool tables_present(const spark_model_desc& desc, std::string& why)
    {
        if (desc.curve_count > 0
            && (desc.curve_kinds == nullptr || desc.curve_int_offsets == nullptr
                || desc.curve_double_offsets == nullptr))
        {
            why = "The model says it has curves and did not bring their tables.";
            return false;
        }

        if (desc.surface_count > 0
            && (desc.surface_kinds == nullptr || desc.surface_int_offsets == nullptr
                || desc.surface_double_offsets == nullptr))
        {
            why = "The model says it has surfaces and did not bring their tables.";
            return false;
        }

        if (desc.face_count > 0 && desc.faces == nullptr)
        {
            why = "The model says it has faces and did not bring the face table.";
            return false;
        }

        return true;
    }
}

namespace spark
{
    spark_status build_shape(const spark_model_desc& desc, double tolerance, TopoDS_Shape& into)
    {
        std::string why;

        if (!tables_present(desc, why))
        {
            return fail(SPARK_ERR_ARGUMENT, why);
        }

        if (desc.face_count <= 0)
        {
            return fail(SPARK_ERR_ARGUMENT, "A shape needs at least one face.");
        }

        const double fuzz = tolerance > 0.0 ? tolerance : 1.0e-6;

        // Vertices and edges are built ONCE and shared between the faces that use them. Building
        // them per face would give every wire its own vertices at the same coordinates, and
        // whether those merged again would then be a question about tolerances rather than about
        // topology — which is the whole thing an index-based BRep exists to avoid.
        std::vector<TopoDS_Edge> edges;
        const spark_status built_edges = build_edges(desc, fuzz, edges);

        if (built_edges != SPARK_OK)
        {
            return built_edges;
        }

        BRepBuilderAPI_Sewing sewing(fuzz);
        int32_t sewn = 0;

        for (int32_t i = 0; i < desc.face_count; i++)
        {
            const int32_t surface_index = desc.faces[(i * 4) + 0];
            const bool reversed = desc.faces[(i * 4) + 3] != 0;

            if (surface_index < 0 || surface_index >= desc.surface_count)
            {
                return fail(
                    SPARK_ERR_ARGUMENT,
                    "Face " + std::to_string(i) + " names surface " + std::to_string(surface_index)
                        + ", which does not exist.");
            }

            Patch patch;
            const bool made = make_surface(
                desc.surface_kinds[surface_index],
                desc.surface_ints + desc.surface_int_offsets[surface_index],
                span(desc.surface_int_offsets, surface_index),
                desc.surface_doubles + desc.surface_double_offsets[surface_index],
                span(desc.surface_double_offsets, surface_index),
                patch);

            if (!made)
            {
                return fail(
                    SPARK_ERR_UNSUPPORTED,
                    "Surface " + std::to_string(surface_index) + " is of a kind ("
                        + std::to_string(desc.surface_kinds[surface_index])
                        + ") this build cannot rebuild, or its data did not describe one.");
            }

            const int32_t firstLoop = desc.faces[(i * 4) + 1];
            const int32_t loopCount = desc.faces[(i * 4) + 2];

            TopoDS_Face built;
            bool fromDomain = false;

            if (loopCount > 0 && desc.loops != nullptr)
            {
                const spark_status bounded =
                    face_from_loops(desc, i, firstLoop, loopCount, patch, edges, fuzz, built);

                if (bounded != SPARK_OK)
                {
                    return bounded;
                }
            }

            if (built.IsNull())
            {
                // No loops, or none that closed: the face is its surface's own rectangle. That is
                // what an untrimmed patch means, and it is the only thing a lone NURBS sheet can
                // be.
                BRepBuilderAPI_MakeFace face(patch.surface, patch.u0, patch.u1, patch.v0, patch.v1, fuzz);

                if (!face.IsDone())
                {
                    return fail(
                        SPARK_ERR_REFUSED,
                        "Face " + std::to_string(i) + " could not be built on its surface.");
                }

                built = face.Face();
                fromDomain = true;
            }

            // `IsReversed` is applied ONLY to a face bounded by its surface's own domain. A face
            // built from its loops already carries the answer: Spark winds a loop anticlockwise
            // seen from outside the solid, so a reversed face's wire runs clockwise in the
            // surface's parameter space, and that is precisely what ShapeFix_Face reads when it
            // decides which way the face points. Applying the flag on top of that flips it twice.
            if (reversed && !fromDomain)
            {
                built.Reverse();
            }

            sewing.Add(built);
            sewn++;
        }

        if (sewn == 1)
        {
            // One face sews into itself and comes back as the face, which is right — a single
            // untrimmed patch is a sheet, not a solid, and pretending otherwise here would hide
            // the difference from the caller who asked for it.
            sewing.Perform();
            into = sewing.SewedShape();

            if (into.IsNull())
            {
                return fail(SPARK_ERR_REFUSED, "The one face did not survive sewing.");
            }

            return SPARK_OK;
        }

        sewing.Perform();
        TopoDS_Shape result = sewing.SewedShape();

        if (result.IsNull())
        {
            return fail(SPARK_ERR_REFUSED, "The faces did not sew into a shape.");
        }

        if (result.ShapeType() == TopAbs_SHELL && TopoDS::Shell(result).Closed())
        {
            BRepBuilderAPI_MakeSolid solid(TopoDS::Shell(result));

            if (solid.IsDone())
            {
                // WHICH SIDE IS INSIDE IS DECIDED HERE, ONCE, BY ASKING — and the faces are
                // turned, not just the flag on the container. Sewing orients a shell consistently
                // and picks the global sign arbitrarily; with the faces built from their surfaces'
                // domains it happened to come out right, and with the faces built from their loops
                // it came out inverted. Every imported box then measured -24.
                //
                // `BRepLib::OrientClosedSolid` was the first attempt and is not enough: it flips
                // the *solid's* orientation flag, which is enough to mesh correctly and not enough
                // for the boolean operators, which then answered questions about the complement —
                // a union of two 24-unit boxes came back as 50 and a difference removed material
                // that was never inside. `ShapeFix_Solid` reverses the faces themselves.
                Handle(ShapeFix_Solid) fix = new ShapeFix_Solid(TopoDS::Solid(solid.Shape()));
                fix->SetPrecision(fuzz);
                fix->FixShellOrientationMode() = 1;
                fix->Perform();

                const TopoDS_Shape fixed = fix->Shape();
                result = fixed.IsNull() ? solid.Shape() : fixed;
            }
        }

        into = result;
        return SPARK_OK;
    }

    spark_status build_wires(
        const spark_model_desc& desc, double tolerance, std::vector<TopoDS_Shape>& into)
    {
        std::string why;

        if (!tables_present(desc, why))
        {
            return fail(SPARK_ERR_ARGUMENT, why);
        }

        if (desc.curve_count <= 0)
        {
            return fail(SPARK_ERR_ARGUMENT, "A profile needs at least one curve.");
        }

        std::vector<TopoDS_Edge> edges;
        const spark_status built = build_edges(desc, tolerance > 0.0 ? tolerance : 1.0e-6, edges);

        if (built != SPARK_OK)
        {
            return built;
        }

        // A PROFILE IS A WIRE, AND THE LOOP TABLE IS HOW IT SAYS SO. One curve per wire was the
        // first version and it forced a polycurve through an *interpolating* NURBS conversion —
        // an approximation, for a shape whose pieces were all exactly representable. The
        // encoding already had a way to group edges into a circuit; it was simply not being read.
        if (desc.loop_count > 0 && desc.loops != nullptr && desc.trims != nullptr)
        {
            for (int32_t l = 0; l < desc.loop_count; l++)
            {
                const int32_t firstTrim = desc.loops[(l * 3) + 0];
                const int32_t trimCount = desc.loops[(l * 3) + 1];

                if (trimCount <= 0)
                {
                    continue;
                }

                BRepBuilderAPI_MakeWire wire;

                for (int32_t t = firstTrim; t < firstTrim + trimCount; t++)
                {
                    if (t < 0 || t >= desc.trim_count)
                    {
                        return fail(
                            SPARK_ERR_ARGUMENT,
                            "Loop " + std::to_string(l) + " names trim " + std::to_string(t)
                                + ", which does not exist.");
                    }

                    const int32_t edgeIndex = desc.trims[(t * 2) + 0];
                    const bool backwards = desc.trims[(t * 2) + 1] != 0;

                    if (edgeIndex < 0 || edgeIndex >= static_cast<int32_t>(edges.size())
                        || edges[static_cast<size_t>(edgeIndex)].IsNull())
                    {
                        return fail(
                            SPARK_ERR_REFUSED,
                            "Loop " + std::to_string(l) + " names an edge that could not be built.");
                    }

                    TopoDS_Edge edge = edges[static_cast<size_t>(edgeIndex)];

                    if (backwards)
                    {
                        edge.Reverse();
                    }

                    wire.Add(edge);
                }

                if (!wire.IsDone())
                {
                    return fail(
                        SPARK_ERR_REFUSED,
                        "Loop " + std::to_string(l) + "'s edges do not form a connected wire.");
                }

                into.push_back(wire.Wire());
            }

            if (!into.empty())
            {
                return SPARK_OK;
            }
        }

        // No loops: every curve is its own wire, which is what a single-curve profile means.
        for (size_t i = 0; i < edges.size(); i++)
        {
            if (edges[i].IsNull())
            {
                return fail(
                    SPARK_ERR_REFUSED, "Curve " + std::to_string(i) + " made no edge.");
            }

            BRepBuilderAPI_MakeWire wire(edges[i]);

            if (!wire.IsDone())
            {
                return fail(SPARK_ERR_REFUSED, "Curve " + std::to_string(i) + " made no wire.");
            }

            into.push_back(wire.Wire());
        }

        return SPARK_OK;
    }
}
