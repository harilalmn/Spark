using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// Flattening a documentation fragment to the one line a tooltip shows.
/// </summary>
/// <remarks>
/// <b>The empty tags carry the nouns, and dropping them breaks the sentence.</b>
/// <c>&lt;paramref/&gt;</c> and <c>&lt;see/&gt;</c> have no content of their own, so a scan that
/// removes every tag whole removes the word as well — which is how <c>Number.Range</c>'s
/// description reached the properties panel as <i>"A list of numbers from up to , stepping by ."</i>
/// and was reported as a broken sentence.
/// </remarks>
public sealed class XmlDocumentationTests
{
    /// <summary>A <c>paramref</c> becomes the parameter's name.</summary>
    [Fact]
    public void AParameterReferenceKeepsItsName()
    {
        string summary = SummaryOf(
            "A list of numbers from <paramref name=\"start\"/> up to <paramref name=\"end\"/>.");

        Assert.Equal("A list of numbers from start up to end.", summary);
    }

    /// <summary>
    /// A <c>cref</c> becomes its last segment, and a <c>langword</c> becomes the keyword.
    /// </summary>
    /// <remarks>
    /// <c>M:Spark.Geometry.Curve.PointAt(System.Double)</c> in the middle of a sentence is noise;
    /// <c>PointAt</c> is the word the author meant when they typed the cross-reference.
    /// </remarks>
    [Fact]
    public void ACrossReferenceIsReducedToTheWordTheAuthorMeant()
    {
        Assert.Equal(
            "Like Point3d, but null when it misses.",
            SummaryOf(
                "Like <see cref=\"T:Spark.Geometry.Point3d\"/>, but <see langword=\"null\"/> when it misses."));

        Assert.Equal(
            "See PointAt for the parameterised form.",
            SummaryOf("See <see cref=\"M:Spark.Geometry.Curve.PointAt(System.Double)\"/> for the parameterised form."));
    }

    /// <summary>
    /// A tag that wraps text emits nothing of its own, because the text flows through.
    /// </summary>
    [Fact]
    public void AWrappingTagIsStillDroppedWhole()
    {
        Assert.Equal(
            "The radius, which must be positive.",
            SummaryOf("The <c>radius</c>, which <b>must</b> be positive."));
    }

    /// <summary>
    /// The first-party library reads as prose, which is the reason any of this exists.
    /// </summary>
    /// <remarks>
    /// Asserted against the real assembly rather than a fixture: the defect was in what the
    /// properties panel showed for a node somebody actually places, and a fixture can be made to
    /// pass while the shipped documentation still reads badly.
    /// </remarks>
    [Fact]
    public void TheFirstPartyLibraryHasNoStrandedPunctuation()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(Assembly.Load("Spark.Nodes.Core")));

        string[] broken =
        [
            .. library.Definitions()
                .Where(d => d.Description is { } text
                    && (text.Contains(" ,", StringComparison.Ordinal)
                        || text.Contains(" .", StringComparison.Ordinal)))
                .Select(d => d.DisplayName),
        ];

        Assert.Empty(broken);
    }

    private static string SummaryOf(string fragment)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".xml");

        try
        {
            File.WriteAllText(
                path,
                "<?xml version=\"1.0\"?><doc><assembly><name>T</name></assembly><members>"
                + "<member name=\"M:T.M\"><summary>" + fragment + "</summary></member>"
                + "</members></doc>");

            XmlDocumentation docs = XmlDocumentation.Load(path);

            // Read straight out of the dictionary rather than through SummaryOf(MemberInfo).
            // The fragments under test are not on any real member - that is the point, they are
            // the shapes a third-party assembly can contain - and widening the product's API to
            // let a test look one up by key would be the test dictating the design.
            System.Collections.Generic.Dictionary<string, string> summaries =
                (System.Collections.Generic.Dictionary<string, string>)typeof(XmlDocumentation)
                    .GetField("_summaries", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(docs)!;

            return summaries.TryGetValue("M:T.M", out string? summary) ? summary : string.Empty;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
