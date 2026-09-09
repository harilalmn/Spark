using System;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// A type declared in one code block, used by another — `E6-T35`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The client reported this as one defect and it was two.</b> `E6-T34` made a block able to
/// declare a class and use it; their screenshot had the class in one block and the call in the
/// next, and that is what these cover.
/// </para>
/// <para>
/// <b>The assertion that matters most is the one about identity, not about compiling.</b> Two
/// assemblies can each hold a <c>SparkGenerated.Helper</c> and both compile perfectly; they are
/// different CLR types, and the failure only appears when an instance travels from one block to the
/// other — which is the whole point of declaring a type in a graph.
/// </para>
/// </remarks>
public sealed class SharedDeclarationTests
{
    private const string Declares = """
        public class Helper
        {
            public double Value = 7;
            public static Helper Make() => new Helper();
        }
        var made = Helper.Make();
        """;

    private const string Uses = """
        var seen = Helper.Make().Value;
        """;

    private static ScriptNodeFactory Factory()
    {
        // The catalogue is built from what this process has loaded, and a test class that never
        // mentions geometry can run before anything has loaded it - at which point the prelude's
        // `using Spark.Geometry;` does not resolve and every script here fails for a reason that
        // has nothing to do with sharing. Naming a type loads it.
        _ = typeof(Spark.Geometry.Point3d).Assembly.Location;

        return new ScriptNodeFactory();
    }

    /// <summary>
    /// <b>The client's own arrangement</b>: one block declares, the block beside it calls. Before
    /// this row the second was told *the name 'Helper' does not exist in the current context*.
    /// </summary>
    [Fact]
    public void ATypeDeclaredInOneBlockIsVisibleInAnother()
    {
        ScriptNodeFactory factory = Factory();

        Assert.True(factory.Share([Declares, Uses]));

        Assert.Empty(factory.Diagnose(Uses));
        Assert.Equal(7.0, Assert.Single(factory.Create(Uses).Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// <b>One declaration, one assembly, one type.</b> This is the assertion a design that let each
    /// block keep its own copy would fail: both blocks compile, both run, and the object one of
    /// them makes is not the type the other one holds — so passing it down a wire fails with a
    /// message naming <c>Helper</c> twice.
    /// </summary>
    [Fact]
    public void AnInstanceMadeInOneBlockIsTheSameTypeInAnother()
    {
        ScriptNodeFactory factory = Factory();

        _ = factory.Share([Declares, "var taken = given.Value;"]);

        object? made = Assert.Single(factory.Create(Declares).Invoke([], CancellationToken.None));

        NodeDefinitionSource consumer = factory.Create("var taken = given.Value;");

        Assert.Equal("given", Assert.Single(consumer.Inputs).Name);
        Assert.Equal(7.0, Assert.Single(consumer.Invoke([made], CancellationToken.None)));
    }

    /// <summary>
    /// <b>Two types in two blocks may name each other</b>, which is the question a design that made
    /// one block reference another's assembly would have had to answer with a cycle rule. One
    /// compilation has no such question: C# has resolved mutual references since version 1.
    /// </summary>
    [Fact]
    public void TypesInDifferentBlocksMayReferToEachOther()
    {
        ScriptNodeFactory factory = Factory();

        const string first = """
            public class Odd
            {
                public static bool Is(int n) => n == 0 ? false : Even.Is(n - 1);
            }
            var declaredOdd = 1;
            """;

        const string second = """
            public class Even
            {
                public static bool Is(int n) => n == 0 ? true : Odd.Is(n - 1);
            }
            var answer = Even.Is(4);
            """;

        Assert.True(factory.Share([first, second]));

        Assert.Empty(factory.Diagnose(second));
        Assert.Equal(true, Assert.Single(factory.Create(second).Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// <b>A factory nobody tells anything behaves exactly as `E6-T34` left it.</b> Every existing
    /// caller is one of these, and the block that declares a type and uses it must keep working
    /// with no shared set at all.
    /// </summary>
    [Fact]
    public void WithNothingSharedABlockStillDeclaresItsOwnType()
    {
        ScriptNodeFactory factory = Factory();

        Assert.False(factory.Declarations.IsShared);
        Assert.Equal(7.0, Assert.IsType<double>(Field(
            Assert.Single(factory.Create(Declares).Invoke([], CancellationToken.None)))));
    }

    /// <summary>Sharing nothing is not a change, so nothing recompiles.</summary>
    [Fact]
    public void SharingScriptsThatDeclareNothingChangesNothing()
    {
        ScriptNodeFactory factory = Factory();

        Assert.False(factory.Share(["var a = 1;", "var b = 2;"]));
        Assert.False(factory.Declarations.IsShared);
    }

    /// <summary>The same set in a different order is the same set, so no cache key moves.</summary>
    [Fact]
    public void TheFingerprintDoesNotDependOnTheOrderTheBlocksArriveIn()
    {
        ScriptNodeFactory factory = Factory();

        Assert.True(factory.Share([Declares, Uses]));

        string first = factory.Declarations.Fingerprint;

        Assert.False(factory.Share([Uses, Declares]));
        Assert.Equal(first, factory.Declarations.Fingerprint);
    }

    /// <summary>
    /// <b>A diagnostic inside a shared declaration is placed on the line of the block that wrote
    /// it</b>, not on a line of the generated file that gathered them all together.
    /// </summary>
    [Fact]
    public void ADiagnosticInASharedDeclarationNamesTheBlocksOwnLine()
    {
        ScriptNodeFactory factory = Factory();

        const string broken = """
            var a = 1;
            public class Helper
            {
                public static double Twice(double x) => nope;
            }
            """;

        _ = factory.Share(["var b = 2;", broken]);

        ScriptDiagnostic error = Assert.Single(
            factory.Declarations.Diagnostics(broken), diagnostic => diagnostic.IsError);

        Assert.Equal("CS0103", error.Id);
        Assert.Equal(4, error.Line);
    }

    /// <summary>
    /// <b>A shared compilation that does not build switches sharing off rather than taking every
    /// block down with it.</b> One unclosed brace in one block must not stop the other nine, and
    /// falling back is exactly where the product stood a moment before this row.
    /// </summary>
    [Fact]
    public void AShareThatDoesNotCompileFallsBackToEachBlockOnItsOwn()
    {
        ScriptNodeFactory factory = Factory();

        const string broken = """
            public class Broken
            {
                public static double Twice(double x) => nope;
            }
            """;

        _ = factory.Share([broken, Declares]);

        Assert.False(factory.Declarations.IsShared);
        Assert.False(factory.Declarations.Holds(Declares));

        // And the block that was fine is still fine, on its own terms.
        Assert.Empty(factory.Diagnose(Declares));
    }

    /// <summary>
    /// <b>Two blocks declaring the same name is a collision, and it is reported</b> rather than one
    /// of them silently winning. Sharing switches off, so both blocks keep working alone — which is
    /// what makes the diagnostic the only way anybody would find out.
    /// </summary>
    /// <remarks>
    /// <b>Once, against one of the two</b>, because that is what <c>CS0101</c> is: the compiler
    /// reports the declaration it saw second and names the type. Asserting it against a particular
    /// one of the two blocks would be asserting the sort order of their text, which is not the
    /// behaviour anybody depends on.
    /// </remarks>
    [Fact]
    public void TwoBlocksDeclaringTheSameNameAreReported()
    {
        ScriptNodeFactory factory = Factory();

        const string other = """
            public class Helper
            {
                public double Value = 9;
            }
            var mine = new Helper();
            """;

        _ = factory.Share([Declares, other]);

        Assert.False(factory.Declarations.IsShared);

        ScriptDiagnostic collision = Assert.Single(
            [.. factory.Declarations.Diagnostics(Declares), .. factory.Declarations.Diagnostics(other)],
            diagnostic => diagnostic.Id == "CS0101");

        Assert.Contains("Helper", collision.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A loop inside a shared declaration is still guarded.</b> The declaration moved to a
    /// different assembly; the reason `E6-T34` wove a guard into it did not move with it.
    /// </summary>
    [Fact]
    public void ALoopInsideASharedDeclarationIsStillBounded()
    {
        _ = typeof(Spark.Geometry.Point3d).Assembly.Location;

        ScriptNodeFactory factory = new(new ReferenceCatalog(), new GuardWeaver(5_000, 16));

        const string spinner = """
            public class Spinner
            {
                public static int Spin()
                {
                    int n = 0;
                    while (true) { n++; }
                    return n;
                }
            }
            var placed = 1;
            """;

        const string caller = "var spun = Spinner.Spin();";

        Assert.True(factory.Share([spinner, caller]));

        Assert.Throws<ScriptGuardException>(
            () => factory.Create(caller).Invoke([], CancellationToken.None));
    }

    /// <summary>Reads the one public field off whatever the block returned.</summary>
    private static object? Field(object? made) =>
        made?.GetType().GetField("Value")?.GetValue(made);
}
