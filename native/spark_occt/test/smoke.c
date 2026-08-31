/*
 * spark_occt — the smoke test.
 *
 * Copyright (c) Spark contributors. MIT.
 *
 * This is C, not C++, and that is the point: it proves the header can be consumed by a plain C
 * compiler, which is what "a flat C ABI" means and what a C++/CLI binding could not have claimed.
 *
 * What it checks is M1.6-C2 reduced to its smallest honest form — a boolean, end to end, without
 * a line of .NET in the way. If this passes and the managed side fails, the fault is in the
 * P/Invoke; if this fails, it is here or in OpenCascade. That separation is the reason it exists.
 */

#include "spark_occt.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int failures = 0;

static void reason(void)
{
    char message[1024];
    spark_occt_last_error(message, (int32_t)sizeof(message));
    printf("      because: %s\n", message);
}

static void check(int condition, const char* what)
{
    if (condition)
    {
        printf("  ok   %s\n", what);
    }
    else
    {
        printf("  FAIL %s\n", what);
        reason();
        failures++;
    }
}

/* The world frame: origin, x, y. Every construction takes one of these. */
static const double identity[9] = { 0, 0, 0, 1, 0, 0, 0, 1, 0 };

static void offset_frame(double* frame, double x, double y, double z)
{
    memcpy(frame, identity, sizeof(identity));
    frame[0] = x;
    frame[1] = y;
    frame[2] = z;
}

/* The signed volume of a closed triangle soup. Positive when the normals point outwards. */
static double mesh_volume(const double* positions, const int32_t* triangles, int32_t count)
{
    double total = 0.0;
    int32_t i;

    for (i = 0; i < count; i++)
    {
        const double* a = positions + (triangles[(i * 3) + 0] * 3);
        const double* b = positions + (triangles[(i * 3) + 1] * 3);
        const double* c = positions + (triangles[(i * 3) + 2] * 3);

        total += (a[0] * ((b[1] * c[2]) - (b[2] * c[1]))
                  - a[1] * ((b[0] * c[2]) - (b[2] * c[0]))
                  + a[2] * ((b[0] * c[1]) - (b[1] * c[0]))) / 6.0;
    }

    return total;
}

int main(void)
{
    char version[256];
    double frame[9];
    spark_shape* box = NULL;
    spark_shape* other = NULL;
    spark_shape* fused = NULL;
    spark_shape* nothing = NULL;
    spark_mesh* mesh = NULL;
    spark_model* model = NULL;
    int32_t counts[4];
    int32_t sizes[SPARK_SIZE_COUNT];
    int32_t solid = 0;

    printf("spark_occt smoke test\n");

    check(spark_occt_abi_version() == SPARK_OCCT_ABI, "the library and the header agree on the ABI");

    spark_occt_engine_version(version, (int32_t)sizeof(version));
    printf("  ..   engine: %s\n", version);

    /* A box, on its own. Six faces, twelve edges, eight vertices, and it is a solid. */
    check(spark_occt_make_box(identity, 2.0, 3.0, 4.0, &box) == SPARK_OK, "a box is built");
    check(box != NULL, "the box came back");

    if (box != NULL)
    {
        check(spark_occt_shape_counts(box, counts) == SPARK_OK, "the box can be counted");
        check(counts[1] == 6, "the box has six faces");
        check(counts[2] == 12, "the box has twelve edges");
        check(counts[3] == 8, "the box has eight vertices");

        check(spark_occt_shape_is_solid(box, &solid) == SPARK_OK && solid == 1, "the box is a solid");
        check(spark_occt_shape_bytes(box) > 0, "the box reports a size");
    }

    /* Two boxes that overlap, fused. THIS IS M1.6-C2. */
    offset_frame(frame, 1.0, 1.0, 1.0);
    check(spark_occt_make_box(frame, 2.0, 3.0, 4.0, &other) == SPARK_OK, "a second box is built");

    check(
        spark_occt_boolean(SPARK_BOOLEAN_UNION, box, other, 0.0, &fused) == SPARK_OK,
        "two overlapping boxes fuse");

    if (fused != NULL)
    {
        check(spark_occt_shape_counts(fused, counts) == SPARK_OK, "the union can be counted");
        printf("  ..   the union has %d faces, %d edges, %d vertices\n", counts[1], counts[2], counts[3]);
        check(counts[1] > 6, "the union has more faces than either box");
        check(spark_occt_shape_is_solid(fused, &solid) == SPARK_OK && solid == 1, "the union is a solid");
    }

    /* A refusal is a value: two boxes that do not touch have no intersection, and saying so is
       not the same as crashing. */
    check(
        spark_occt_boolean(SPARK_BOOLEAN_INTERSECTION, box, other, 0.0, &nothing) == SPARK_OK,
        "boxes that do overlap do intersect");
    spark_occt_shape_release(nothing);
    nothing = NULL;

    {
        spark_shape* distant = NULL;
        offset_frame(frame, 100.0, 100.0, 100.0);
        spark_occt_make_box(frame, 1.0, 1.0, 1.0, &distant);
        check(
            spark_occt_boolean(SPARK_BOOLEAN_INTERSECTION, box, distant, 0.0, &nothing)
                == SPARK_ERR_REFUSED,
            "boxes that do not touch refuse to intersect, by name");
        spark_occt_shape_release(distant);
    }

    /* Tessellation, and the volume that proves the winding. */
    if (fused != NULL)
    {
        check(spark_occt_tessellate(fused, 0.05, 0.5, &mesh) == SPARK_OK, "the union tessellates");

        if (mesh != NULL)
        {
            int32_t mesh_sizes[2];
            double* positions;
            int32_t* triangles;
            double volume;

            check(spark_occt_mesh_sizes(mesh, mesh_sizes) == SPARK_OK, "the mesh can be sized");
            printf("  ..   %d vertices, %d triangles\n", mesh_sizes[0], mesh_sizes[1]);

            positions = (double*)malloc((size_t)mesh_sizes[0] * 3 * sizeof(double));
            triangles = (int32_t*)malloc((size_t)mesh_sizes[1] * 3 * sizeof(int32_t));

            check(
                spark_occt_mesh_read(mesh, positions, NULL, triangles) == SPARK_OK,
                "the mesh reads out");

            volume = mesh_volume(positions, triangles, mesh_sizes[1]);
            printf("  ..   volume %.4f\n", volume);

            /* 2x3x4 twice, overlapping in a 1x2x3 corner: 24 + 24 - 6 = 42. */
            check(fabs(volume - 42.0) < 0.5, "the union's volume is 42, and the winding is outward");

            free(positions);
            free(triangles);
        }
    }

    /* Reading back into Spark's tables. */
    if (fused != NULL)
    {
        check(spark_occt_read(fused, 0.0, &model) == SPARK_OK, "the union reads into a model");

        if (model != NULL)
        {
            check(spark_occt_model_sizes(model, sizes) == SPARK_OK, "the model can be sized");
            printf(
                "  ..   %d points, %d curves, %d surfaces, %d edges, %d trims, %d loops, %d faces, %d shells\n",
                sizes[SPARK_SIZE_POINTS],
                sizes[SPARK_SIZE_CURVES],
                sizes[SPARK_SIZE_SURFACES],
                sizes[SPARK_SIZE_EDGES],
                sizes[SPARK_SIZE_TRIMS],
                sizes[SPARK_SIZE_LOOPS],
                sizes[SPARK_SIZE_FACES],
                sizes[SPARK_SIZE_SHELLS]);

            check(sizes[SPARK_SIZE_FACES] == counts[1], "the model has as many faces as the shape");
            check(sizes[SPARK_SIZE_SURFACES] == counts[1], "every face brought a surface");
            check(sizes[SPARK_SIZE_SHELLS] >= 1, "the model has a shell");

            {
                spark_model_desc desc;
                memset(&desc, 0, sizeof(desc));

                desc.points = (double*)malloc((size_t)sizes[SPARK_SIZE_POINTS] * 3 * sizeof(double));
                desc.curve_kinds = (int32_t*)malloc((size_t)sizes[SPARK_SIZE_CURVES] * sizeof(int32_t));
                desc.curve_int_offsets =
                    (int32_t*)malloc(((size_t)sizes[SPARK_SIZE_CURVES] + 1) * sizeof(int32_t));
                desc.curve_ints = (int32_t*)malloc((size_t)(sizes[SPARK_SIZE_CURVE_INTS] + 1) * sizeof(int32_t));
                desc.curve_double_offsets =
                    (int32_t*)malloc(((size_t)sizes[SPARK_SIZE_CURVES] + 1) * sizeof(int32_t));
                desc.curve_doubles = (double*)malloc((size_t)(sizes[SPARK_SIZE_CURVE_DOUBLES] + 1) * sizeof(double));
                desc.surface_kinds = (int32_t*)malloc((size_t)sizes[SPARK_SIZE_SURFACES] * sizeof(int32_t));
                desc.surface_int_offsets =
                    (int32_t*)malloc(((size_t)sizes[SPARK_SIZE_SURFACES] + 1) * sizeof(int32_t));
                desc.surface_ints = (int32_t*)malloc((size_t)(sizes[SPARK_SIZE_SURFACE_INTS] + 1) * sizeof(int32_t));
                desc.surface_double_offsets =
                    (int32_t*)malloc(((size_t)sizes[SPARK_SIZE_SURFACES] + 1) * sizeof(int32_t));
                desc.surface_doubles =
                    (double*)malloc((size_t)(sizes[SPARK_SIZE_SURFACE_DOUBLES] + 1) * sizeof(double));
                desc.vertices = (int32_t*)malloc((size_t)sizes[SPARK_SIZE_VERTICES] * sizeof(int32_t));
                desc.edges = (int32_t*)malloc((size_t)sizes[SPARK_SIZE_EDGES] * 3 * sizeof(int32_t));
                desc.trims = (int32_t*)malloc((size_t)sizes[SPARK_SIZE_TRIMS] * 2 * sizeof(int32_t));
                desc.loops = (int32_t*)malloc((size_t)sizes[SPARK_SIZE_LOOPS] * 3 * sizeof(int32_t));
                desc.faces = (int32_t*)malloc((size_t)sizes[SPARK_SIZE_FACES] * 4 * sizeof(int32_t));
                desc.shells = (int32_t*)malloc((size_t)sizes[SPARK_SIZE_SHELLS] * 2 * sizeof(int32_t));

                check(spark_occt_model_read(model, &desc) == SPARK_OK, "the model reads out");
                check(desc.face_count == sizes[SPARK_SIZE_FACES], "the read agrees with the sizing");

                /* And back in again. A round trip is the only test that exercises both halves of
                   the encoding against each other, which is where an off-by-one in an offset
                   table would otherwise sit undetected. */
                {
                    spark_shape* returned = NULL;
                    const spark_status status = spark_occt_import(&desc, 1.0e-6, &returned);

                    check(status == SPARK_OK, "the model imports back into a shape");

                    if (returned != NULL)
                    {
                        int32_t back[4];
                        spark_occt_shape_counts(returned, back);
                        printf("  ..   the round trip has %d faces (was %d)\n", back[1], counts[1]);
                        check(back[1] == counts[1], "the round trip kept every face");
                        spark_occt_shape_release(returned);
                    }
                }

                free(desc.points);
                free(desc.curve_kinds);
                free(desc.curve_int_offsets);
                free(desc.curve_ints);
                free(desc.curve_double_offsets);
                free(desc.curve_doubles);
                free(desc.surface_kinds);
                free(desc.surface_int_offsets);
                free(desc.surface_ints);
                free(desc.surface_double_offsets);
                free(desc.surface_doubles);
                free(desc.vertices);
                free(desc.edges);
                free(desc.trims);
                free(desc.loops);
                free(desc.faces);
                free(desc.shells);
            }
        }
    }

    /* A split keeps every piece, which is what makes it not a fourth boolean. */
    {
        spark_shape* plate = NULL;
        spark_shape* pieces = NULL;
        int32_t parts = 0;

        offset_frame(frame, -1.0, -1.0, 1.9);
        spark_occt_make_box(frame, 6.0, 6.0, 0.2, &plate);

        check(
            spark_occt_split(box, (const spark_shape* const*)&plate, 1, 0.0, &pieces) == SPARK_OK,
            "a box splits on a plate");

        if (pieces != NULL)
        {
            check(
                spark_occt_shape_part_count(pieces, &parts) == SPARK_OK && parts >= 2,
                "the split produced more than one piece");
            printf("  ..   %d pieces\n", parts);

            {
                spark_shape* first = NULL;
                check(spark_occt_shape_part(pieces, 0, &first) == SPARK_OK, "a piece comes out");
                spark_occt_shape_release(first);
            }
        }

        /* And a point inside the box is inside the box. */
        {
            const double middle[3] = { 1.0, 1.5, 2.0 };
            int32_t inside = 0;
            check(
                spark_occt_shape_contains(box, middle, 0.0, &inside) == SPARK_OK && inside == 1,
                "a point in the middle of the box is inside it");
        }

        spark_occt_shape_release(pieces);
        spark_occt_shape_release(plate);
    }

    /* A draft, on a natively built box: the walls tilt, the caps cannot and are left alone. */
    {
        spark_shape* drafted = NULL;
        const double pull[3] = { 0.0, 0.0, 1.0 };
        const double neutral_origin[3] = { 0.0, 0.0, 2.0 };
        const double neutral_normal[3] = { 0.0, 0.0, 1.0 };

        check(
            spark_occt_draft(box, NULL, 0, pull, 0.0872665, neutral_origin, neutral_normal, &drafted)
                == SPARK_OK,
            "a box drafts");

        if (drafted != NULL)
        {
            spark_occt_shape_counts(drafted, counts);
            printf("  ..   the drafted box has %d faces\n", counts[1]);
            check(counts[1] == 6, "drafting tilts faces rather than adding them");
        }

        spark_occt_shape_release(drafted);
    }

    /* A cylinder, because it is the shape whose exactness is the whole argument: three faces. */
    {
        spark_shape* cylinder = NULL;
        check(spark_occt_make_cylinder(identity, 1.0, 5.0, &cylinder) == SPARK_OK, "a cylinder is built");

        if (cylinder != NULL)
        {
            spark_occt_shape_counts(cylinder, counts);
            check(counts[1] == 3, "the cylinder has three faces, not several hundred triangles");
            spark_occt_shape_release(cylinder);
        }
    }

    /* An argument error is an argument error, and says which argument. */
    check(spark_occt_make_box(identity, -1.0, 1.0, 1.0, &box) == SPARK_ERR_ARGUMENT, "a negative box is refused");

    spark_occt_mesh_release(mesh);
    spark_occt_model_release(model);
    spark_occt_shape_release(nothing);
    spark_occt_shape_release(fused);
    spark_occt_shape_release(other);
    spark_occt_shape_release(box);

    printf("%s: %d failure(s)\n", failures == 0 ? "PASS" : "FAIL", failures);

    return failures == 0 ? 0 : 1;
}
