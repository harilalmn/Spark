using System.Collections.Generic;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// One row of the case table in <c>docs/help/concepts/lacing.md</c> §6.
/// </summary>
/// <param name="Number">The case number. Stable, and never reused.</param>
/// <param name="Description">The table's own description of the row.</param>
/// <param name="Node">The node definition, including the <c>DefaultLacing</c> the table specifies.</param>
/// <param name="Mode">The lacing set on the instance.</param>
/// <param name="Inputs">One value per input port.</param>
/// <param name="Expected">
/// One expected value per output port, or <see langword="null"/> when the table says the node
/// produced no output.
/// </param>
/// <param name="ExpectedRanks">
/// The expected rank per output port. <b>Asserted separately from the value.</b> A flat hundred and
/// a ten-by-ten both look plausible in a watch node, and a rank bug is exactly what a value-only
/// assertion survives.
/// </param>
/// <param name="Code">The expected diagnostic code, or <see langword="null"/> for none.</param>
/// <param name="Severity">The expected severity of that diagnostic.</param>
/// <param name="MessagePrefix">The start of the expected message, where the table quotes one.</param>
public sealed record LacingCase(
    int Number,
    string Description,
    NodeDefinition Node,
    LacingMode Mode,
    object?[] Inputs,
    object?[]? Expected,
    int[] ExpectedRanks,
    string? Code = null,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error,
    string? MessagePrefix = null);

/// <summary>
/// The case table, transcribed. This file is a translation of a document, not a design: if a row
/// here disagrees with <c>docs/help/concepts/lacing.md</c>, this file is wrong.
/// </summary>
public static class LacingCaseTable
{
    private static readonly Point3d A = new(1, 0, 0);
    private static readonly Point3d B = new(2, 0, 0);

    private static readonly Dictionary<int, LacingCase> ByNumber = Build();

    /// <summary>Every case number, as xunit theory data.</summary>
    public static TheoryData<int> Numbers
    {
        get
        {
            TheoryData<int> data = [];
            foreach (int number in ByNumber.Keys)
            {
                data.Add(number);
            }

            return data;
        }
    }

    /// <summary>How many rows the table has. Read off the table, never asserted as a target.</summary>
    public static int Count => ByNumber.Count;

    /// <summary>Every case number in the corpus.</summary>
    public static IReadOnlyCollection<int> AllNumbers => ByNumber.Keys;

    /// <summary>Looks a case up.</summary>
    /// <param name="number">The case number.</param>
    /// <returns>The case.</returns>
    public static LacingCase Case(int number) => ByNumber[number];

    private static SparkList L(params object?[] items) => SparkList.Of(items);

    private static SparkList Empty(int rank) => SparkList.Empty(rank);

    private static Point3d P(double x, double y, double z) => new(x, y, z);

    private static TestCircle C(Point3d centre, double radius) => new(centre, radius);

    private static Dictionary<int, LacingCase> Build()
    {
        List<LacingCase> cases =
        [
            // Group A - depth 0, promotion and rank reconciliation.
            new(1, "Two scalars into two rank-0 ports; nothing replicates",
                LacingNodes.Add, LacingMode.Auto, [3.0, 4.0], [7.0], [0]),
            new(2, "Same, with lacing off; result must be identical",
                LacingNodes.Add, LacingMode.Disabled, [3.0, 4.0], [7.0], [0]),
            new(3, "Same, Cross Product with no replicating inputs; k=0",
                LacingNodes.Add, LacingMode.CrossProduct, [3.0, 4.0], [7.0], [0]),
            new(4, "List into a rank-1 port; excess 0, no replication",
                LacingNodes.Sum, LacingMode.Auto, [L(1.0, 2.0, 3.0)], [6.0], [0]),
            new(5, "Nested list into a rank-2 port; excess 0",
                LacingNodes.Total2d, LacingMode.Auto, [L(L(1.0, 2.0), L(3.0, 4.0))], [10.0], [0]),
            new(6, "Promotion, excess -1: scalar wrapped into a one-element list",
                LacingNodes.Sum, LacingMode.Auto, [5.0], [5.0], [0]),
            new(7, "Promotion, excess -1 into a rank-2 port",
                LacingNodes.Total2d, LacingMode.Auto, [L(1.0, 2.0)], [3.0], [0]),
            new(8, "Promotion, excess -2: wrapped twice",
                LacingNodes.Total2d, LacingMode.Auto, [5.0], [5.0], [0]),
            new(9, "Promotion still applies under Disabled (Decision D3)",
                LacingNodes.Sum, LacingMode.Disabled, [5.0], [5.0], [0]),
            new(10, "Promotion that cannot be reconciled: element type is wrong",
                LacingNodes.Sum, LacingMode.Auto, ["abc"], null, [], DiagnosticCodes.PromotionFailed),
            new(11, "Node whose natural output rank is 1; no replication",
                LacingNodes.Range, LacingMode.Auto, [3.0], [L(0.0, 1.0, 2.0)], [1]),

            // Group B - one replicating input.
            new(12, "Excess +1 on one input, scalar broadcast - Shortest",
                LacingNodes.Add, LacingMode.Shortest, [L(1.0, 2.0, 3.0), 10.0], [L(11.0, 12.0, 13.0)], [1]),
            new(13, "Same - Longest",
                LacingNodes.Add, LacingMode.Longest, [L(1.0, 2.0, 3.0), 10.0], [L(11.0, 12.0, 13.0)], [1]),
            new(14, "Same - Auto resolves to Add's default, which is Longest",
                LacingNodes.Add, LacingMode.Auto, [L(1.0, 2.0, 3.0), 10.0], [L(11.0, 12.0, 13.0)], [1]),
            new(15, "Same - Cross Product with k=1 adds exactly one level",
                LacingNodes.Add, LacingMode.CrossProduct, [L(1.0, 2.0, 3.0), 10.0], [L(11.0, 12.0, 13.0)], [1]),
            new(16, "Same - Disabled; a list cannot become a double",
                LacingNodes.Add, LacingMode.Disabled, [L(1.0, 2.0, 3.0), 10.0], null, [], DiagnosticCodes.MarshallingFailed),
            new(17, "Excess +1 on a rank-1 port",
                LacingNodes.Sum, LacingMode.Auto, [L(L(1.0, 2.0), L(3.0, 4.0))], [L(3.0, 7.0)], [1]),
            new(18, "Same, Disabled; rank 2 will not marshal into IReadOnlyList<double>",
                LacingNodes.Sum, LacingMode.Disabled, [L(L(1.0, 2.0), L(3.0, 4.0))], null, [], DiagnosticCodes.MarshallingFailed),
            new(19, "Excess +1 on a rank-2 port",
                LacingNodes.Total2d, LacingMode.Auto,
                [L(L(L(1.0, 2.0), L(3.0, 4.0)), L(L(5.0, 6.0)))], [L(10.0, 11.0)], [1]),
            new(20, "Excess +2 replicates twice; two levels added",
                LacingNodes.Add, LacingMode.Auto,
                [L(L(1.0, 2.0), L(3.0, 4.0)), 10.0], [L(L(11.0, 12.0), L(13.0, 14.0))], [2]),
            new(21, "Excess +2 on a rank-1 port",
                LacingNodes.Sum, LacingMode.Auto,
                [L(L(L(1.0, 2.0), L(3.0, 4.0)), L(L(5.0), L(6.0, 7.0)))], [L(L(3.0, 7.0), L(5.0, 13.0))], [2]),
            new(22, "Natural output rank 1 plus one replication level",
                LacingNodes.Range, LacingMode.Auto, [L(2.0, 3.0)], [L(L(0.0, 1.0), L(0.0, 1.0, 2.0))], [2]),

            // Group C - two replicating inputs, length relationships.
            new(23, "Equal lengths - Shortest",
                LacingNodes.Add, LacingMode.Shortest, [L(1.0, 2.0, 3.0), L(10.0, 20.0, 30.0)], [L(11.0, 22.0, 33.0)], [1]),
            new(24, "Equal lengths - Longest",
                LacingNodes.Add, LacingMode.Longest, [L(1.0, 2.0, 3.0), L(10.0, 20.0, 30.0)], [L(11.0, 22.0, 33.0)], [1]),
            new(25, "Equal lengths - Auto resolves to Longest",
                LacingNodes.Add, LacingMode.Auto, [L(1.0, 2.0, 3.0), L(10.0, 20.0, 30.0)], [L(11.0, 22.0, 33.0)], [1]),
            new(26, "Equal lengths - Cross Product, k=2, shape 3x3",
                LacingNodes.Add, LacingMode.CrossProduct, [L(1.0, 2.0, 3.0), L(10.0, 20.0, 30.0)],
                [L(L(11.0, 21.0, 31.0), L(12.0, 22.0, 32.0), L(13.0, 23.0, 33.0))], [2]),
            new(27, "Equal lengths - Disabled",
                LacingNodes.Add, LacingMode.Disabled, [L(1.0, 2.0, 3.0), L(10.0, 20.0, 30.0)], null, [], DiagnosticCodes.MarshallingFailed),
            new(28, "One shorter - Shortest truncates to 2",
                LacingNodes.Add, LacingMode.Shortest, [L(1.0, 2.0, 3.0), L(10.0, 20.0)], [L(11.0, 22.0)], [1]),
            // Cases 29 and 30: the table prints [11,22,32], which is arithmetically impossible.
            // Longest repeats b's LAST element, so the third pair is 3 + 20 = 23, not 32 - and the
            // table's own rule, its worked example in §3 ([1,5] extended to four is [1,5,5,5]) and
            // case 45 (y=[10,20] extended to four gives 10,20,20,20) all agree that it is 23. The
            // printed value is a digit transposition. The rule is right and the arithmetic is
            // wrong, so the rule is what is transcribed here. Reported for correction in the
            // document; this comment goes when the table does.
            new(29, "One shorter - Longest repeats b's last element",
                LacingNodes.Add, LacingMode.Longest, [L(1.0, 2.0, 3.0), L(10.0, 20.0)], [L(11.0, 22.0, 23.0)], [1]),
            new(30, "One shorter - Auto resolves to Longest",
                LacingNodes.Add, LacingMode.Auto, [L(1.0, 2.0, 3.0), L(10.0, 20.0)], [L(11.0, 22.0, 23.0)], [1]),
            new(31, "One shorter - Cross Product, shape 3x2",
                LacingNodes.Add, LacingMode.CrossProduct, [L(1.0, 2.0, 3.0), L(10.0, 20.0)],
                [L(L(11.0, 21.0), L(12.0, 22.0), L(13.0, 23.0))], [2]),
            new(32, "One of length 1 - Shortest collapses to a single item",
                LacingNodes.Add, LacingMode.Shortest, [L(1.0, 2.0, 3.0), L(10.0)], [L(11.0)], [1]),
            new(33, "One of length 1 - Longest repeats it",
                LacingNodes.Add, LacingMode.Longest, [L(1.0, 2.0, 3.0), L(10.0)], [L(11.0, 12.0, 13.0)], [1]),
            new(34, "One of length 1 - Cross Product, shape 3x1 (still rank 2)",
                LacingNodes.Add, LacingMode.CrossProduct, [L(1.0, 2.0, 3.0), L(10.0)],
                [L(L(11.0), L(12.0), L(13.0))], [2]),
            new(35, "A length-1 list is not a scalar: rank 1 replicates, rank 0 broadcasts",
                LacingNodes.Add, LacingMode.Longest, [L(10.0), 5.0], [L(15.0)], [1]),
            new(36, "One empty - Shortest; min = 0, silently empty",
                LacingNodes.Add, LacingMode.Shortest, [L(1.0, 2.0, 3.0), Empty(1)], [Empty(1)], [1]),
            new(37, "One empty - Longest; empty propagates (Decision D7)",
                LacingNodes.Add, LacingMode.Longest, [L(1.0, 2.0, 3.0), Empty(1)], [Empty(1)], [1],
                DiagnosticCodes.LongestEmptyPropagated, DiagnosticSeverity.Warning),
            new(38, "One empty - Auto resolves to Longest, so D7 applies",
                LacingNodes.Add, LacingMode.Auto, [L(1.0, 2.0, 3.0), Empty(1)], [Empty(1)], [1],
                DiagnosticCodes.LongestEmptyPropagated, DiagnosticSeverity.Warning),
            new(39, "Both empty - Longest; no warning, nothing surprising happened",
                LacingNodes.Add, LacingMode.Longest, [Empty(1), Empty(1)], [Empty(1)], [1]),
            new(40, "Empty inner dimension - Cross Product keeps the skeleton",
                LacingNodes.Add, LacingMode.CrossProduct, [L(1.0, 2.0, 3.0), Empty(1)],
                [L(Empty(1), Empty(1), Empty(1))], [2]),
            new(41, "Empty outer dimension - Cross Product; empty at rank 2, not 1",
                LacingNodes.Add, LacingMode.CrossProduct, [Empty(1), L(10.0, 20.0)], [Empty(2)], [2]),
            new(42, "Empty list into a rank-1 port is excess 0, not replication",
                LacingNodes.Sum, LacingMode.Auto, [Empty(1)], [0.0], [0]),

            // Group D - three inputs.
            new(43, "Three replicating inputs, equal lengths - Shortest",
                LacingNodes.PointByCoordinates, LacingMode.Shortest,
                [L(1.0, 2.0), L(3.0, 4.0), L(5.0, 6.0)], [L(P(1, 3, 5), P(2, 4, 6))], [1]),
            new(44, "Three replicating, mixed lengths - Shortest takes min(3,2,4)=2",
                LacingNodes.PointByCoordinates, LacingMode.Shortest,
                [L(1.0, 2.0, 3.0), L(10.0, 20.0), L(100.0, 200.0, 300.0, 400.0)],
                [L(P(1, 10, 100), P(2, 20, 200))], [1]),
            new(45, "Same inputs - Longest takes max=4; x and y repeat their last",
                LacingNodes.PointByCoordinates, LacingMode.Longest,
                [L(1.0, 2.0, 3.0), L(10.0, 20.0), L(100.0, 200.0, 300.0, 400.0)],
                [L(P(1, 10, 100), P(2, 20, 200), P(3, 20, 300), P(3, 20, 400))], [1]),
            new(46, "Three replicating inputs - Cross Product gives rank 3, shape 2x2x2",
                LacingNodes.PointByCoordinates, LacingMode.CrossProduct,
                [L(0.0, 1.0), L(0.0, 1.0), L(0.0, 1.0)],
                [L(
                    L(L(P(0, 0, 0), P(0, 0, 1)), L(P(0, 1, 0), P(0, 1, 1))),
                    L(L(P(1, 0, 0), P(1, 0, 1)), L(P(1, 1, 0), P(1, 1, 1))))], [3]),
            new(47, "Two replicating plus one broadcast - Cross Product k=2, not 3",
                LacingNodes.PointByCoordinates, LacingMode.CrossProduct,
                [L(1.0, 2.0), L(10.0, 20.0), 0.0],
                [L(L(P(1, 10, 0), P(1, 20, 0)), L(P(2, 10, 0), P(2, 20, 0)))], [2]),
            new(48, "Mixed excess 1 / 0 / 2 - outermost-first alignment (Decision D1)",
                LacingNodes.PointByCoordinates, LacingMode.Longest,
                [L(1.0, 2.0), 5.0, L(L(7.0, 8.0), L(9.0, 10.0))],
                [L(L(P(1, 5, 7), P(1, 5, 8)), L(P(2, 5, 9), P(2, 5, 10)))], [2]),
            new(49, "Three inputs, one of length 1 - Longest",
                LacingNodes.PointByCoordinates, LacingMode.Longest,
                [L(1.0, 2.0, 3.0), L(0.0), 0.0], [L(P(1, 0, 0), P(2, 0, 0), P(3, 0, 0))], [1]),

            // Group E - Cross Product specifics and replication guides.
            new(50, "Default dimension order is port order; port 0 is the outer loop",
                LacingNodes.Add, LacingMode.CrossProduct, [L(1.0, 2.0), L(10.0, 20.0)],
                [L(L(11.0, 21.0), L(12.0, 22.0))], [2]),
            new(51, "[ReplicationGuide] reverses the nesting order: b outer, a inner",
                LacingNodes.AddGuided, LacingMode.CrossProduct, [L(1.0, 2.0), L(10.0, 20.0)],
                [L(L(11.0, 12.0), L(21.0, 22.0))], [2]),
            new(52, "Duplicate guides on two replicating ports are refused",
                LacingNodes.AddDuplicateGuides, LacingMode.CrossProduct, [L(1.0, 2.0), L(10.0, 20.0)],
                null, [], DiagnosticCodes.DuplicateReplicationGuide),
            new(53, "Cross Product compounds through recursion: k=2 outer plus one inner level",
                LacingNodes.Add, LacingMode.CrossProduct,
                [L(L(1.0, 2.0), L(3.0, 4.0)), L(10.0, 20.0)],
                [L(L(L(11.0, 12.0), L(21.0, 22.0)), L(L(13.0, 14.0), L(23.0, 24.0)))], [3]),
            new(54, "The headline geometry case: centres x radii is a grid, not a flat list",
                LacingNodes.CircleByCenterRadius, LacingMode.CrossProduct, [L(A, B), L(1.0, 5.0)],
                [L(L(C(A, 1), C(A, 5)), L(C(B, 1), C(B, 5)))], [2]),
            new(55, "The same inputs under Longest - 2 circles, rank 1, not 4",
                LacingNodes.CircleByCenterRadius, LacingMode.Longest, [L(A, B), L(1.0, 5.0)],
                [L(C(A, 1), C(B, 5))], [1]),
            new(56, "Cross Product where one input has excess 0 - it is not a dimension",
                LacingNodes.Add, LacingMode.CrossProduct, [L(1.0, 2.0), 10.0], [L(11.0, 12.0)], [1]),

            // Group F - ragged nesting.
            new(57, "Ragged input, scalar broadcast; shape preserved exactly",
                LacingNodes.Add, LacingMode.Longest, [L(L(1.0, 2.0), 3.0), 10.0],
                [L(L(11.0, 12.0), 13.0)], [2]),
            new(58, "Ragged on both inputs, branches align independently",
                LacingNodes.Add, LacingMode.Longest, [L(L(1.0, 2.0), 3.0), L(10.0, L(20.0, 30.0))],
                [L(L(11.0, 12.0), L(23.0, 33.0))], [2]),
            new(59, "Ragged inner lengths - Shortest applies per branch",
                LacingNodes.Add, LacingMode.Shortest,
                [L(L(1.0, 2.0), L(3.0)), L(L(10.0, 20.0), L(30.0, 40.0))],
                [L(L(11.0, 22.0), L(33.0))], [2]),
            new(60, "Ragged inner lengths - Longest applies per branch",
                LacingNodes.Add, LacingMode.Longest,
                [L(L(1.0, 2.0), L(3.0)), L(L(10.0, 20.0), L(30.0, 40.0))],
                [L(L(11.0, 22.0), L(33.0, 43.0))], [2]),
            new(61, "Ragged into a rank-1 port: shallow branches promote, deep ones replicate",
                LacingNodes.Sum, LacingMode.Auto, [L(1.0, L(2.0, 3.0))], [L(1.0, 5.0)], [1]),
            new(62, "Ragged under Cross Product; each cell recurses on its own shape",
                LacingNodes.Add, LacingMode.CrossProduct, [L(L(1.0, 2.0), 3.0), L(10.0, 20.0)],
                [L(L(L(11.0, 12.0), L(21.0, 22.0)), L(13.0, 23.0))], [3]),

            // Group G - null and per-element failure.
            new(63, "null is a rank-0 element and passes through untouched",
                LacingNodes.Echo, LacingMode.Auto, [L(1.0, null, 3.0)], [L(1.0, null, 3.0)], [1]),
            new(64, "null as the whole input - depth 0, so it is a node error, not per-element",
                LacingNodes.Add, LacingMode.Auto, [null, 10.0], null, [], DiagnosticCodes.MarshallingFailed),
            new(65, "1 of 4 elements fails; the other 3 survive, slot 2 is null",
                LacingNodes.Invert, LacingMode.Auto, [L(1.0, 2.0, 0.0, 4.0)],
                [L(1.0, 0.5, null, 0.25)], [1],
                DiagnosticCodes.ElementsFailed, DiagnosticSeverity.Warning, "1 of 4 elements failed; first at [2]"),
            new(66, "A failing element inside a list; the cast failure is per-element",
                LacingNodes.Add, LacingMode.Longest, [L(1.0, null, 3.0), 10.0],
                [L(11.0, null, 13.0)], [1],
                DiagnosticCodes.ElementsFailed, DiagnosticSeverity.Warning, "1 of 3 elements failed; first at [1]"),
            new(67, "Failure inside nested structure reports the full ElementPath",
                LacingNodes.Invert, LacingMode.Auto, [L(L(1.0, 0.0), L(2.0))],
                [L(L(1.0, null), L(0.5))], [2],
                DiagnosticCodes.ElementsFailed, DiagnosticSeverity.Warning, "1 of 3 elements failed; first at [0][1]"),
            new(68, "Every element fails - still a Warning, never an Error (Decision D6)",
                LacingNodes.Invert, LacingMode.Auto, [L(0.0, 0.0)], [L(null, null)], [1],
                DiagnosticCodes.ElementsFailed, DiagnosticSeverity.Warning, "2 of 2 elements failed"),
            new(69, "A failure at depth 0 is an Error, not a Warning - nothing was isolated",
                LacingNodes.Invert, LacingMode.Auto, [0.0], null, [], DiagnosticCodes.NodeThrewAtDepthZero),

            // Group H - author attributes.
            new(70, "[NoReplication] port broadcasts normally when given a scalar",
                LacingNodes.Scale, LacingMode.Auto, [L(1.0, 2.0, 3.0), 2.0], [L(2.0, 4.0, 6.0)], [1]),
            new(71, "[NoReplication] port given a list - refused, not laced",
                LacingNodes.Scale, LacingMode.Auto, [L(1.0, 2.0, 3.0), L(2.0, 3.0)],
                null, [], DiagnosticCodes.ListIntoNoReplicationPort),
            new(72, "[NoReplication] does not contribute to n under Cross Product",
                LacingNodes.Scale, LacingMode.CrossProduct, [L(1.0, 2.0), 2.0], [L(2.0, 4.0)], [1]),
            new(73, "[KeepStructure] - the node sees the outer list, counts rows not items",
                LacingNodes.ListCount, LacingMode.Auto, [L(L(1.0, 2.0), L(3.0, 4.0), L(5.0))], [3], [0]),
            new(74, "[KeepStructure] cannot be overridden by choosing an explicit mode",
                LacingNodes.ListCount, LacingMode.Longest, [L(L(1.0, 2.0), L(3.0, 4.0), L(5.0))], [3], [0]),
            new(75, "[KeepStructure] under Cross Product - still not a dimension",
                LacingNodes.ListCount, LacingMode.CrossProduct, [L(L(1.0, 2.0), L(3.0, 4.0), L(5.0))], [3], [0]),
            new(76, "[KeepStructure] never promotes: a scalar arrives as a scalar",
                LacingNodes.ListCount, LacingMode.Auto, [5.0], [1], [0]),
            new(77, "[KeepStructure] returns the supplied structure unchanged",
                LacingNodes.ListReverse, LacingMode.Auto, [L(L(1.0, 2.0), L(3.0, 4.0))],
                [L(L(3.0, 4.0), L(1.0, 2.0))], [2]),
            new(78, "The bug the attribute prevents - same node without it, rank 2 in",
                LacingNodes.CountNoAttr, LacingMode.Auto, [L(L(1.0, 2.0), L(3.0, 4.0), L(5.0))],
                [L(2, 2, 1)], [1]),
            new(79, "...and the same node rescued by Disabled instead of the attribute",
                LacingNodes.CountNoAttr, LacingMode.Disabled, [L(L(1.0, 2.0), L(3.0, 4.0), L(5.0))], [3], [0]),

            // Group I - multi-output nodes.
            new(80, "Multi-output at depth 0 - both ports scalar",
                LacingNodes.Bounds, LacingMode.Auto, [L(1.0, 2.0, 3.0)], [1.0, 3.0], [0, 0]),
            new(81, "Multi-output transpose - two lists of 2, never one list of pairs",
                LacingNodes.Bounds, LacingMode.Auto, [L(L(1.0, 2.0, 3.0), L(10.0, 20.0))],
                [L(1.0, 10.0), L(3.0, 20.0)], [1, 1]),
            new(82, "Multi-output, two replicating inputs - Longest, lockstep",
                LacingNodes.Split, LacingMode.Longest, [L(1.0, 2.0), L(10.0, 20.0)],
                [L(11.0, 22.0), L(-9.0, -18.0)], [1, 1]),
            new(83, "Multi-output under Cross Product - every port is rank +k",
                LacingNodes.Split, LacingMode.CrossProduct, [L(1.0, 2.0), L(10.0, 20.0)],
                [L(L(11.0, 21.0), L(12.0, 22.0)), L(L(-9.0, -19.0), L(-8.0, -18.0))], [2, 2]),
            new(84, "Multi-output nested twice - both ports keep the same shape",
                LacingNodes.Bounds, LacingMode.Auto, [L(L(L(1.0, 2.0)), L(L(10.0, 20.0, 30.0)))],
                [L(L(1.0), L(10.0)), L(L(2.0), L(30.0))], [2, 2]),
            new(85, "Multi-output with a per-element failure - the null lands on both ports",
                LacingNodes.Bounds, LacingMode.Auto, [L(L(1.0, 2.0, 3.0), Empty(1))],
                [L(1.0, null), L(3.0, null)], [1, 1],
                DiagnosticCodes.ElementsFailed, DiagnosticSeverity.Warning, "1 of 2 elements failed; first at [1]"),

            // Group J - Auto resolution.
            new(86, "Auto on a node whose definition declares CrossProduct - resolves to Cross Product",
                LacingNodes.GridByXY, LacingMode.Auto, [L(0.0, 1.0), L(0.0, 1.0)],
                [L(L(P(0, 0, 0), P(0, 1, 0)), L(P(1, 0, 0), P(1, 1, 0)))], [2]),
            new(87, "The same node with an explicit Longest - the instance overrides the default",
                LacingNodes.GridByXY, LacingMode.Longest, [L(0.0, 1.0), L(0.0, 1.0)],
                [L(P(0, 0, 0), P(1, 1, 0))], [1]),
            new(88, "Auto on a node whose definition declares Disabled - Flatten sees the whole list",
                LacingNodes.ListFlatten, LacingMode.Auto, [L(L(1.0, 2.0), L(3.0, 4.0))],
                [L(1.0, 2.0, 3.0, 4.0)], [1]),
            new(89, "The same node forced to Longest - it flattens each row instead",
                LacingNodes.ListFlatten, LacingMode.Longest, [L(L(1.0, 2.0), L(3.0, 4.0))],
                [L(L(1.0, 2.0), L(3.0, 4.0))], [2]),
            new(90, "Auto on a Longest-defaulting node - defaults do not travel along wires",
                LacingNodes.Add, LacingMode.Auto, [L(0.0, 1.0), L(0.0, 1.0)], [L(0.0, 2.0)], [1]),
        ];

        Dictionary<int, LacingCase> byNumber = [];
        foreach (LacingCase item in cases)
        {
            byNumber.Add(item.Number, item);
        }

        return byNumber;
    }
}
