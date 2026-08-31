// spark_occt — the library, the handles, and every operation that is one OpenCascade call.
//
// Copyright (c) Spark contributors. MIT.
//
// Import lives in spark_occt_import.cpp and reading back in spark_occt_read.cpp, because those
// two are the interesting halves and burying them here would hide them.

#define SPARK_OCCT_BUILD 1

#include "spark_occt_internal.hpp"

#include <BRepAlgoAPI_BooleanOperation.hxx>
#include <BRepBndLib.hxx>
#include <BRepAlgoAPI_Common.hxx>
#include <BRepAlgoAPI_Splitter.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepBuilderAPI_Sewing.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepClass3d_SolidClassifier.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepFilletAPI_MakeChamfer.hxx>
#include <BRepFilletAPI_MakeFillet.hxx>
#include <BRepLib.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepFill_Filling.hxx>
#include <BRepOffsetAPI_DraftAngle.hxx>
#include <BRepOffsetAPI_MakePipe.hxx>
#include <BRepOffsetAPI_MakeOffsetShape.hxx>
#include <BRepOffsetAPI_MakeThickSolid.hxx>
#include <BRepOffsetAPI_ThruSections.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeCone.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakePrism.hxx>
#include <BRepPrimAPI_MakeRevol.hxx>
#include <BRepPrimAPI_MakeSphere.hxx>
#include <BRepPrimAPI_MakeTorus.hxx>
#include <BRepTools.hxx>
#include <BRep_Tool.hxx>
#include <GeomLProp_SLProps.hxx>
#include <IGESControl_Controller.hxx>
#include <IGESControl_Reader.hxx>
#include <IGESControl_Writer.hxx>
#include <Geom_ConicalSurface.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <Geom_Plane.hxx>
#include <Geom_RectangularTrimmedSurface.hxx>
#include <Geom_Surface.hxx>
#include <BRepTools.hxx>
#include <Message.hxx>
#include <Message_Alert.hxx>
#include <Message_Gravity.hxx>
#include <Message_ListOfAlert.hxx>
#include <Message_Report.hxx>
#include <Message_Messenger.hxx>
#include <Message_PrinterOStream.hxx>
#include <OSD.hxx>
#include <STEPControl_Reader.hxx>
#include <STEPControl_Writer.hxx>
#include <ShapeFix_Shape.hxx>
#include <ShapeFix_Solid.hxx>
#include <Standard_Failure.hxx>
#include <Standard_Version.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Face.hxx>
#include <BRep_Builder.hxx>
#include <TopLoc_Location.hxx>
#include <TopoDS_Shell.hxx>
#include <TopoDS_Solid.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Iterator.hxx>
#include <TopoDS_Wire.hxx>
#include <gp.hxx>
#include <gp_Ax1.hxx>
#include <gp_Pln.hxx>
#include <gp_Dir.hxx>
#include <gp_Trsf.hxx>
#include <gp_Vec.hxx>

#include <Bnd_Box.hxx>

#include <cmath>
#include <cstring>
#include <exception>
#include <new>
#include <string>
#include <vector>

namespace
{
    // Thread-local, because two graph nodes may be evaluating in parallel and a shared last-error
    // would let one node report the other's refusal. See ADR-0021 on residency and threading.
    thread_local std::string g_error;

    int32_t copy_out(const std::string& text, char* buffer, int32_t capacity)
    {
        const int32_t needed = static_cast<int32_t>(text.size()) + 1;

        if (buffer != nullptr && capacity > 0)
        {
            const int32_t room = capacity - 1 < needed - 1 ? capacity - 1 : needed - 1;
            std::memcpy(buffer, text.c_str(), static_cast<size_t>(room));
            buffer[room] = '\0';
        }

        return needed;
    }

    // OPENCASCADE INSTALLS SIGNAL HANDLERS AND THE CLR HAS ITS OWN. `OSD::SetSignal(false)` tells
    // OpenCascade not to, which is R19's mitigation and has to happen before the first call rather
    // than at some convenient later point — so it is a function-local static, initialised on first
    // use, thread-safe by the standard, and impossible to forget at a call site.
    struct Startup
    {
        Startup()
        {
            OSD::SetSignal(false);

            // AND OPENCASCADE STOPS PRINTING. Its default messenger writes progress to `cout` —
            // "** WorkSession : Sending all data", a transfer banner per shape — which lands in
            // the middle of `spark export`'s own output and makes it undiffable. A library
            // reached through a C ABI has no business owning the caller's stdout; what it has to
            // say comes back through `spark_occt_last_error`.
            Message::DefaultMessenger()->RemovePrinters(STANDARD_TYPE(Message_PrinterOStream));
        }
    };

    void ensure_started()
    {
        static const Startup once;
        (void)once;
    }

    bool positive(double value)
    {
        return value > 0.0 && value == value && value * 0.0 == 0.0;
    }
}

namespace spark
{
    spark_status fail(spark_status status, const std::string& message)
    {
        g_error = message;
        return status;
    }

    void clear_error()
    {
        g_error.clear();
    }

    spark_status guard(const char* what, const std::function<spark_status()>& body)
    {
        ensure_started();
        clear_error();

        try
        {
            return body();
        }
        catch (const Standard_Failure& failure)
        {
            // what() rather than the deprecated GetMessageString(): OpenCascade 8.0 derives
            // Standard_Failure from std::exception, so the standard spelling is the
            // supported one now and never returns null.
            const char* detail = failure.what();
            return fail(
                SPARK_ERR_EXCEPTION,
                std::string(what) + " raised inside OpenCascade: "
                    + (detail != nullptr && *detail != '\0' ? detail : "no message given"));
        }
        catch (const std::bad_alloc&)
        {
            return fail(SPARK_ERR_EXCEPTION, std::string(what) + " ran out of memory.");
        }
        catch (const std::exception& error)
        {
            return fail(SPARK_ERR_EXCEPTION, std::string(what) + " failed: " + error.what());
        }
        catch (...)
        {
            return fail(SPARK_ERR_EXCEPTION, std::string(what) + " failed for an unknown reason.");
        }
    }

    gp_Ax3 frame_to_ax3(const double* frame)
    {
        const gp_Pnt origin(frame[0], frame[1], frame[2]);
        const gp_Dir x(frame[3], frame[4], frame[5]);
        const gp_Dir y(frame[6], frame[7], frame[8]);
        const gp_Dir z = x.Crossed(y);

        return gp_Ax3(origin, z, x);
    }

    bool ax3_to_frame(const gp_Ax3& axes, double* frame)
    {
        const gp_Pnt& origin = axes.Location();
        const gp_Dir& x = axes.XDirection();
        const gp_Dir& y = axes.YDirection();

        frame[0] = origin.X();
        frame[1] = origin.Y();
        frame[2] = origin.Z();
        frame[3] = x.X();
        frame[4] = x.Y();
        frame[5] = x.Z();

        const bool direct = axes.Direct();
        const double sign = direct ? 1.0 : -1.0;

        frame[6] = sign * y.X();
        frame[7] = sign * y.Y();
        frame[8] = sign * y.Z();

        return direct;
    }

    gp_Pnt point(const double* xyz)
    {
        return gp_Pnt(xyz[0], xyz[1], xyz[2]);
    }

    spark_status emit(const TopoDS_Shape& shape, spark_shape** out)
    {
        if (shape.IsNull())
        {
            return fail(SPARK_ERR_REFUSED, "The operation produced no shape.");
        }

        spark_shape* handle = new spark_shape();
        handle->shape = shape;
        *out = handle;

        return SPARK_OK;
    }
}

using spark::clear_error;
using spark::emit;
using spark::fail;
using spark::frame_to_ax3;
using spark::guard;

namespace
{
    // A primitive is built at the origin and moved onto its frame, rather than built in place.
    // BRepPrimAPI's frame-taking overloads exist, but they take a gp_Ax2 whose y-axis is derived,
    // so a caller's y would be silently replaced. Transforming preserves it.
    TopoDS_Shape placed(const TopoDS_Shape& shape, const double* frame)
    {
        gp_Trsf move;
        move.SetTransformation(frame_to_ax3(frame), gp_Ax3());

        return BRepBuilderAPI_Transform(shape, move, true).Shape();
    }

    // An algorithm that did not finish yields a null shape rather than raising. OpenCascade's
    // `Shape()` throws StdFail_NotDone, whose message is "BRep_API: command not done" and says
    // nothing about which command; a null shape reaches `emit`, which refuses by name.
    TopoDS_Shape finished(BRepBuilderAPI_MakeShape& maker)
    {
        return maker.IsDone() ? maker.Shape() : TopoDS_Shape();
    }

    // Caps a profile, but only if it closes. An OPEN wire handed to BRepBuilderAPI_MakeFace can
    // come back "done" and unusable, and the failure then surfaces two calls later inside the
    // sweep with a message about the wrong thing entirely.
    TopoDS_Shape capped(const TopoDS_Shape& wire)
    {
        if (wire.ShapeType() != TopAbs_WIRE || !BRep_Tool::IsClosed(wire))
        {
            return wire;
        }

        BRepBuilderAPI_MakeFace face(TopoDS::Wire(wire), true);

        return face.IsDone() ? face.Shape() : wire;
    }

    // Whatever an OpenCascade algorithm accumulated while failing.
    //
    // R16 is the risk that a boolean returning a wrong-but-valid shape is diagnosable only inside
    // code we do not own. This is the cheap third of the mitigation: the algorithm's own alerts,
    // by key, appended to the message the caller gets. It is not a translation of every alert
    // type - the keys are OpenCascade's own and are meant for its developers - and it is
    // deliberately better than "the operation did not complete".
    std::string report_text(const Handle(Message_Report)& report)
    {
        if (report.IsNull())
        {
            return std::string();
        }

        std::string text;

        const Message_Gravity levels[2] = { Message_Fail, Message_Warning };

        for (int level = 0; level < 2; level++)
        {
            const auto& alerts = report->GetAlerts(levels[level]);

            for (auto it = alerts.cbegin(); it != alerts.cend(); ++it)
            {
                const Handle(Message_Alert)& alert = *it;

                if (alert.IsNull())
                {
                    continue;
                }

                const char* key = alert->GetMessageKey();

                if (key != nullptr && *key != '\0')
                {
                    text += text.empty() ? " The kernel reported: " : ", ";
                    text += key;
                }
            }
        }

        return text.empty() ? text : text + ".";
    }

    spark_status require_shape(const spark_shape* shape, const char* name)
    {
        if (shape == nullptr || shape->shape.IsNull())
        {
            return fail(SPARK_ERR_ARGUMENT, std::string("The ") + name + " shape is missing.");
        }

        return SPARK_OK;
    }

    // Selects sub-shapes by index into the same map spark_occt_read numbers them with, so that an
    // edge index a caller took from a read is the edge it meant. An empty list means all of them.
    spark_status select(
        const TopoDS_Shape& shape,
        TopAbs_ShapeEnum kind,
        const int32_t* indices,
        int32_t count,
        const char* what,
        SparkShapeList& into)
    {
        SparkShapeMap map;

        if (kind == TopAbs_FACE)
        {
            spark::ordered_faces(shape, map);
        }
        else
        {
            spark::ordered_edges(shape, map);
        }

        if (count <= 0)
        {
            for (int i = 1; i <= map.Extent(); i++)
            {
                into.Append(map(i));
            }

            return SPARK_OK;
        }

        if (indices == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, std::string("A ") + what + " count was given with no list.");
        }

        for (int32_t i = 0; i < count; i++)
        {
            const int32_t index = indices[i];

            if (index < 0 || index >= map.Extent())
            {
                return fail(
                    SPARK_ERR_ARGUMENT,
                    std::string("There is no ") + what + " " + std::to_string(index) + "; the shape has "
                        + std::to_string(map.Extent()) + ".");
            }

            into.Append(map(index + 1));
        }

        return SPARK_OK;
    }
}

// ------------------------------------------------------------------------------------------------
// Library
// ------------------------------------------------------------------------------------------------

extern "C" int32_t SPARK_OCCT_CALL spark_occt_abi_version(void)
{
    ensure_started();

    return SPARK_OCCT_ABI;
}

extern "C" int32_t SPARK_OCCT_CALL spark_occt_engine_version(char* buffer, int32_t capacity)
{
    return copy_out(std::string("OpenCascade ") + OCC_VERSION_COMPLETE, buffer, capacity);
}

extern "C" int32_t SPARK_OCCT_CALL spark_occt_last_error(char* buffer, int32_t capacity)
{
    return copy_out(g_error, buffer, capacity);
}

// ------------------------------------------------------------------------------------------------
// Shapes
// ------------------------------------------------------------------------------------------------

extern "C" void SPARK_OCCT_CALL spark_occt_shape_release(spark_shape* shape)
{
    // A destructor is the one place an exception is least expected and most damaging: this is a
    // `void` entry point, so there is nowhere to report from, and an exception leaving it would
    // unwind into a managed finalizer thread.
    try
    {
        delete shape;
    }
    catch (...)
    {
    }
}

extern "C" int64_t SPARK_OCCT_CALL spark_occt_shape_bytes(const spark_shape* shape)
{
    ensure_started();

    if (shape == nullptr || shape->shape.IsNull())
    {
        return 0;
    }

    // It walks the shape, so it can raise, and it has no status to report through. Zero is a
    // legal answer from a provider that cannot say (see the header), so a failure gives one.
    try
    {
        // An estimate, and the header says so. Counting the sub-shapes and charging a fixed price per
        // one tracks the real figure closely enough for a cache to evict by, and is O(n) rather than a
        // traversal of every curve's poles. ADR-0021 asks for a number that grows with the shape.
        int64_t total = 0;

        for (TopExp_Explorer it(shape->shape, TopAbs_FACE); it.More(); it.Next())
        {
            total += 2048;
        }

        for (TopExp_Explorer it(shape->shape, TopAbs_EDGE); it.More(); it.Next())
        {
            total += 512;
        }

        for (TopExp_Explorer it(shape->shape, TopAbs_VERTEX); it.More(); it.Next())
        {
            total += 128;
        }

        for (TopExp_Explorer it(shape->shape, TopAbs_FACE); it.More(); it.Next())
        {
            TopLoc_Location location;
            const Handle(Poly_Triangulation) mesh = BRep_Tool::Triangulation(TopoDS::Face(it.Value()), location);

            if (!mesh.IsNull())
            {
                total += static_cast<int64_t>(mesh->NbNodes()) * 24;
                total += static_cast<int64_t>(mesh->NbTriangles()) * 12;
            }
        }

        return total;
    }
    catch (...)
    {
        return 0;
    }
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_shape_counts(
    const spark_shape* shape, int32_t* counts)
{
    return guard("Counting a shape", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "counted");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (counts == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "Counting a shape needs somewhere to put the counts.");
        }

        // Faces and edges are counted through the canonical orderings rather than through a raw
        // map, so that a count and an index into a read model agree. They differ: a degenerate
        // edge is in the map and is not an edge Spark can name.
        SparkShapeMap shells;
        TopExp::MapShapes(shape->shape, TopAbs_SHELL, shells);

        SparkShapeMap faces;
        spark::ordered_faces(shape->shape, faces);

        SparkShapeMap edges;
        spark::ordered_edges(shape->shape, edges);

        SparkShapeMap vertices;
        TopExp::MapShapes(shape->shape, TopAbs_VERTEX, vertices);

        counts[0] = shells.Extent();
        counts[1] = faces.Extent();
        counts[2] = edges.Extent();
        counts[3] = vertices.Extent();

        return SPARK_OK;
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_dump_brep(
    const spark_shape* shape, const char* path)
{
    return guard("Dumping a shape", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "dumped");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (path == nullptr || *path == '\0')
        {
            return fail(SPARK_ERR_ARGUMENT, "A dump needs a path.");
        }

        return BRepTools::Write(shape->shape, path)
            ? SPARK_OK
            : fail(SPARK_ERR_REFUSED, std::string("The shape could not be written to ") + path + ".");
    });
}

extern "C" int32_t SPARK_OCCT_CALL spark_occt_check(
    const spark_shape* shape, char* buffer, int32_t capacity)
{
    ensure_started();

    std::string text;

    try
    {
        if (shape != nullptr && !shape->shape.IsNull())
        {
            const BRepCheck_Analyzer analyzer(shape->shape);

            if (!analyzer.IsValid())
            {
                text = "The shape is not valid.";

                // Which *kind* of sub-shape is bad narrows a bug report from "somewhere" to "an
                // edge", and costs one traversal.
                const TopAbs_ShapeEnum kinds[4] =
                    { TopAbs_FACE, TopAbs_EDGE, TopAbs_VERTEX, TopAbs_WIRE };
                const char* names[4] = { "face", "edge", "vertex", "wire" };

                for (int i = 0; i < 4; i++)
                {
                    int bad = 0;

                    for (TopExp_Explorer it(shape->shape, kinds[i]); it.More(); it.Next())
                    {
                        if (!analyzer.IsValid(it.Value()))
                        {
                            bad++;
                        }
                    }

                    if (bad > 0)
                    {
                        text += " " + std::to_string(bad) + " bad " + names[i]
                            + (bad == 1 ? "." : "s.");
                    }
                }
            }
        }
    }
    catch (...)
    {
        text = "The validity check itself raised, which is a finding of its own.";
    }

    return copy_out(text, buffer, capacity);
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_shape_is_solid(
    const spark_shape* shape, int32_t* out_solid)
{
    return guard("Asking whether a shape is a solid", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "tested");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out_solid == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "The answer needs somewhere to go.");
        }

        TopExp_Explorer solids(shape->shape, TopAbs_SOLID);
        *out_solid = solids.More() && BRepCheck_Analyzer(shape->shape).IsValid() ? 1 : 0;

        return SPARK_OK;
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_shape_contains(
    const spark_shape* shape, const double* point, double tolerance, int32_t* out_inside)
{
    return guard("Asking whether a point is inside a shape", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "tested");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (point == nullptr || out_inside == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "The question needs a point and somewhere for the answer.");
        }

        BRepClass3d_SolidClassifier classifier(shape->shape);
        classifier.Perform(spark::point(point), tolerance > 0.0 ? tolerance : 1.0e-7);

        const TopAbs_State where = classifier.State();
        *out_inside = where == TopAbs_IN || where == TopAbs_ON ? 1 : 0;

        return SPARK_OK;
    });
}

namespace
{
    // The top-level pieces of a shape. A compound has its children; anything else is itself, which
    // is what makes `part_count` answer 1 rather than 0 for an ordinary solid and lets a caller
    // walk every result the same way.
    void parts(const TopoDS_Shape& shape, std::vector<TopoDS_Shape>& into)
    {
        if (shape.ShapeType() != TopAbs_COMPOUND)
        {
            into.push_back(shape);
            return;
        }

        for (TopoDS_Iterator it(shape); it.More(); it.Next())
        {
            into.push_back(it.Value());
        }
    }
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_shape_part_count(
    const spark_shape* shape, int32_t* out_count)
{
    return guard("Counting a shape's pieces", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "counted");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out_count == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "The count needs somewhere to go.");
        }

        std::vector<TopoDS_Shape> pieces;
        parts(shape->shape, pieces);
        *out_count = static_cast<int32_t>(pieces.size());

        return SPARK_OK;
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_shape_part(
    const spark_shape* shape, int32_t index, spark_shape** out)
{
    return guard("Taking a shape's piece", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "taken from");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "The piece needs somewhere to go.");
        }

        std::vector<TopoDS_Shape> pieces;
        parts(shape->shape, pieces);

        if (index < 0 || index >= static_cast<int32_t>(pieces.size()))
        {
            return fail(
                SPARK_ERR_ARGUMENT,
                "There is no piece " + std::to_string(index) + "; the shape has "
                    + std::to_string(pieces.size()) + ".");
        }

        return emit(pieces[static_cast<size_t>(index)], out);
    });
}

// ------------------------------------------------------------------------------------------------
// Construction
// ------------------------------------------------------------------------------------------------

extern "C" spark_status SPARK_OCCT_CALL spark_occt_make_box(
    const double* frame, double length, double width, double height, spark_shape** out)
{
    return guard("Making a box", [&]() -> spark_status
    {
        if (frame == nullptr || out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A box needs a frame and somewhere to go.");
        }

        if (!positive(length) || !positive(width) || !positive(height))
        {
            return fail(SPARK_ERR_ARGUMENT, "A box's three sizes must all be positive and finite.");
        }

        return emit(placed(BRepPrimAPI_MakeBox(length, width, height).Shape(), frame), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_make_cylinder(
    const double* frame, double radius, double height, spark_shape** out)
{
    return guard("Making a cylinder", [&]() -> spark_status
    {
        if (frame == nullptr || out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A cylinder needs a frame and somewhere to go.");
        }

        if (!positive(radius) || !positive(height))
        {
            return fail(SPARK_ERR_ARGUMENT, "A cylinder's radius and height must be positive and finite.");
        }

        return emit(placed(BRepPrimAPI_MakeCylinder(radius, height).Shape(), frame), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_make_sphere(
    const double* frame, double radius, spark_shape** out)
{
    return guard("Making a sphere", [&]() -> spark_status
    {
        if (frame == nullptr || out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A sphere needs a frame and somewhere to go.");
        }

        if (!positive(radius))
        {
            return fail(SPARK_ERR_ARGUMENT, "A sphere's radius must be positive and finite.");
        }

        return emit(placed(BRepPrimAPI_MakeSphere(radius).Shape(), frame), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_make_cone(
    const double* frame, double bottom_radius, double top_radius, double height, spark_shape** out)
{
    return guard("Making a cone", [&]() -> spark_status
    {
        if (frame == nullptr || out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A cone needs a frame and somewhere to go.");
        }

        if (!positive(height) || bottom_radius < 0.0 || top_radius < 0.0
            || (bottom_radius == 0.0 && top_radius == 0.0))
        {
            return fail(
                SPARK_ERR_ARGUMENT,
                "A cone's height must be positive and at least one of its radii must be.");
        }

        return emit(
            placed(BRepPrimAPI_MakeCone(bottom_radius, top_radius, height).Shape(), frame), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_make_torus(
    const double* frame, double major_radius, double minor_radius, spark_shape** out)
{
    return guard("Making a torus", [&]() -> spark_status
    {
        if (frame == nullptr || out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A torus needs a frame and somewhere to go.");
        }

        if (!positive(major_radius) || !positive(minor_radius))
        {
            return fail(SPARK_ERR_ARGUMENT, "A torus's two radii must be positive and finite.");
        }

        return emit(placed(BRepPrimAPI_MakeTorus(major_radius, minor_radius).Shape(), frame), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_import(
    const spark_model_desc* model, double tolerance, spark_shape** out)
{
    return guard("Importing a shape", [&]() -> spark_status
    {
        if (model == nullptr || out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "An import needs a model and somewhere to go.");
        }

        TopoDS_Shape built;
        const spark_status status = spark::build_shape(*model, tolerance, built);

        if (status != SPARK_OK)
        {
            return status;
        }

        return emit(built, out);
    });
}

// ------------------------------------------------------------------------------------------------
// Operations
// ------------------------------------------------------------------------------------------------

extern "C" spark_status SPARK_OCCT_CALL spark_occt_boolean(
    int32_t operation,
    const spark_shape* first,
    const spark_shape* second,
    double tolerance,
    spark_shape** out)
{
    return guard("A boolean", [&]() -> spark_status
    {
        spark_status ready = require_shape(first, "first");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        ready = require_shape(second, "second");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A boolean needs somewhere to put its result.");
        }

        SparkShapeList arguments;
        arguments.Append(first->shape);

        SparkShapeList tools;
        tools.Append(second->shape);

        BRepAlgoAPI_BooleanOperation* algorithm = nullptr;
        BRepAlgoAPI_Fuse fuse;
        BRepAlgoAPI_Cut cut;
        BRepAlgoAPI_Common common;

        switch (operation)
        {
            case SPARK_BOOLEAN_UNION: algorithm = &fuse; break;
            case SPARK_BOOLEAN_DIFFERENCE: algorithm = &cut; break;
            case SPARK_BOOLEAN_INTERSECTION: algorithm = &common; break;
            default:
                return fail(
                    SPARK_ERR_ARGUMENT,
                    "There is no boolean operation " + std::to_string(operation) + ".");
        }

        algorithm->SetArguments(arguments);
        algorithm->SetTools(tools);

        if (tolerance > 0.0)
        {
            algorithm->SetFuzzyValue(tolerance);
        }

        algorithm->Build();

        if (!algorithm->IsDone())
        {
            return fail(
                SPARK_ERR_REFUSED,
                "The boolean did not complete on these two shapes." + report_text(algorithm->GetReport()));
        }

        const TopoDS_Shape result = algorithm->Shape();

        if (result.IsNull())
        {
            return fail(SPARK_ERR_REFUSED, "The boolean produced an empty result.");
        }

        // An intersection or a difference may legitimately come back empty — two solids that do
        // not touch — and that is a refusal rather than a failure, said in those words.
        TopExp_Explorer faces(result, TopAbs_FACE);

        if (!faces.More())
        {
            return fail(
                SPARK_ERR_REFUSED,
                "The boolean produced nothing: the two shapes do not overlap in the way it needs.");
        }

        return emit(result, out);
    });
}

namespace
{
    spark_status sweep(
        const spark_model_desc* profile,
        double tolerance,
        const char* what,
        const std::function<TopoDS_Shape(const TopoDS_Shape&)>& make,
        spark_shape** out)
    {
        if (profile == nullptr || out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, std::string(what) + " needs a profile and somewhere to go.");
        }

        std::vector<TopoDS_Shape> wires;
        const spark_status status = spark::build_wires(*profile, tolerance, wires);

        if (status != SPARK_OK)
        {
            return status;
        }

        if (wires.size() != 1)
        {
            return fail(
                SPARK_ERR_ARGUMENT,
                std::string(what) + " takes one profile and was given " + std::to_string(wires.size())
                    + ".");
        }

        return emit(make(wires[0]), out);
    }
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_extrude(
    const spark_model_desc* profile,
    const double* direction,
    int32_t cap,
    double tolerance,
    spark_shape** out)
{
    return guard("An extrusion", [&]() -> spark_status
    {
        if (direction == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "An extrusion needs a direction.");
        }

        const gp_Vec along(direction[0], direction[1], direction[2]);

        if (along.Magnitude() <= gp::Resolution())
        {
            return fail(SPARK_ERR_ARGUMENT, "An extrusion's direction must have a length.");
        }

        return sweep(profile, tolerance, "An extrusion", [&](const TopoDS_Shape& wire)
        {
            const TopoDS_Shape base = cap != 0 ? capped(wire) : wire;
            BRepPrimAPI_MakePrism prism(base, along);

            return finished(prism);
        }, out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_revolve(
    const spark_model_desc* profile,
    const double* axis_origin,
    const double* axis_direction,
    double angle,
    double tolerance,
    spark_shape** out)
{
    return guard("A revolve", [&]() -> spark_status
    {
        if (axis_origin == nullptr || axis_direction == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A revolve needs an axis.");
        }

        const gp_Vec direction(axis_direction[0], axis_direction[1], axis_direction[2]);

        if (direction.Magnitude() <= gp::Resolution())
        {
            return fail(SPARK_ERR_ARGUMENT, "A revolve's axis must have a direction.");
        }

        const gp_Ax1 axis(spark::point(axis_origin), gp_Dir(direction));

        return sweep(profile, tolerance, "A revolve", [&](const TopoDS_Shape& wire)
        {
            BRepPrimAPI_MakeRevol revolve(capped(wire), axis, angle);

            return finished(revolve);
        }, out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_sweep(
    const spark_model_desc* profile,
    const spark_model_desc* rail,
    int32_t cap,
    double tolerance,
    spark_shape** out)
{
    return guard("A sweep", [&]() -> spark_status
    {
        if (profile == nullptr || rail == nullptr || out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A sweep needs a profile, a rail and somewhere to go.");
        }

        std::vector<TopoDS_Shape> profiles;
        spark_status status = spark::build_wires(*profile, tolerance, profiles);

        if (status != SPARK_OK)
        {
            return status;
        }

        std::vector<TopoDS_Shape> rails;
        status = spark::build_wires(*rail, tolerance, rails);

        if (status != SPARK_OK)
        {
            return status;
        }

        if (profiles.size() != 1 || rails.size() != 1)
        {
            return fail(
                SPARK_ERR_ARGUMENT,
                "A sweep takes one profile and one rail, and was given "
                    + std::to_string(profiles.size()) + " and " + std::to_string(rails.size()) + ".");
        }

        const TopoDS_Shape base = cap != 0 ? capped(profiles[0]) : profiles[0];

        BRepOffsetAPI_MakePipe pipe(TopoDS::Wire(rails[0]), base);
        pipe.Build();

        return emit(finished(pipe), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_patch(
    const spark_model_desc* boundary, double tolerance, spark_shape** out)
{
    return guard("A patch", [&]() -> spark_status
    {
        if (boundary == nullptr || out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A patch needs a boundary and somewhere to go.");
        }

        std::vector<TopoDS_Shape> wires;
        const spark_status status = spark::build_wires(*boundary, tolerance, wires);

        if (status != SPARK_OK)
        {
            return status;
        }

        if (wires.empty())
        {
            return fail(SPARK_ERR_ARGUMENT, "A patch needs at least one boundary curve.");
        }

        // BRepFill_Filling takes edges rather than wires: it does not require the boundary to be
        // a single connected circuit, which is the difference between a patch and a face. Every
        // edge of every wire goes in.
        BRepFill_Filling filling;
        int32_t added = 0;

        for (const TopoDS_Shape& wire : wires)
        {
            for (TopExp_Explorer it(wire, TopAbs_EDGE); it.More(); it.Next())
            {
                filling.Add(TopoDS::Edge(it.Value()), GeomAbs_C0);
                added++;
            }
        }

        if (added == 0)
        {
            return fail(SPARK_ERR_ARGUMENT, "The boundary contained no edges.");
        }

        filling.Build();

        if (!filling.IsDone())
        {
            return fail(
                SPARK_ERR_REFUSED,
                "No surface could be fitted to that boundary: " + std::to_string(added)
                    + " edge(s) were given.");
        }

        return emit(filling.Face(), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_loft(
    const spark_model_desc* profiles, int32_t closed, double tolerance, spark_shape** out)
{
    return guard("A loft", [&]() -> spark_status
    {
        if (profiles == nullptr || out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A loft needs profiles and somewhere to go.");
        }

        std::vector<TopoDS_Shape> wires;
        const spark_status status = spark::build_wires(*profiles, tolerance, wires);

        if (status != SPARK_OK)
        {
            return status;
        }

        if (wires.size() < 2)
        {
            return fail(
                SPARK_ERR_ARGUMENT,
                "A loft needs at least two profiles and was given " + std::to_string(wires.size()) + ".");
        }

        BRepOffsetAPI_ThruSections sections(true, false, tolerance > 0.0 ? tolerance : 1.0e-7);

        for (const TopoDS_Shape& wire : wires)
        {
            sections.AddWire(TopoDS::Wire(wire));
        }

        if (closed != 0)
        {
            sections.AddWire(TopoDS::Wire(wires[0]));
        }

        sections.Build();

        if (!sections.IsDone())
        {
            return fail(SPARK_ERR_REFUSED, "The loft could not be matched between these profiles.");
        }

        return emit(sections.Shape(), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_fillet(
    const spark_shape* shape, const int32_t* edges, int32_t edge_count, double radius, spark_shape** out)
{
    return guard("A fillet", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "filleted");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A fillet needs somewhere to go.");
        }

        if (!positive(radius))
        {
            return fail(SPARK_ERR_ARGUMENT, "A fillet radius must be positive and finite.");
        }

        SparkShapeList chosen;
        const spark_status selected = select(shape->shape, TopAbs_EDGE, edges, edge_count, "edge", chosen);

        if (selected != SPARK_OK)
        {
            return selected;
        }

        BRepFilletAPI_MakeFillet fillet(shape->shape);

        for (SparkShapeList::Iterator it(chosen); it.More(); it.Next())
        {
            fillet.Add(radius, TopoDS::Edge(it.Value()));
        }

        fillet.Build();

        if (!fillet.IsDone())
        {
            return fail(
                SPARK_ERR_REFUSED,
                "A fillet of radius " + std::to_string(radius) + " does not fit these edges.");
        }

        return emit(fillet.Shape(), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_chamfer(
    const spark_shape* shape, const int32_t* edges, int32_t edge_count, double distance, spark_shape** out)
{
    return guard("A chamfer", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "chamfered");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A chamfer needs somewhere to go.");
        }

        if (!positive(distance))
        {
            return fail(SPARK_ERR_ARGUMENT, "A chamfer distance must be positive and finite.");
        }

        SparkShapeList chosen;
        const spark_status selected = select(shape->shape, TopAbs_EDGE, edges, edge_count, "edge", chosen);

        if (selected != SPARK_OK)
        {
            return selected;
        }

        BRepFilletAPI_MakeChamfer chamfer(shape->shape);

        for (SparkShapeList::Iterator it(chosen); it.More(); it.Next())
        {
            chamfer.Add(distance, TopoDS::Edge(it.Value()));
        }

        chamfer.Build();

        if (!chamfer.IsDone())
        {
            return fail(
                SPARK_ERR_REFUSED,
                "A chamfer of " + std::to_string(distance) + " does not fit these edges.");
        }

        return emit(chamfer.Shape(), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_shell(
    const spark_shape* shape,
    const int32_t* faces,
    int32_t face_count,
    double thickness,
    double tolerance,
    spark_shape** out)
{
    return guard("Hollowing a solid", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "hollowed");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "Hollowing needs somewhere to go.");
        }

        if (thickness == 0.0 || thickness != thickness)
        {
            return fail(SPARK_ERR_ARGUMENT, "A wall thickness must be a non-zero finite number.");
        }

        // An empty list means *no* face is removed here, unlike the edge lists above where it
        // means all of them. That asymmetry is deliberate and matches what the two operations
        // mean: filleting nothing is a no-op nobody asks for, hollowing nothing is a closed void.
        SparkShapeList openings;

        if (face_count > 0)
        {
            const spark_status selected =
                select(shape->shape, TopAbs_FACE, faces, face_count, "face", openings);

            if (selected != SPARK_OK)
            {
                return selected;
            }
        }

        const double fuzz = tolerance > 0.0 ? tolerance : 1.0e-7;

        BRepOffsetAPI_MakeThickSolid hollow;
        hollow.MakeThickSolidByJoin(shape->shape, openings, thickness, fuzz);

        if (!hollow.IsDone())
        {
            return fail(
                SPARK_ERR_REFUSED,
                "A wall of " + std::to_string(thickness) + " does not fit inside this solid.");
        }

        if (face_count > 0)
        {
            return emit(hollow.Shape(), out);
        }

        // WITH NO FACE OPENED, OpenCascade's thick solid IS THE OFFSET SOLID, not the wall
        // between it and the original — a 4-cube hollowed by 0.5 comes back with volume 27 rather
        // than 37, which is a correct answer to a different question. Subtracting it gives the
        // closed void this ABI documents, and the measurement is what found it: a comment
        // asserting the other behaviour had already been written and was wrong.
        SparkShapeList arguments;
        arguments.Append(shape->shape);

        SparkShapeList tools;
        tools.Append(hollow.Shape());

        BRepAlgoAPI_Cut wall;
        wall.SetArguments(arguments);
        wall.SetTools(tools);
        wall.SetFuzzyValue(fuzz);
        wall.Build();

        if (!wall.IsDone())
        {
            return fail(SPARK_ERR_REFUSED, "The hollow could not be cut out of the solid.");
        }

        return emit(wall.Shape(), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_draft(
    const spark_shape* shape,
    const int32_t* faces,
    int32_t face_count,
    const double* pull_direction,
    double angle,
    const double* neutral_origin,
    const double* neutral_normal,
    spark_shape** out)
{
    return guard("A draft", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "drafted");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out == nullptr || pull_direction == nullptr
            || neutral_origin == nullptr || neutral_normal == nullptr)
        {
            return fail(
                SPARK_ERR_ARGUMENT,
                "A draft needs a pull direction, a neutral plane and somewhere to go.");
        }

        const gp_Vec pull(pull_direction[0], pull_direction[1], pull_direction[2]);
        const gp_Vec normal(neutral_normal[0], neutral_normal[1], neutral_normal[2]);

        if (pull.Magnitude() <= gp::Resolution() || normal.Magnitude() <= gp::Resolution())
        {
            return fail(SPARK_ERR_ARGUMENT, "A draft's directions must have a length.");
        }

        if (angle == 0.0 || angle != angle)
        {
            return fail(SPARK_ERR_ARGUMENT, "A draft angle must be a non-zero finite number.");
        }

        SparkShapeList chosen;
        const spark_status selected = select(shape->shape, TopAbs_FACE, faces, face_count, "face", chosen);

        if (selected != SPARK_OK)
        {
            return selected;
        }

        const gp_Pln neutral(spark::point(neutral_origin), gp_Dir(normal));

        // WHICH FACES CAN BE DRAFTED IS DECIDED BEFORE ASKING, NOT BY ASKING. OpenCascade only
        // tapers planar, cylindrical and conical faces, and a failed `Add` poisons every later
        // one — the next call raises `Standard_ConstructionError` until `Remove` cancels the bad
        // one. Handing it a box's flat top and recovering afterwards turned out to leave the
        // algorithm in a state where `Build` itself raised, with no message. Skipping what cannot
        // be tapered is both simpler and the behaviour a moulder means by "draft this part".
        BRepOffsetAPI_DraftAngle draft(shape->shape);

        const gp_Dir pullDirection(pull);
        int32_t added = 0;
        int32_t skipped = 0;

        for (SparkShapeList::Iterator it(chosen); it.More(); it.Next())
        {
            const TopoDS_Face face = TopoDS::Face(it.Value());
            Handle(Geom_Surface) surface = BRep_Tool::Surface(face);

            while (!surface.IsNull() && surface->IsKind(STANDARD_TYPE(Geom_RectangularTrimmedSurface)))
            {
                surface = Handle(Geom_RectangularTrimmedSurface)::DownCast(surface)->BasisSurface();
            }

            if (surface.IsNull())
            {
                skipped++;
                continue;
            }

            // A plane whose normal is along the pull has no line to tilt about: the neutral plane
            // and the face are parallel. That is a box's top and bottom, and it is the ordinary
            // case rather than an error.
            if (surface->IsKind(STANDARD_TYPE(Geom_Plane)))
            {
                const gp_Dir facing = Handle(Geom_Plane)::DownCast(surface)->Position().Direction();

                if (facing.IsParallel(pullDirection, 1.0e-6))
                {
                    skipped++;
                    continue;
                }
            }
            else if (!surface->IsKind(STANDARD_TYPE(Geom_CylindricalSurface))
                && !surface->IsKind(STANDARD_TYPE(Geom_ConicalSurface)))
            {
                skipped++;
                continue;
            }

            draft.Add(face, pullDirection, angle, neutral);

            if (!draft.AddDone())
            {
                draft.Remove(face);
                skipped++;
                continue;
            }

            added++;
        }

        if (added == 0)
        {
            return fail(
                SPARK_ERR_REFUSED,
                "No face could be drafted in that direction: " + std::to_string(skipped)
                    + " face(s) are parallel to the pull, or are neither planar, cylindrical nor "
                    "conical, which are the only kinds a draft can tilt.");
        }

        draft.Build();

        if (!draft.IsDone())
        {
            return fail(
                SPARK_ERR_REFUSED,
                "The draft tilted " + std::to_string(added) + " face(s) and then did not complete.");
        }

        return emit(draft.Shape(), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_sew(
    const spark_shape* const* pieces, int32_t count, double tolerance, spark_shape** out)
{
    return guard("Sewing", [&]() -> spark_status
    {
        if (pieces == nullptr || out == nullptr || count <= 0)
        {
            return fail(SPARK_ERR_ARGUMENT, "Sewing needs at least one piece and somewhere to go.");
        }

        BRepBuilderAPI_Sewing sewing(tolerance > 0.0 ? tolerance : 1.0e-6);

        for (int32_t i = 0; i < count; i++)
        {
            if (pieces[i] == nullptr || pieces[i]->shape.IsNull())
            {
                return fail(SPARK_ERR_ARGUMENT, "Piece " + std::to_string(i) + " is missing.");
            }

            sewing.Add(pieces[i]->shape);
        }

        sewing.Perform();

        TopoDS_Shape sewn = sewing.SewedShape();

        if (sewn.IsNull())
        {
            return fail(SPARK_ERR_REFUSED, "The pieces did not sew into anything.");
        }

        // If it closed, say so in the topology rather than leaving a shell that looks like a solid
        // to a human and does not to a kernel.
        if (sewn.ShapeType() == TopAbs_SHELL)
        {
            const TopoDS_Shell& asShell = TopoDS::Shell(sewn);

            if (asShell.Closed())
            {
                BRepBuilderAPI_MakeSolid solid(asShell);

                if (solid.IsDone())
                {
                    TopoDS_Solid made = TopoDS::Solid(solid.Shape());
                    BRepLib::OrientClosedSolid(made);
                    sewn = made;
                }
            }
        }

        return emit(sewn, out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_heal(
    const spark_shape* shape, double tolerance, spark_shape** out)
{
    return guard("Healing", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "healed");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "Healing needs somewhere to go.");
        }

        Handle(ShapeFix_Shape) fix = new ShapeFix_Shape(shape->shape);
        fix->SetPrecision(tolerance > 0.0 ? tolerance : 1.0e-7);
        fix->SetMaxTolerance(tolerance > 0.0 ? tolerance * 1000.0 : 1.0e-4);
        fix->Perform();

        const TopoDS_Shape healed = fix->Shape();

        if (healed.IsNull())
        {
            return fail(SPARK_ERR_REFUSED, "Healing did not produce a shape.");
        }

        return emit(healed, out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_split(
    const spark_shape* shape,
    const spark_shape* const* tools,
    int32_t tool_count,
    double tolerance,
    spark_shape** out)
{
    return guard("A split", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "split");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out == nullptr || tools == nullptr || tool_count <= 0)
        {
            return fail(SPARK_ERR_ARGUMENT, "A split needs at least one tool and somewhere to go.");
        }

        SparkShapeList arguments;
        arguments.Append(shape->shape);

        SparkShapeList cutters;

        for (int32_t i = 0; i < tool_count; i++)
        {
            if (tools[i] == nullptr || tools[i]->shape.IsNull())
            {
                return fail(SPARK_ERR_ARGUMENT, "Split tool " + std::to_string(i) + " is missing.");
            }

            cutters.Append(tools[i]->shape);
        }

        // BRepAlgoAPI_Splitter rather than a Cut: a difference throws the far side away and a
        // split keeps every piece, which is the whole distinction and the reason this is not
        // expressible as one of the three boolean opcodes.
        BRepAlgoAPI_Splitter splitter;
        splitter.SetArguments(arguments);
        splitter.SetTools(cutters);

        if (tolerance > 0.0)
        {
            splitter.SetFuzzyValue(tolerance);
        }

        splitter.Build();

        if (!splitter.IsDone())
        {
            return fail(
                SPARK_ERR_REFUSED,
                "The split did not complete on these shapes." + report_text(splitter.GetReport()));
        }

        return emit(splitter.Shape(), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_offset(
    const spark_shape* shape, double distance, double tolerance, spark_shape** out)
{
    return guard("An offset", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "offset");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "An offset needs somewhere to go.");
        }

        if (distance == 0.0 || distance != distance)
        {
            return fail(SPARK_ERR_ARGUMENT, "An offset distance must be a non-zero finite number.");
        }

        BRepOffsetAPI_MakeOffsetShape offset;
        offset.PerformByJoin(shape->shape, distance, tolerance > 0.0 ? tolerance : 1.0e-7);

        if (!offset.IsDone())
        {
            return fail(
                SPARK_ERR_REFUSED,
                "An offset of " + std::to_string(distance) + " does not fit this shape.");
        }

        return emit(offset.Shape(), out);
    });
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_thicken(
    const spark_shape* shape, double thickness, double tolerance, spark_shape** out)
{
    return guard("Thickening", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "thickened");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "Thickening needs somewhere to go.");
        }

        if (thickness == 0.0 || thickness != thickness)
        {
            return fail(SPARK_ERR_ARGUMENT, "A thickness must be a non-zero finite number.");
        }

        // MakeThickSolidBySimple, not ByJoin: a sheet has no faces to leave open, and ByJoin with
        // an empty opening list is the call that returns an offset rather than a wall (see
        // spark_occt_shell).
        BRepOffsetAPI_MakeThickSolid thick;
        thick.MakeThickSolidBySimple(shape->shape, thickness);

        if (!thick.IsDone())
        {
            return fail(
                SPARK_ERR_REFUSED,
                "A thickness of " + std::to_string(thickness) + " does not fit this sheet.");
        }

        // A SHEET HAS NO INSIDE UNTIL THIS CALL GIVES IT ONE, so this is the operation that has
        // to choose which side that is — and `MakeThickSolidBySimple` follows the sheet's face
        // normal rather than asking. Thickening a world-XY plate upwards produced a solid whose
        // faces all pointed in and whose volume measured -8. Same fix as the importer's, same
        // reason ([N50](../../docs/NOTES.md)): turn the faces, not the flag.
        TopoDS_Shape made = thick.Shape();

        if (made.ShapeType() == TopAbs_SOLID)
        {
            Handle(ShapeFix_Solid) fix = new ShapeFix_Solid(TopoDS::Solid(made));
            fix->SetPrecision(tolerance > 0.0 ? tolerance : 1.0e-7);
            fix->FixShellOrientationMode() = 1;
            fix->Perform();

            const TopoDS_Shape fixed = fix->Shape();

            if (!fixed.IsNull())
            {
                made = fixed;
            }
        }

        return emit(made, out);
    });
}

// ------------------------------------------------------------------------------------------------
// Interchange
// ------------------------------------------------------------------------------------------------

extern "C" spark_status SPARK_OCCT_CALL spark_occt_write_file(
    int32_t format, const spark_shape* shape, const char* path)
{
    return guard("Writing a file", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "written");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (path == nullptr || *path == '\0')
        {
            return fail(SPARK_ERR_ARGUMENT, "A file needs a path.");
        }

        if (format == SPARK_FORMAT_STEP)
        {
            // AP214 international draft is what most CAD systems read most reliably. AP242 is the
            // richer schema and is what an assembly with names and colours would need; nothing
            // here has either yet, so the more widely-read one wins until it does.
            STEPControl_Writer writer;

            if (writer.Transfer(shape->shape, STEPControl_AsIs) != IFSelect_RetDone)
            {
                return fail(SPARK_ERR_REFUSED, "The shape could not be transferred to STEP.");
            }

            return writer.Write(path) == IFSelect_RetDone
                ? SPARK_OK
                : fail(SPARK_ERR_REFUSED, std::string("The STEP file could not be written to ") + path + ".");
        }

        if (format == SPARK_FORMAT_IGES)
        {
            IGESControl_Controller::Init();

            // BRep mode: solids as B-Rep entities rather than as trimmed surfaces. IGES can say
            // both and the second loses the topology, which is the thing worth keeping.
            IGESControl_Writer writer("MM", 1);

            if (!writer.AddShape(shape->shape))
            {
                return fail(SPARK_ERR_REFUSED, "The shape could not be added to the IGES model.");
            }

            writer.ComputeModel();

            return writer.Write(path)
                ? SPARK_OK
                : fail(SPARK_ERR_REFUSED, std::string("The IGES file could not be written to ") + path + ".");
        }

        return fail(SPARK_ERR_ARGUMENT, "There is no file format " + std::to_string(format) + ".");
    });
}

namespace
{
    // Everything a reader transferred, as one shape. A file with one solid gives that solid; a
    // file with several gives a compound, which spark_occt_shape_part_count walks.
    template <typename TReader>
    TopoDS_Shape collect(TReader& reader)
    {
        // TransferRoots() rather than a loop over TransferOneRoot: both readers derive it from
        // XSControl_Reader, and a per-root loop is spelled differently on each of them.
        reader.TransferRoots();

        const int shapes = reader.NbShapes();

        if (shapes == 1)
        {
            return reader.Shape(1);
        }

        BRep_Builder builder;
        TopoDS_Compound compound;
        builder.MakeCompound(compound);

        for (int i = 1; i <= shapes; i++)
        {
            builder.Add(compound, reader.Shape(i));
        }

        return compound;
    }
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_read_file(
    int32_t format, const char* path, double tolerance, spark_shape** out)
{
    return guard("Reading a file", [&]() -> spark_status
    {
        if (path == nullptr || *path == '\0' || out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A read needs a path and somewhere to go.");
        }

        TopoDS_Shape read;

        if (format == SPARK_FORMAT_STEP)
        {
            STEPControl_Reader reader;

            if (reader.ReadFile(path) != IFSelect_RetDone)
            {
                return fail(SPARK_ERR_REFUSED, std::string("The STEP file at ") + path + " could not be read.");
            }

            read = collect(reader);
        }
        else if (format == SPARK_FORMAT_IGES)
        {
            IGESControl_Controller::Init();
            IGESControl_Reader reader;

            if (reader.ReadFile(path) != IFSelect_RetDone)
            {
                return fail(SPARK_ERR_REFUSED, std::string("The IGES file at ") + path + " could not be read.");
            }

            read = collect(reader);
        }
        else
        {
            return fail(SPARK_ERR_ARGUMENT, "There is no file format " + std::to_string(format) + ".");
        }

        if (read.IsNull())
        {
            return fail(SPARK_ERR_REFUSED, std::string("The file at ") + path + " contained no geometry.");
        }

        // An interchange file is somebody else's tolerances, which is the case ShapeFix exists
        // for and the one place ADR-0021's caution about it does not apply: a shape that has just
        // crossed a file format has no parameterisation of ours to drift away from.
        Handle(ShapeFix_Shape) fix = new ShapeFix_Shape(read);
        fix->SetPrecision(tolerance > 0.0 ? tolerance : 1.0e-7);
        fix->Perform();

        const TopoDS_Shape healed = fix->Shape();

        return emit(healed.IsNull() ? read : healed, out);
    });
}

// ------------------------------------------------------------------------------------------------
// Reading back
// ------------------------------------------------------------------------------------------------

extern "C" spark_status SPARK_OCCT_CALL spark_occt_read(
    const spark_shape* shape, double tolerance, spark_model** out)
{
    return guard("Reading a shape", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "read");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A read needs somewhere to put the model.");
        }

        spark_model* model = new spark_model();
        const spark_status status = spark::read_model(shape->shape, tolerance, *model);

        if (status != SPARK_OK)
        {
            delete model;
            return status;
        }

        *out = model;
        return SPARK_OK;
    });
}

extern "C" void SPARK_OCCT_CALL spark_occt_model_release(spark_model* model)
{
    try
    {
        delete model;
    }
    catch (...)
    {
    }
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_model_sizes(const spark_model* model, int32_t* sizes)
{
    ensure_started();

    if (model == nullptr || sizes == nullptr)
    {
        return fail(SPARK_ERR_ARGUMENT, "Sizing a model needs a model and somewhere to put the sizes.");
    }

    for (int i = 0; i < SPARK_SIZE_COUNT; i++)
    {
        sizes[i] = 0;
    }

    sizes[SPARK_SIZE_POINTS] = static_cast<int32_t>(model->points.size() / 3);
    sizes[SPARK_SIZE_CURVES] = static_cast<int32_t>(model->curve_kinds.size());
    sizes[SPARK_SIZE_CURVE_INTS] = static_cast<int32_t>(model->curve_ints.size());
    sizes[SPARK_SIZE_CURVE_DOUBLES] = static_cast<int32_t>(model->curve_doubles.size());
    sizes[SPARK_SIZE_SURFACES] = static_cast<int32_t>(model->surface_kinds.size());
    sizes[SPARK_SIZE_SURFACE_INTS] = static_cast<int32_t>(model->surface_ints.size());
    sizes[SPARK_SIZE_SURFACE_DOUBLES] = static_cast<int32_t>(model->surface_doubles.size());
    sizes[SPARK_SIZE_VERTICES] = static_cast<int32_t>(model->vertices.size());
    sizes[SPARK_SIZE_EDGES] = static_cast<int32_t>(model->edges.size() / 3);
    sizes[SPARK_SIZE_TRIMS] = static_cast<int32_t>(model->trims.size() / 2);
    sizes[SPARK_SIZE_LOOPS] = static_cast<int32_t>(model->loops.size() / 3);
    sizes[SPARK_SIZE_FACES] = static_cast<int32_t>(model->faces.size() / 4);
    sizes[SPARK_SIZE_SHELLS] = static_cast<int32_t>(model->shells.size() / 2);

    return SPARK_OK;
}

namespace
{
    template <typename T>
    void pour(const std::vector<T>& from, T* into)
    {
        if (!from.empty() && into != nullptr)
        {
            std::memcpy(into, from.data(), from.size() * sizeof(T));
        }
    }
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_model_read(const spark_model* model, spark_model_desc* into)
{
    ensure_started();

    if (model == nullptr || into == nullptr)
    {
        return fail(SPARK_ERR_ARGUMENT, "Reading a model needs a model and somewhere to put it.");
    }

    clear_error();

    pour(model->points, into->points);
    pour(model->curve_kinds, into->curve_kinds);
    pour(model->curve_int_offsets, into->curve_int_offsets);
    pour(model->curve_ints, into->curve_ints);
    pour(model->curve_double_offsets, into->curve_double_offsets);
    pour(model->curve_doubles, into->curve_doubles);
    pour(model->surface_kinds, into->surface_kinds);
    pour(model->surface_int_offsets, into->surface_int_offsets);
    pour(model->surface_ints, into->surface_ints);
    pour(model->surface_double_offsets, into->surface_double_offsets);
    pour(model->surface_doubles, into->surface_doubles);
    pour(model->vertices, into->vertices);
    pour(model->edges, into->edges);
    pour(model->trims, into->trims);
    pour(model->loops, into->loops);
    pour(model->faces, into->faces);
    pour(model->shells, into->shells);

    into->point_count = static_cast<int32_t>(model->points.size() / 3);
    into->curve_count = static_cast<int32_t>(model->curve_kinds.size());
    into->surface_count = static_cast<int32_t>(model->surface_kinds.size());
    into->vertex_count = static_cast<int32_t>(model->vertices.size());
    into->edge_count = static_cast<int32_t>(model->edges.size() / 3);
    into->trim_count = static_cast<int32_t>(model->trims.size() / 2);
    into->loop_count = static_cast<int32_t>(model->loops.size() / 3);
    into->face_count = static_cast<int32_t>(model->faces.size() / 4);
    into->shell_count = static_cast<int32_t>(model->shells.size() / 2);

    return SPARK_OK;
}

// ------------------------------------------------------------------------------------------------
// Tessellation
// ------------------------------------------------------------------------------------------------

extern "C" spark_status SPARK_OCCT_CALL spark_occt_tessellate(
    const spark_shape* shape, double linear, double angular, spark_mesh** out)
{
    return guard("Tessellating", [&]() -> spark_status
    {
        const spark_status ready = require_shape(shape, "tessellated");
        if (ready != SPARK_OK)
        {
            return ready;
        }

        if (out == nullptr)
        {
            return fail(SPARK_ERR_ARGUMENT, "A tessellation needs somewhere to go.");
        }

        double deflection = positive(linear) ? linear : 0.01;
        const double angle = positive(angular) ? angular : 0.5;

        // A DEFLECTION IS CLAMPED TO THE SHAPE'S OWN SIZE, and this is not a nicety. Asking for
        // 1e-6 on a two-metre sphere is a legal request whose answer is hundreds of millions of
        // triangles; the first time it was asked here the test process reached 31 GB before it
        // was killed. Spark's own tessellator caps itself for the same reason
        // (`Tessellation.MaximumSamplesPerDirection`), so the provider path caps itself too, at a
        // hundred-thousandth of the diagonal — far finer than any display or export needs, and
        // finite.
        Bnd_Box bounds;
        BRepBndLib::Add(shape->shape, bounds, false);

        if (!bounds.IsVoid())
        {
            double xMin = 0.0;
            double yMin = 0.0;
            double zMin = 0.0;
            double xMax = 0.0;
            double yMax = 0.0;
            double zMax = 0.0;
            bounds.Get(xMin, yMin, zMin, xMax, yMax, zMax);

            const double dx = xMax - xMin;
            const double dy = yMax - yMin;
            const double dz = zMax - zMin;
            const double diagonal = std::sqrt((dx * dx) + (dy * dy) + (dz * dz));
            const double floor = diagonal * 1.0e-5;

            if (floor > 0.0 && deflection < floor)
            {
                deflection = floor;
            }
        }

        // A copy, because meshing writes the triangulation into the shape and the caller's shape
        // is a value they may still be holding. Residency makes a shape shared; meshing must not
        // make it different.
        TopoDS_Shape meshed = shape->shape;
        BRepMesh_IncrementalMesh(meshed, deflection, false, angle, true);

        spark_mesh* mesh = new spark_mesh();

        for (TopExp_Explorer it(meshed, TopAbs_FACE); it.More(); it.Next())
        {
            const TopoDS_Face face = TopoDS::Face(it.Value());
            TopLoc_Location location;
            const Handle(Poly_Triangulation) patch = BRep_Tool::Triangulation(face, location);

            if (patch.IsNull() || patch->NbNodes() == 0)
            {
                continue;
            }

            const gp_Trsf& placement = location.Transformation();
            const bool reversed = face.Orientation() == TopAbs_REVERSED;
            const int32_t base = static_cast<int32_t>(mesh->positions.size() / 3);

            // Normals come from the *surface* at each node's own (u, v), not from the triangles:
            // a tessellated cylinder whose normals were per-facet would shade as a prism, and
            // shading like a prism is exactly the thing an exact kernel was bought to avoid.
            // Asking the surface rather than StdPrs also keeps this translation unit out of
            // TKV3d, which is a presentation library and has no business in a geometry shim.
            const Handle(Geom_Surface) basis = BRep_Tool::Surface(face);
            const bool parameterised = !basis.IsNull() && patch->HasUVNodes();

            for (int i = 1; i <= patch->NbNodes(); i++)
            {
                const gp_Pnt position = patch->Node(i).Transformed(placement);
                mesh->positions.push_back(position.X());
                mesh->positions.push_back(position.Y());
                mesh->positions.push_back(position.Z());

                gp_Vec normal(0.0, 0.0, 0.0);

                if (parameterised)
                {
                    const gp_Pnt2d uv = patch->UVNode(i);
                    GeomLProp_SLProps properties(basis, uv.X(), uv.Y(), 1, 1.0e-9);

                    if (properties.IsNormalDefined())
                    {
                        normal = gp_Vec(properties.Normal());
                    }
                }

                // A pole, a degenerate corner, or a surface we could not ask. Zero is honest and
                // the consumer smooths it away against its neighbours.
                if (normal.SquareMagnitude() > 0.0)
                {
                    normal.Transform(placement);
                    normal.Normalize();
                }

                const double sign = reversed ? -1.0 : 1.0;
                mesh->normals.push_back(sign * normal.X());
                mesh->normals.push_back(sign * normal.Y());
                mesh->normals.push_back(sign * normal.Z());
            }

            for (int i = 1; i <= patch->NbTriangles(); i++)
            {
                int a = 0;
                int b = 0;
                int c = 0;
                patch->Triangle(i).Get(a, b, c);

                if (reversed)
                {
                    const int swap = b;
                    b = c;
                    c = swap;
                }

                mesh->triangles.push_back(base + static_cast<int32_t>(a) - 1);
                mesh->triangles.push_back(base + static_cast<int32_t>(b) - 1);
                mesh->triangles.push_back(base + static_cast<int32_t>(c) - 1);
            }
        }

        if (mesh->triangles.empty())
        {
            delete mesh;
            return fail(SPARK_ERR_REFUSED, "The shape produced no triangles at this tolerance.");
        }

        *out = mesh;
        return SPARK_OK;
    });
}

extern "C" void SPARK_OCCT_CALL spark_occt_mesh_release(spark_mesh* mesh)
{
    try
    {
        delete mesh;
    }
    catch (...)
    {
    }
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_mesh_sizes(const spark_mesh* mesh, int32_t* sizes)
{
    ensure_started();

    if (mesh == nullptr || sizes == nullptr)
    {
        return fail(SPARK_ERR_ARGUMENT, "Sizing a mesh needs a mesh and somewhere to put the sizes.");
    }

    sizes[0] = static_cast<int32_t>(mesh->positions.size() / 3);
    sizes[1] = static_cast<int32_t>(mesh->triangles.size() / 3);

    return SPARK_OK;
}

extern "C" spark_status SPARK_OCCT_CALL spark_occt_mesh_read(
    const spark_mesh* mesh, double* positions, double* normals, int32_t* triangles)
{
    ensure_started();

    if (mesh == nullptr)
    {
        return fail(SPARK_ERR_ARGUMENT, "Reading a mesh needs a mesh.");
    }

    clear_error();

    pour(mesh->positions, positions);
    pour(mesh->normals, normals);
    pour(mesh->triangles, triangles);

    return SPARK_OK;
}
