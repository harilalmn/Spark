using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// Which packages a graph needs and this machine does not have (<c>E7-T6</c>).
/// </summary>
/// <remarks>
/// <b>Read from the placeholders rather than remembered from the load.</b> A placeholder keeps the
/// original <c>NodeKey</c> verbatim, so the package a user has to install is written on the node.
/// That also means the answer goes stale by itself in the right direction: install the package,
/// reopen, and there are no placeholders left to report.
/// </remarks>
public sealed class MissingPackageBannerTests
{
    /// <summary>A graph whose nodes all resolve needs nothing.</summary>
    [Fact]
    public void AGraphThatResolvesNeedsNothing()
    {
        MainWindowViewModel model = new();

        Assert.Empty(model.MissingPackages());
    }

    /// <summary>
    /// <b>A graph naming a package that is not installed reports it, once.</b> Three nodes from one
    /// package are one thing to install, and a banner listing it three times would read as three
    /// problems.
    /// </summary>
    [Fact]
    public void AGraphNamingAnUninstalledPackageReportsItOnce()
    {
        MainWindowViewModel model = new();

        Assert.True(model.TryOpenDocument(Rewritten(model, "Acme.Nodes")));

        Assert.Equal("Acme.Nodes", Assert.Single(model.MissingPackages()));
    }

    /// <summary>Two missing packages are both reported, in the order their nodes appear.</summary>
    [Fact]
    public void TwoMissingPackagesAreBothReported()
    {
        MainWindowViewModel model = new();

        string text = Rewritten(model, "Acme.Nodes");

        // Move one of the rewritten nodes to a second package, so the graph needs two.
        int at = text.IndexOf("Acme.Nodes/", StringComparison.Ordinal);
        text = text[..at] + "Beta.Nodes/" + text[(at + "Acme.Nodes/".Length)..];

        Assert.True(model.TryOpenDocument(text));

        IReadOnlyList<string> missing = model.MissingPackages();

        Assert.Equal(2, missing.Count);
        Assert.Contains("Acme.Nodes", missing, StringComparer.Ordinal);
        Assert.Contains("Beta.Nodes", missing, StringComparer.Ordinal);
    }

    /// <summary>
    /// <b>And the graph is unharmed</b>, which is the promise the banner is only the messenger for:
    /// every node is still there, and the file still saves byte for byte.
    /// </summary>
    [Fact]
    public void TheGraphIsUnharmedAndStillSavesByteForByte()
    {
        MainWindowViewModel model = new();

        string text = Rewritten(model, "Acme.Nodes");
        int before = model.Graph.Nodes.Count;

        Assert.True(model.TryOpenDocument(text));
        Assert.NotEmpty(model.MissingPackages());
        Assert.Equal(before, model.Graph.Nodes.Count);
        Assert.Equal(text, model.TrySaveDocument());
    }

    /// <summary>
    /// The demo graph, reopened, needs nothing again — so the banner is not a state that sticks.
    /// </summary>
    [Fact]
    public void ReopeningAResolvableGraphClearsTheAnswer()
    {
        MainWindowViewModel model = new();

        string ordinary = model.TrySaveDocument() ?? throw new InvalidOperationException("the graph did not save");

        Assert.True(model.TryOpenDocument(Rewritten(model, "Acme.Nodes")));
        Assert.NotEmpty(model.MissingPackages());

        Assert.True(model.TryOpenDocument(ordinary));
        Assert.Empty(model.MissingPackages());
    }

    /// <summary>
    /// Saves the current graph and re-points every node at a package that does not exist, which is
    /// exactly what opening somebody else's graph on a machine without their package looks like.
    /// </summary>
    private static string Rewritten(MainWindowViewModel model, string package)
    {
        string text = model.TrySaveDocument() ?? throw new InvalidOperationException("the graph did not save");

        Assert.Contains("Spark.Nodes.Core/", text, StringComparison.Ordinal);

        return text.Replace("Spark.Nodes.Core/", package + "/", StringComparison.Ordinal);
    }
}
