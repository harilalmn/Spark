// spark_occt — shared internals. Nothing here is visible through the C ABI.
//
// Copyright (c) Spark contributors. MIT.

#ifndef SPARK_OCCT_INTERNAL_HPP
#define SPARK_OCCT_INTERNAL_HPP

#include "spark_occt.h"

#include <NCollection_Array1.hxx>
#include <NCollection_Array2.hxx>
#include <NCollection_IndexedMap.hxx>
#include <NCollection_List.hxx>
#include <TopTools_ShapeMapHasher.hxx>
#include <TopoDS_Shape.hxx>
#include <Poly_Triangulation.hxx>
#include <gp_Ax3.hxx>
#include <gp_Pnt.hxx>

#include <functional>
#include <string>
#include <vector>

// OpenCascade 8.0 deprecated the TopTools_* and TColStd_* typedefs in favour of the NCollection
// templates they always were. One alias here rather than the old spelling in three files: the
// name is ours, so an upstream rename costs one line.
using SparkShapeMap = NCollection_IndexedMap<TopoDS_Shape, TopTools_ShapeMapHasher>;
using SparkShapeList = NCollection_List<TopoDS_Shape>;

// The three handle types. Each is a struct rather than a typedef so that the C ABI's opaque
// pointers are genuinely distinct types to the compiler and cannot be swapped by accident.

struct spark_shape
{
    TopoDS_Shape shape;
};

struct spark_mesh
{
    std::vector<double> positions;  // 3 per vertex
    std::vector<double> normals;    // 3 per vertex
    std::vector<int32_t> triangles; // 3 per triangle
};

// The flat tables a read produces. Owns its storage; `spark_occt_model_read` copies out of it.
struct spark_model
{
    std::vector<double> points;

    std::vector<int32_t> curve_kinds;
    std::vector<int32_t> curve_int_offsets;
    std::vector<int32_t> curve_ints;
    std::vector<int32_t> curve_double_offsets;
    std::vector<double> curve_doubles;

    std::vector<int32_t> surface_kinds;
    std::vector<int32_t> surface_int_offsets;
    std::vector<int32_t> surface_ints;
    std::vector<int32_t> surface_double_offsets;
    std::vector<double> surface_doubles;

    std::vector<int32_t> vertices;
    std::vector<int32_t> edges;
    std::vector<int32_t> trims;
    std::vector<int32_t> loops;
    std::vector<int32_t> faces;
    std::vector<int32_t> shells;
};

namespace spark
{
    // Records why the last call on this thread failed, and hands back the status so that every
    // failure site can be a single `return fail(...)`.
    spark_status fail(spark_status status, const std::string& message);

    // Clears the thread's error before a call that is about to try something.
    void clear_error();

    // Wraps a call so that an OpenCascade exception, a std::exception or anything else becomes a
    // status and a message rather than crossing the ABI. Every exported function's body goes
    // through this — an exception reaching C is undefined behaviour, not a bug report.
    spark_status guard(const char* what, const std::function<spark_status()>& body);

    // A frame is nine doubles: origin, x-axis, y-axis. The normal is x cross y, always.
    gp_Ax3 frame_to_ax3(const double* frame);

    // Writes an Ax3 out as nine doubles. Returns false when the system is left-handed, in which
    // case the caller must flip: Spark has no way to say "indirect", so the y-axis is negated and
    // the u-parameter is mirrored by the caller instead.
    bool ax3_to_frame(const gp_Ax3& axes, double* frame);

    gp_Pnt point(const double* xyz);

    // Takes ownership of a shape into a freshly allocated handle.
    spark_status emit(const TopoDS_Shape& shape, spark_shape** out);

    // The canonical numbering of a shape's faces and edges, and the only one. Every index that
    // crosses the ABI — a fillet's edge list, a hollow's face list, a read model's tables — is an
    // index into one of these two, so an index a caller took from a read is the sub-shape it
    // meant. Faces are walked shell by shell, so a shell's faces are contiguous, which is what
    // Spark's contiguous BRep layout requires. Degenerate edges are left out of both, because
    // they carry no curve for Spark to name and nothing can be done to them anyway.
    void ordered_faces(const TopoDS_Shape& shape, SparkShapeMap& into);
    void ordered_edges(const TopoDS_Shape& shape, SparkShapeMap& into);

    // Reading a shape into flat tables (spark_occt_read.cpp).
    spark_status read_model(const TopoDS_Shape& shape, double tolerance, spark_model& into);

    // Building a shape from flat tables (spark_occt_import.cpp).
    spark_status build_shape(const spark_model_desc& desc, double tolerance, TopoDS_Shape& into);

    // Building just the curves of a model — what extrude, revolve and loft take as a profile.
    spark_status build_wires(
        const spark_model_desc& desc, double tolerance, std::vector<TopoDS_Shape>& into);
}

#endif // SPARK_OCCT_INTERNAL_HPP
