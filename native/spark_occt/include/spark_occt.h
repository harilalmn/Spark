/*
 * spark_occt — Spark's C ABI over OpenCascade.
 *
 * Copyright (c) Spark contributors. MIT. See LICENSE at the repository root.
 *
 * ---------------------------------------------------------------------------------------------
 *
 * THIS HEADER CONTAINS NO C++. That is the whole design, and it is ADR-0020's decision made
 * mechanical: everything OpenCascade knows about is on the other side of this file. Nothing here
 * is a class, nothing here throws, nothing here allocates into a caller's allocator, and nothing
 * here has a destructor. What crosses is `double`, `int32_t`, and pointers to memory whose owner
 * is stated in a comment.
 *
 * THE SURFACE IS SMALL ON PURPOSE (ADR-0020, D17). A generated binding cannot reduce its own
 * surface; a hand-written one is exactly as large as we choose, and every entry point here is one
 * we will have to keep working across an OpenCascade upgrade. So there is one boolean entry point
 * with an opcode rather than three, one import and one read rather than a call per topology
 * table, and geometry travels as tagged flat arrays rather than as a call per surface type.
 *
 * ERROR HANDLING. Every fallible call returns a `spark_status`. On anything but SPARK_OK the
 * caller may retrieve a human-readable reason with `spark_occt_last_error`, which describes the
 * most recent failure *on the calling thread*. A refusal is not exceptional here — an exact
 * kernel refuses constantly and correctly — so it is a return value on both sides of the ABI.
 *
 * OWNERSHIP. `spark_shape`, `spark_model` and `spark_mesh` are opaque handles owned by this
 * library and released by the matching `*_release`. Every `const double*`/`const int32_t*` in a
 * `spark_model_desc` passed *in* is borrowed for the duration of the call and never retained.
 * Every buffer passed to a `*_read` is owned by the caller, who sized it from the matching
 * `*_sizes`.
 *
 * THREADING. Calls are safe from multiple threads on distinct handles. A single handle must not
 * be used from two threads at once. `spark_occt_last_error` is thread-local.
 */

#ifndef SPARK_OCCT_H
#define SPARK_OCCT_H

#include <stdint.h>

#if defined(_WIN32)
#  if defined(SPARK_OCCT_BUILD)
#    define SPARK_OCCT_API __declspec(dllexport)
#  else
#    define SPARK_OCCT_API __declspec(dllimport)
#  endif
#  define SPARK_OCCT_CALL __cdecl
#else
#  if defined(SPARK_OCCT_BUILD)
#    define SPARK_OCCT_API __attribute__((visibility("default")))
#  else
#    define SPARK_OCCT_API
#  endif
#  define SPARK_OCCT_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* --------------------------------------------------------------------------------------------
 * Handles and status
 * ------------------------------------------------------------------------------------------ */

/** An OpenCascade shape. Opaque; released with spark_occt_shape_release. */
typedef struct spark_shape spark_shape;

/** A shape read out into Spark's flat tables. Opaque; released with spark_occt_model_release. */
typedef struct spark_model spark_model;

/** A triangulation. Opaque; released with spark_occt_mesh_release. */
typedef struct spark_mesh spark_mesh;

typedef int32_t spark_status;

#define SPARK_OK                0 /**< The call succeeded. */
#define SPARK_ERR_ARGUMENT      1 /**< A null pointer, a negative count, or a nonsensical number. */
#define SPARK_ERR_REFUSED       2 /**< The operation ran and the geometry declined it. Not a bug. */
#define SPARK_ERR_UNSUPPORTED   3 /**< This build cannot express the request at all. */
#define SPARK_ERR_EXCEPTION     4 /**< OpenCascade raised. The message says what. */

/** The ABI revision. Bumped whenever a signature or an encoding below changes. */
#define SPARK_OCCT_ABI 5

/* --------------------------------------------------------------------------------------------
 * Geometry encodings
 *
 * A curve or a surface crosses as (kind, ints, doubles). A "frame" is always NINE doubles —
 * origin xyz, x-axis xyz, y-axis xyz — and the normal is x cross y, so a frame never disagrees
 * with itself about handedness. Domains are always a closed interval, two doubles, low then high.
 * ------------------------------------------------------------------------------------------ */

#define SPARK_CURVE_LINE      1 /**< doubles: start[3] end[3]. */
#define SPARK_CURVE_CIRCLE    2 /**< doubles: frame[9] radius. */
#define SPARK_CURVE_ARC       3 /**< doubles: frame[9] radius startAngle sweepAngle. */
#define SPARK_CURVE_ELLIPSE   4 /**< doubles: frame[9] major minor startAngle sweepAngle. */
#define SPARK_CURVE_NURBS     5 /**< ints: degree, count, knotCount, rational.
                                     doubles: knots[knotCount] then count * (rational ? 4 : 3). */

#define SPARK_SURFACE_PLANE    1 /**< doubles: frame[9] domainU[2] domainV[2]. */
#define SPARK_SURFACE_CYLINDER 2 /**< doubles: frame[9] radius domainU[2] domainV[2]. */
#define SPARK_SURFACE_CONE     3 /**< doubles: frame[9] radius halfAngle domainU[2] domainV[2]. */
#define SPARK_SURFACE_SPHERE   4 /**< doubles: frame[9] radius domainU[2] domainV[2]. */
#define SPARK_SURFACE_TORUS    5 /**< doubles: frame[9] major minor domainU[2] domainV[2]. */
#define SPARK_SURFACE_NURBS    6 /**< ints: degreeU, degreeV, countU, countV, knotCountU,
                                      knotCountV, rational.
                                      doubles: knotsU, knotsV, then countU*countV*(rational?4:3)
                                      in u-major order. */

/** Loop kinds, matching Spark.Geometry.BrepLoopKind. */
#define SPARK_LOOP_OUTER 0
#define SPARK_LOOP_INNER 1

/**
 * A whole BRep as flat tables — the same nine arrays `Spark.Geometry.Brep` is made of, plus the
 * two variable-length geometry blobs.
 *
 * The `*_offsets` arrays have one more entry than their table, so element i occupies
 * [offsets[i], offsets[i + 1]). An empty table may have a null pointer and a zero count.
 *
 * The same struct is used in both directions: `spark_occt_import` reads one, `spark_occt_model_read`
 * fills one. There is deliberately no separate output type — two structs that must agree field for
 * field are two chances to disagree.
 */
typedef struct spark_model_desc
{
    int32_t   point_count;
    double*   points;                 /**< 3 per point. */

    int32_t   curve_count;
    int32_t*  curve_kinds;            /**< SPARK_CURVE_*, one per curve. */
    int32_t*  curve_int_offsets;      /**< curve_count + 1 entries. */
    int32_t*  curve_ints;
    int32_t*  curve_double_offsets;   /**< curve_count + 1 entries. */
    double*   curve_doubles;

    int32_t   surface_count;
    int32_t*  surface_kinds;          /**< SPARK_SURFACE_*, one per surface. */
    int32_t*  surface_int_offsets;    /**< surface_count + 1 entries. */
    int32_t*  surface_ints;
    int32_t*  surface_double_offsets; /**< surface_count + 1 entries. */
    double*   surface_doubles;

    int32_t   vertex_count;
    int32_t*  vertices;               /**< 1 per vertex: point index. */

    int32_t   edge_count;
    int32_t*  edges;                  /**< 3 per edge: start vertex, end vertex, curve. */

    int32_t   trim_count;
    int32_t*  trims;                  /**< 2 per trim: edge, reversed (0 or 1). */

    int32_t   loop_count;
    int32_t*  loops;                  /**< 3 per loop: first trim, trim count, SPARK_LOOP_*. */

    int32_t   face_count;
    int32_t*  faces;                  /**< 4 per face: surface, first loop, loop count, reversed. */

    int32_t   shell_count;
    int32_t*  shells;                 /**< 2 per shell: first face, face count. */
} spark_model_desc;

/** Indices into the array filled by spark_occt_model_sizes. */
#define SPARK_SIZE_POINTS          0
#define SPARK_SIZE_CURVES          1
#define SPARK_SIZE_CURVE_INTS      2
#define SPARK_SIZE_CURVE_DOUBLES   3
#define SPARK_SIZE_SURFACES        4
#define SPARK_SIZE_SURFACE_INTS    5
#define SPARK_SIZE_SURFACE_DOUBLES 6
#define SPARK_SIZE_VERTICES        7
#define SPARK_SIZE_EDGES           8
#define SPARK_SIZE_TRIMS           9
#define SPARK_SIZE_LOOPS          10
#define SPARK_SIZE_FACES          11
#define SPARK_SIZE_SHELLS         12
#define SPARK_SIZE_COUNT          16 /**< The array to hand spark_occt_model_sizes. */

/* --------------------------------------------------------------------------------------------
 * Library
 * ------------------------------------------------------------------------------------------ */

/** The ABI revision this library was built with. Compare with SPARK_OCCT_ABI. */
SPARK_OCCT_API int32_t SPARK_OCCT_CALL spark_occt_abi_version(void);

/** The OpenCascade version string, into a caller buffer. Returns the length including the NUL. */
SPARK_OCCT_API int32_t SPARK_OCCT_CALL spark_occt_engine_version(char* buffer, int32_t capacity);

/**
 * Why the most recent failing call on this thread failed.
 *
 * Returns the length including the terminating NUL, so a caller may pass (NULL, 0) to ask how
 * much room it needs. Truncates rather than overflowing.
 */
SPARK_OCCT_API int32_t SPARK_OCCT_CALL spark_occt_last_error(char* buffer, int32_t capacity);

/* --------------------------------------------------------------------------------------------
 * Shapes
 * ------------------------------------------------------------------------------------------ */

/** Releases a shape. Null is allowed and does nothing. */
SPARK_OCCT_API void SPARK_OCCT_CALL spark_occt_shape_release(spark_shape* shape);

/** Roughly how much memory the shape occupies. An estimate; zero is a legal answer. */
SPARK_OCCT_API int64_t SPARK_OCCT_CALL spark_occt_shape_bytes(const spark_shape* shape);

/** Fills counts[4] with shells, faces, edges and vertices. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_shape_counts(
    const spark_shape* shape, int32_t* counts);

/**
 * Writes a shape in OpenCascade's own `.brep` format.
 *
 * NOT an interchange format and not meant to be one: it is what the Draw test harness reads, so a
 * shape that made a kernel misbehave here can be handed to upstream as a reproduction. That is the
 * only reason it exists, and it is why it is not in the format opcode with STEP and IGES.
 */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_dump_brep(
    const spark_shape* shape, const char* path);

/**
 * Runs OpenCascade's own validity checker and describes what it found.
 *
 * Writes a human-readable report into the caller's buffer and returns the length including the
 * NUL, so (NULL, 0) asks how much room is needed. An empty report means the shape is valid.
 */
SPARK_OCCT_API int32_t SPARK_OCCT_CALL spark_occt_check(
    const spark_shape* shape, char* buffer, int32_t capacity);

/** Whether the shape is a closed, correctly oriented solid. Writes 0 or 1. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_shape_is_solid(
    const spark_shape* shape, int32_t* out_solid);

/**
 * Whether a point is inside the shape. Writes 0 or 1.
 *
 * Exists so that `Trim` can be a *managed* composition of `spark_occt_split` and this, rather than
 * a seventh boolean-like entry point that would have to repeat all of split's argument handling.
 */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_shape_contains(
    const spark_shape* shape, const double* point, double tolerance, int32_t* out_inside);

/**
 * How many top-level pieces a shape has.
 *
 * One, for anything that is a single solid or shell. More, for the compound `spark_occt_split`
 * produces. A caller walks them with spark_occt_shape_part.
 */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_shape_part_count(
    const spark_shape* shape, int32_t* out_count);

/** One top-level piece, as its own shape. The caller owns it. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_shape_part(
    const spark_shape* shape, int32_t index, spark_shape** out);

/* --------------------------------------------------------------------------------------------
 * Construction
 * ------------------------------------------------------------------------------------------ */

SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_make_box(
    const double* frame, double length, double width, double height, spark_shape** out);

SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_make_cylinder(
    const double* frame, double radius, double height, spark_shape** out);

SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_make_sphere(
    const double* frame, double radius, spark_shape** out);

SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_make_cone(
    const double* frame, double bottom_radius, double top_radius, double height, spark_shape** out);

SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_make_torus(
    const double* frame, double major_radius, double minor_radius, spark_shape** out);

/**
 * Builds an OpenCascade shape from Spark's tables.
 *
 * Faces are made from their surfaces and their loops; a loop whose trims are the surface's own
 * boundary produces an untrimmed face. The result is sewn, and if it closes it is made a solid.
 */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_import(
    const spark_model_desc* model, double tolerance, spark_shape** out);

/* --------------------------------------------------------------------------------------------
 * Operations
 * ------------------------------------------------------------------------------------------ */

#define SPARK_BOOLEAN_UNION        0
#define SPARK_BOOLEAN_DIFFERENCE   1
#define SPARK_BOOLEAN_INTERSECTION 2

SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_boolean(
    int32_t operation,
    const spark_shape* first,
    const spark_shape* second,
    double tolerance,
    spark_shape** out);

/** Sweeps the profile — a model carrying curves and nothing else — along a direction. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_extrude(
    const spark_model_desc* profile,
    const double* direction,
    int32_t cap,
    double tolerance,
    spark_shape** out);

/** Spins the profile about an axis. `angle` is in radians. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_revolve(
    const spark_model_desc* profile,
    const double* axis_origin,
    const double* axis_direction,
    double angle,
    double tolerance,
    spark_shape** out);

/** Lofts through the profiles, in the order the model carries them. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_loft(
    const spark_model_desc* profiles, int32_t closed, double tolerance, spark_shape** out);

/**
 * Rounds edges. Indices are into the shape's own edge order — the same order
 * spark_occt_model_read reports — and an empty list means every edge.
 */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_fillet(
    const spark_shape* shape,
    const int32_t* edges,
    int32_t edge_count,
    double radius,
    spark_shape** out);

/** Bevels edges. Indices as for spark_occt_fillet. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_chamfer(
    const spark_shape* shape,
    const int32_t* edges,
    int32_t edge_count,
    double distance,
    spark_shape** out);

/** Hollows the solid, removing the listed faces. An empty list leaves it closed. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_shell(
    const spark_shape* shape,
    const int32_t* faces,
    int32_t face_count,
    double thickness,
    double tolerance,
    spark_shape** out);

/**
 * Tilts faces away from a pull direction, the way a moulded part is drafted.
 *
 * `angle` is in radians and positive tilts outwards. The neutral plane is the one the tilt pivots
 * about: it is where the shape keeps its size, and it has to be named because "tilt this face"
 * otherwise does not say around what.
 */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_draft(
    const spark_shape* shape,
    const int32_t* faces,
    int32_t face_count,
    const double* pull_direction,
    double angle,
    const double* neutral_origin,
    const double* neutral_normal,
    spark_shape** out);

/** Joins pieces along coincident edges. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_sew(
    const spark_shape* const* pieces, int32_t count, double tolerance, spark_shape** out);

/** Repairs a shape: fixes orientation, small edges, and gaps within tolerance. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_heal(
    const spark_shape* shape, double tolerance, spark_shape** out);

/**
 * Cuts a shape into pieces with one or more tools, keeping every piece.
 *
 * The result is a compound; walk it with spark_occt_shape_part_count and spark_occt_shape_part.
 * A difference throws the far side away and this does not, which is the whole distinction.
 */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_split(
    const spark_shape* shape,
    const spark_shape* const* tools,
    int32_t tool_count,
    double tolerance,
    spark_shape** out);

/** Offsets every face of a shape by a distance. Negative moves inwards. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_offset(
    const spark_shape* shape, double distance, double tolerance, spark_shape** out);

/** Gives a sheet a thickness, making a solid of it. Negative thickens the other way. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_thicken(
    const spark_shape* shape, double thickness, double tolerance, spark_shape** out);

/* --------------------------------------------------------------------------------------------
 * Interchange
 *
 * ONE PAIR OF ENTRY POINTS FOR BOTH FORMATS, with the format as an opcode — the same choice as
 * the single boolean entry point, and for the same reason: every entry point is a thing that has
 * to keep working across an OpenCascade upgrade.
 *
 * Paths are UTF-8 and NUL-terminated, and the library never keeps one.
 * ------------------------------------------------------------------------------------------ */

#define SPARK_FORMAT_STEP 0
#define SPARK_FORMAT_IGES 1

/** Writes a shape to a file. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_write_file(
    int32_t format, const spark_shape* shape, const char* path);

/** Reads a file into a shape. Everything in the file becomes one shape, a compound if need be. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_read_file(
    int32_t format, const char* path, double tolerance, spark_shape** out);

/* --------------------------------------------------------------------------------------------
 * Reading back
 * ------------------------------------------------------------------------------------------ */

/** Reads a shape into Spark's tables. The model is a snapshot and does not alias the shape. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_read(
    const spark_shape* shape, double tolerance, spark_model** out);

/** Releases a model. Null is allowed and does nothing. */
SPARK_OCCT_API void SPARK_OCCT_CALL spark_occt_model_release(spark_model* model);

/** Fills a SPARK_SIZE_COUNT array with the lengths the caller must allocate. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_model_sizes(
    const spark_model* model, int32_t* sizes);

/**
 * Copies the model into the caller's buffers.
 *
 * Every pointer in `into` must address at least as many elements as spark_occt_model_sizes
 * reported, times that array's stride. A null pointer for a table whose count is zero is fine.
 * The count fields of `into` are ignored on the way in and overwritten on the way out.
 */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_model_read(
    const spark_model* model, spark_model_desc* into);

/* --------------------------------------------------------------------------------------------
 * Tessellation
 * ------------------------------------------------------------------------------------------ */

/** Triangulates a shape. `angular` is in radians. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_tessellate(
    const spark_shape* shape, double linear, double angular, spark_mesh** out);

/** Releases a mesh. Null is allowed and does nothing. */
SPARK_OCCT_API void SPARK_OCCT_CALL spark_occt_mesh_release(spark_mesh* mesh);

/** Fills sizes[2] with the vertex count and the triangle count. */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_mesh_sizes(
    const spark_mesh* mesh, int32_t* sizes);

/**
 * Copies the mesh out. `positions` and `normals` take 3 doubles per vertex, `triangles` 3 ints
 * per triangle. Any of the three may be null to skip it.
 */
SPARK_OCCT_API spark_status SPARK_OCCT_CALL spark_occt_mesh_read(
    const spark_mesh* mesh, double* positions, double* normals, int32_t* triangles);

#ifdef __cplusplus
}
#endif

#endif /* SPARK_OCCT_H */
