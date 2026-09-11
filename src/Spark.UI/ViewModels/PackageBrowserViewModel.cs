using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Spark.Engine;
using Spark.Packages;

namespace Spark.UI.ViewModels;

/// <summary>One row in the package browser, whether installed or merely found.</summary>
/// <param name="Id">The package id.</param>
/// <param name="Version">The version.</param>
/// <param name="Title">What to show.</param>
/// <param name="Detail">The second line: authors, description or node count.</param>
/// <param name="IsInstalled">Whether this exact version is installed.</param>
public sealed record PackageRow(string Id, string Version, string Title, string Detail, bool IsInstalled)
{
    /// <summary>The identity this row stands for.</summary>
    internal PackageIdentity Identity => new(Id, Version);
}

/// <summary>
/// The package browser's state and operations (<c>E7-T10</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A view model rather than a control, because everything here names engine and package
/// types</b> and <c>Spark.Architecture.Tests</c> forbids that under <c>Views</c> and
/// <c>Controls</c>. The window binds to this and knows nothing about a <c>NodeLibrary</c>.
/// </para>
/// <para>
/// <b>Install is two steps here as it is underneath</b>: <see cref="PrepareAsync"/> downloads and
/// reports what the package is, and nothing is installed until <see cref="ConfirmAsync"/>. That is
/// what makes the disclosure meaningful rather than a notification after the fact
/// (<c>E7-T8</c>).
/// </para>
/// </remarks>
public sealed partial class PackageBrowserViewModel : ObservableObject
{
    private readonly PackageStore _store;
    private readonly PackageTrustStore _trust;
    private readonly PackageManager _manager;
    private readonly NuGetPackageClient _client;
    private readonly Func<Spark.Scripting.ReferenceCatalog?> _catalogue;
    private PendingInstall? _pending;
    private bool _pendingIsLibrary;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _disclosure = string.Empty;

    [ObservableProperty]
    private string _nativeNotice = string.Empty;

    [ObservableProperty]
    private bool _carriesNativeCode;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasPendingInstall;

    /// <summary>Creates a browser over a session's library.</summary>
    /// <param name="library">The library packages contribute their nodes to.</param>
    /// <param name="store">Where packages are installed, or null for the default.</param>
    /// <param name="source">The feed, or null for nuget.org.</param>
    /// <param name="catalogue">
    /// How to reach the code block reference catalogue, or null when there is none. <b>A delegate
    /// rather than the catalogue itself</b>, for `E6-T14`'s reason: building this must not load
    /// Roslyn, and it is called only when a user actually adds a library.
    /// </param>
    /// <param name="trust">
    /// The record of what the user has agreed to, or null for the one beside
    /// <paramref name="store"/>. <b>Pass the session's own when there is one</b> (`E7-T16`): the
    /// record is one file, and two instances over it each miss the other's decisions and then
    /// overwrite them on the next save.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    public PackageBrowserViewModel(
        NodeLibrary library,
        PackageStore? store = null,
        string? source = null,
        Func<Spark.Scripting.ReferenceCatalog?>? catalogue = null,
        PackageTrustStore? trust = null)
    {
        ArgumentNullException.ThrowIfNull(library);

        _catalogue = catalogue ?? (() => null);

        _store = store ?? PackageStore.Default();
        _trust = trust ?? PackageTrustStore.For(_store);
        _manager = new PackageManager(_store, library);
        _client = new NuGetPackageClient(source);

        SourceLabel = Describe(_client.Source);
        Status = "Search " + SourceLabel + " for packages tagged 'spark'.";

        RefreshInstalled();
    }

    /// <summary>The record of what the user has agreed to, for a test to check it is shared.</summary>
    internal PackageTrustStore Trust => _trust;

    /// <summary>
    /// The feed in a form worth showing a user: <c>nuget.org</c>, a host name, or a folder path.
    /// </summary>
    /// <remarks>
    /// <b>A window that says <i>Search nuget.org</i> while pointed at an organisation's own feed
    /// is lying to the person reading it</b>, and the lie matters here more than most: what they
    /// are about to install is code that will run with their permissions.
    /// </remarks>
    public string SourceLabel { get; } = string.Empty;

    /// <summary>What is installed, refreshed from the store.</summary>
    public ObservableCollection<PackageRow> Installed { get; } = [];

    /// <summary>What the last search found.</summary>
    public ObservableCollection<PackageRow> Results { get; } = [];

    /// <summary>Loads every installed package's nodes, and reports what happened.</summary>
    /// <returns>One line per package.</returns>
    /// <remarks>
    /// Called once at startup. A package that will not load is reported rather than thrown,
    /// because one bad package must not stop the application starting — the user needs to get in
    /// to remove it.
    /// </remarks>
    public IReadOnlyList<string> LoadInstalled()
    {
        List<string> lines = [];
        List<string> problems = [];

        foreach (PackageLoadReport report in _manager.LoadAll())
        {
            foreach (string problem in report.Problems)
            {
                problems.Add(report.Identity + ": " + problem);
            }

            lines.Add(report.Problems.Count == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{report.Identity}: {report.Nodes} nodes")
                : report.Identity + ": " + report.Nodes.ToString(CultureInfo.InvariantCulture)
                    + " nodes, " + report.Problems.Count.ToString(CultureInfo.InvariantCulture)
                    + " problem(s) - " + string.Join("; ", report.Problems));
        }

        StartupProblems = problems;
        RefreshInstalled();
        return lines;
    }

    /// <summary>
    /// What went wrong while loading installed packages at startup, one line each.
    /// </summary>
    /// <remarks>
    /// <b>Separate from the returned summary because these are the lines a user has to see.</b> A
    /// package that loaded with half its nodes missing is not a log entry: the nodes it did not
    /// contribute are the ones whose absence turns into placeholders in a document, and a user who
    /// is not told will blame the document.
    /// </remarks>
    public IReadOnlyList<string> StartupProblems { get; private set; } = [];

    /// <summary>
    /// Whether the search asks only for Spark node packages, or for anything on the feed
    /// (`E7-T20`).
    /// </summary>
    /// <remarks>
    /// <b>On by default, because the window's older job is still its main one.</b> A Spark package
    /// carries the <c>spark</c> tag and a <c>tools/spark.json</c> manifest, and installing it adds
    /// **nodes**. Turning this off looks for an ordinary .NET **library** to write code against,
    /// which is what the client was doing when they searched for <c>nice3point</c> and were told
    /// nothing was found.
    /// </remarks>
    [ObservableProperty]
    private bool _sparkPackagesOnly = true;

    /// <summary>
    /// The <c>.spark</c> file this browser is installing beside, or null when the graph has never
    /// been saved (`E7-T18`).
    /// </summary>
    /// <remarks>
    /// <b>Pushed in rather than read out</b>, because the browser is built once per session and
    /// the path changes underneath it — a user saves a scratch graph while the window is open, and
    /// the refusal has to lift without them closing and reopening anything.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGraphSaved))]
    [NotifyPropertyChangedFor(nameof(UnsavedGraphRefusal))]
    private string? _graphPath;

    /// <summary>Whether there is a file to install beside.</summary>
    public bool IsGraphSaved => !string.IsNullOrWhiteSpace(GraphPath);

    /// <summary>
    /// Why the package half of the window is refusing, or empty when it is not (`E7-T18`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The client's call, and it removes a whole class of question.</b> A package added here is
    /// staged into the graph's own <c>&lt;name&gt;.packages</c> folder, which is named after the
    /// file; with no file the window would have to invent a location, move it on first save, and
    /// explain both. Refusing costs the user one save and costs Spark nothing it has to remember.
    /// </para>
    /// <para>
    /// <b>It names the way out and it names what still works</b>, which is the half a refusal
    /// usually leaves off. Adding a local assembly by its full path needs no file on disk, so that
    /// tab is untouched and the sentence says so rather than letting a user conclude the whole
    /// window is shut.
    /// </para>
    /// </remarks>
    public string UnsavedGraphRefusal => IsGraphSaved
        ? string.Empty
        : "Save the graph first. A package added here is installed into the graph's own '"
            + GraphPackages.FolderSuffix
            + "' folder, which sits beside the file and is named after it — and this graph has "
            + "no file yet, so there is nowhere to put it.\n\nLocal assemblies are unaffected: "
            + "that tab adds a DLL by its full path and needs no graph on disk.";

    /// <summary>Searches the feed.</summary>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>A task that completes when the results have been replaced.</returns>
    public async Task SearchAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        Status = "Searching…";

        try
        {
            IReadOnlyList<PackageListing> found = await _client
                .SearchAsync(Query, 30, cancellationToken, SparkPackagesOnly).ConfigureAwait(true);

            Results.Clear();
            foreach (PackageListing listing in found)
            {
                Results.Add(new PackageRow(
                    listing.Identity.Id,
                    listing.Identity.Version,
                    listing.Title,
                    Describe(listing),
                    _store.IsInstalled(listing.Identity)));
            }

            // THE MESSAGE NAMES THE WAY OUT, WHICH IS THE HALF THE OLD ONE WAS MISSING (`E7-T20`).
            //
            // It used to say "Nothing found on nuget.org. Spark packages carry the tag 'spark'." -
            // true, and no help at all to somebody searching for an ordinary library. It stated the
            // rule that had just excluded their result and left them to work out that the rule was
            // the problem. The client did work it out, by asking.
            Status = Results.Count switch
            {
                0 when SparkPackagesOnly =>
                    "No Spark node package found on " + SourceLabel
                    + ". Untick 'Spark packages only' to search every library on the feed.",
                0 =>
                    "Nothing found on " + SourceLabel + ".",
                _ when SparkPackagesOnly =>
                    string.Create(CultureInfo.InvariantCulture, $"{Results.Count} Spark package(s) on {SourceLabel}."),
                _ =>
                    string.Create(CultureInfo.InvariantCulture, $"{Results.Count} package(s) on {SourceLabel}."),
            };
        }
        catch (SparkPackageException failure)
        {
            Status = failure.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Downloads a package and reports what it is, <b>without installing it</b>.
    /// </summary>
    /// <param name="row">The package to prepare.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>A task that completes when <see cref="Disclosure"/> has been filled in.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is null.</exception>
    public async Task PrepareAsync(PackageRow row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        // The rule lives here rather than only on the disabled button (`E7-T18`). A window can be
        // driven from a startup switch and a button can be enabled by a state nobody thought
        // about; an install that would have to invent a folder refuses at the point it would have
        // invented one.
        if (!IsGraphSaved)
        {
            Status = "Save the graph first — there is no folder to install into yet.";
            return;
        }

        Cancel();
        IsBusy = true;
        Status = string.Create(CultureInfo.InvariantCulture, $"Fetching {row.Id} {row.Version}…");

        try
        {
            _pending = await _client
                .PrepareAsync(row.Identity, _store, cancellationToken).ConfigureAwait(true);

            Disclosure = Present(_pending.Disclosure);
            NativeNotice = NativeLine(_pending.Disclosure);
            CarriesNativeCode = _pending.Disclosure.CarriesNativeBinaries;
            HasPendingInstall = true;
            Status = "Nothing has been installed yet.";
        }
        catch (SparkPackageException failure)
        {
            Status = failure.Message;
            ClearDisclosure();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The graph's own package folder, as a store, or null when the graph has no file (`E7-T21`).
    /// </summary>
    /// <remarks>
    /// <b>A <see cref="PackageStore"/> rooted at <c>&lt;name&gt;.packages</c> rather than a second
    /// kind of install.</b> It lays a package down as
    /// <c>&lt;id&gt;.&lt;version&gt;/lib/&lt;tfm&gt;/</c>, which is exactly the layout
    /// <see cref="GraphPackages"/> already reads - so what this writes is what a graph opened on
    /// another machine finds beside it.
    /// </remarks>
    private PackageStore? GraphStore =>
        IsGraphSaved ? new PackageStore(GraphPackages.FolderFor(GraphPath!)) : null;

    /// <summary>
    /// Downloads a package to <b>reference from a code block</b> rather than to take nodes from,
    /// and reports what it is without installing it (`E7-T21`).
    /// </summary>
    /// <param name="row">The package to prepare.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>A task that completes when <see cref="Disclosure"/> has been filled in.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>The client asked why Spark insisted on a Spark package, and the rule was right while the
    /// button was wrong.</b> A manifest names which assemblies hold <c>[SparkNode]</c> types, and
    /// without one Spark would reflect over a package's whole <c>lib</c> folder and offer every
    /// public static method of every dependency as a canvas node. But they did not want
    /// Nice3point's nodes; they wanted its <b>types</b>, in a code block, which needs no manifest
    /// at all. There was one Install button and it was wired to the node importer, so the only
    /// answer it could give was <i>this is not that kind of package</i>.
    /// </para>
    /// <para>
    /// <b>It goes beside the graph, not into the machine-wide store</b>, which is the client's call
    /// and <c>ADR-0024</c>: the graph and the libraries it needs travel together, and two graphs
    /// may disagree about a version without either breaking.
    /// </para>
    /// </remarks>
    public async Task AddAsLibraryAsync(PackageRow row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (GraphStore is not { } target)
        {
            Status = "Save the graph first - there is no folder to install into yet.";
            return;
        }

        Cancel();
        IsBusy = true;
        Status = string.Create(CultureInfo.InvariantCulture, $"Fetching {row.Id} {row.Version}...");

        try
        {
            _pending = await _client
                .PrepareLibraryAsync(row.Identity, target, cancellationToken).ConfigureAwait(true);

            _pendingIsLibrary = true;
            Disclosure = Present(_pending.Disclosure);
            NativeNotice = NativeLine(_pending.Disclosure);
            CarriesNativeCode = _pending.Disclosure.CarriesNativeBinaries;
            HasPendingInstall = true;
            Status = "Nothing has been added yet.";
        }
        catch (SparkPackageException failure)
        {
            Status = failure.Message;
            ClearDisclosure();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Whether the pending decision is about a library to write code against rather than a node
    /// package to install (`E7-T21`).
    /// </summary>
    /// <remarks>
    /// <b>The two answers are not interchangeable, and the button that offers them has to say
    /// which.</b> One adds nodes to the canvas; the other adds types to a code block and puts a
    /// folder beside the user's file.
    /// </remarks>
    public bool PendingIsLibrary => _pendingIsLibrary;

    /// <summary>
    /// What the last added library changed for code blocks: the namespaces that were refused, and
    /// why (`E6-T39`, `E7-T21`).
    /// </summary>
    /// <remarks>
    /// <b><c>ReferenceCatalog.SkippedImports</c> was written and nothing read it</b>, which made a
    /// refused namespace silent - the worst of the three options, because a user then types a type
    /// name that plainly exists and is told it does not. This is where it surfaces.
    /// </remarks>
    [ObservableProperty]
    private string _importNotice = string.Empty;

    /// <summary>Commits the prepared library into the graph's folder and references it.</summary>
    private void CommitLibrary()
    {
        PackageIdentity identity = _pending!.Identity;

        try
        {
            _pending.Commit();

            // Consent is recorded per **content hash**, which is `E7-T16`'s rule and the client's:
            // a path-keyed decision would agree once to a filename and then load whatever later
            // occupied it. Agreeing here is what stops the graph asking again when it is reopened,
            // and a rebuilt assembly still asks, because its bytes are not the ones agreed to.
            GraphAssembly[] added =
            [
                .. GraphPackages.Discover(GraphPath).Assemblies.Where(assembly =>
                    assembly.Package.Equals(identity.FolderName, StringComparison.OrdinalIgnoreCase)),
            ];

            foreach (GraphAssembly assembly in added)
            {
                if (assembly.Hash.Length > 0)
                {
                    _trust.Trust(assembly.Hash);
                }
            }

            Status = Reference(identity, added);
        }
        catch (SparkPackageException failure)
        {
            Status = failure.Message;
        }
        finally
        {
            _pending = null;
            _pendingIsLibrary = false;
            ClearDisclosure();
        }
    }

    /// <summary>
    /// Hands an added library's assemblies to the code block catalogue, and says what changed.
    /// </summary>
    /// <remarks>
    /// <b>Referenced, not loaded.</b> The catalogue takes a metadata reference, which reads the
    /// file without running anything in it - no module initialiser, no static constructor, no
    /// reflection over its types. That is the posture <c>E7-T9</c> takes with a local DLL, and the
    /// reason this gate is about compiling against a library rather than executing one.
    /// </remarks>
    private string Reference(PackageIdentity identity, IReadOnlyList<GraphAssembly> added)
    {
        if (added.Count == 0)
        {
            // `E7-T23`: NAME WHAT THE PACKAGE HAS, NOT WHAT IT WAS GUESSED TO HAVE.
            //
            // This used to say "its 'lib' folder may target a framework Spark does not run on",
            // which was wrong twice over for the package the client tried: it has no 'lib' folder,
            // and its target is the framework Spark does run on. A refusal that guesses sends
            // somebody looking in the wrong place.
            IReadOnlyList<string> offered = GraphPackages.FrameworksOffered(GraphPath!, identity);

            return offered.Count == 0
                ? identity + " was added, but it carries no assembly at all - no 'lib' or 'ref' "
                    + "folder and no .dll beside them. It may be a metadata-only package."
                : identity + " was added, but none of its assemblies are for this build. It offers "
                    + string.Join(", ", offered) + ", and Spark is "
                    + PackageFrameworks.Current.GetShortFolderName() + ".";
        }

        Spark.Scripting.ReferenceCatalog? catalogue = _catalogue();

        if (catalogue is null)
        {
            ImportNotice = string.Empty;

            return identity + " was added to the graph's " + GraphPackages.FolderSuffix
                + " folder. Scripting is off for this session, so nothing compiles against it yet.";
        }

        _ = catalogue.Add(added.Select(assembly => assembly.Path));

        ImportNotice = catalogue.SkippedImports.IsEmpty
            ? string.Empty
            : string.Join(" ", catalogue.SkippedImports);

        string done = string.Create(
            CultureInfo.InvariantCulture,
            $"{identity} was added beside the graph, and {added.Count} assembly(s) are now referenced.");

        return catalogue.SkippedImports.IsEmpty
            ? done + " Code blocks can use its types."
            : done + " Not every namespace could be imported - see below.";
    }

    /// <summary>Installs the prepared package, records the decision, and loads its nodes.</summary>
    /// <returns>A task that completes when the package is usable.</returns>
    public Task ConfirmAsync()
    {
        if (_pending is null)
        {
            return Task.CompletedTask;
        }

        // `E7-T21`: the two answers do different things and must not be confused. A library is
        // referenced so a code block can name its types; a node package is loaded and reflected
        // over so the canvas gains nodes. The same disclosure asks for both, so which one was
        // asked has to be remembered rather than inferred from the package.
        if (_pendingIsLibrary)
        {
            CommitLibrary();
            return Task.CompletedTask;
        }

        PackageIdentity identity = _pending.Identity;

        try
        {
            _pending.Commit();
            _trust.Trust(identity);

            PackageLoadReport report = _manager.Load(identity);

            Status = report.Problems.Count == 0
                ? string.Create(CultureInfo.InvariantCulture, $"Installed {identity}. {report.Nodes} node(s) added.")
                : "Installed " + identity + ", with problems: " + string.Join("; ", report.Problems);
        }
        catch (SparkPackageException failure)
        {
            Status = failure.Message;
        }
        finally
        {
            _pending = null;
            ClearDisclosure();
            RefreshInstalled();
        }

        return Task.CompletedTask;
    }

    /// <summary>Throws away a prepared package without installing it.</summary>
    public void Cancel()
    {
        _pending?.Discard();
        _pending = null;
        _pendingIsLibrary = false;
        HasPendingInstall = false;
        ClearDisclosure();
    }

    /// <summary>Takes the gate off screen and forgets what it said.</summary>
    private void ClearDisclosure()
    {
        HasPendingInstall = false;
        Disclosure = string.Empty;
        NativeNotice = string.Empty;
        CarriesNativeCode = false;
    }

    /// <summary>
    /// Unloads and removes an installed package.
    /// </summary>
    /// <param name="row">The package to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is null.</exception>
    /// <remarks>
    /// <b>When the unload does not take, the status says so and asks for a restart</b>, which is
    /// the UI half of <c>E7-T5</c>. A collectible context is held by anything reachable inside it,
    /// and pretending otherwise would leave a user wondering why the old nodes are still there.
    /// </remarks>
    public void Remove(PackageRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        PackageIdentity identity = row.Identity;
        WeakReference? context = _manager.Unload(identity);

        // Collect in a bounded loop rather than once. An assembly load context does not unload on
        // the collection that drops the last reference to it - the runtime needs a further pass to
        // finalise it - and until it has, Windows still holds the package's .dll files open and the
        // folder cannot be deleted. One Collect() call left a half-deleted folder behind and a
        // status line claiming the package was locked; this is the fix for that.
        for (int attempt = 0; attempt < 20 && context is { IsAlive: true }; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        bool released = context is null || !context.IsAlive;

        try
        {
            _store.Uninstall(identity);
            _trust.Revoke(identity);

            Status = released
                ? string.Create(CultureInfo.InvariantCulture, $"Removed {identity}.")
                : "Removed " + identity + "'s nodes, but its code is still loaded. Restart Spark "
                    + "to release it completely.";
        }
        catch (SparkPackageException failure)
        {
            Status = failure.Message;
        }
        finally
        {
            RefreshInstalled();
        }
    }

    private void RefreshInstalled()
    {
        Installed.Clear();

        foreach (PackageIdentity identity in _store.Installed())
        {
            int nodes = _manager.NodesOf(identity).Count;

            Installed.Add(new PackageRow(
                identity.Id,
                identity.Version,
                identity.Id,
                string.Create(CultureInfo.InvariantCulture, $"{identity.Version} — {nodes} node(s)"),
                IsInstalled: true));
        }
    }

    private static string Describe(PackageListing listing) => listing.Downloads is { } downloads
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{listing.Authors} — {downloads:N0} downloads — {Trim(listing.Description)}")
        : string.Create(CultureInfo.InvariantCulture, $"{listing.Authors} — {Trim(listing.Description)}");

    /// <summary>Turns a feed's source into something short enough to sit in a placeholder.</summary>
    private static string Describe(string source) =>
        string.Equals(source, NuGetPackageClient.DefaultSource, StringComparison.OrdinalIgnoreCase)
            ? "nuget.org"
            : Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && !uri.IsFile
                ? uri.Host
                : source;

    private static string Trim(string text) =>
        text.Length > 140 ? text[..137] + "…" : text;

    /// <summary>
    /// The disclosure as a user reads it, with the native-binary line last and unmissable.
    /// </summary>
    private static string Present(PackageDisclosure disclosure)
    {
        System.Text.StringBuilder text = new();
        text.Append(disclosure.Identity.Id).Append(' ').AppendLine(disclosure.Identity.Version);
        text.Append("Published by ").AppendLine(disclosure.Authors);
        text.Append("Licence: ").AppendLine(disclosure.Licence ?? "not declared");

        text.AppendLine(disclosure.Signature switch
        {
            PackageSignature.PresentButUnverified =>
                "Signature: present. Spark does not verify signatures — it does not check the "
                + "certificate chain, revocation, or who signed it.",
            PackageSignature.Unsigned => "Signature: none.",
            _ => "Signature: could not be read.",
        });

        text.Append("Nodes from: ").AppendLine(
            disclosure.NodeAssemblies.IsEmpty ? "nothing declared" : string.Join(", ", disclosure.NodeAssemblies));

        if (!disclosure.Dependencies.IsEmpty)
        {
            text.Append("Also installs: ").AppendLine(string.Join(", ", disclosure.Dependencies));
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// The native-code sentence, kept apart from the rest so it can be shown in a colour the
    /// others are not.
    /// </summary>
    /// <remarks>
    /// <b>This is the line the disclosure exists for.</b> Spark's own promise is no native
    /// dependencies; a package is entitled to break that on its own behalf, but not silently and
    /// not on the user's behalf. Set in capitals and given the warning colour, because a sentence
    /// that reads like the four above it is a sentence nobody reads.
    /// </remarks>
    private static string NativeLine(PackageDisclosure disclosure) =>
        disclosure.CarriesNativeBinaries
            ? "THIS PACKAGE CONTAINS NATIVE CODE. Spark itself has no native dependencies; this "
                + "package adds "
                + disclosure.NativeBinaries.Length.ToString(CultureInfo.InvariantCulture)
                + ": " + string.Join(", ", disclosure.NativeBinaries.Take(5))
                + (disclosure.NativeBinaries.Length > 5 ? ", …" : string.Empty)
                + ". Native code cannot be unloaded without restarting, and it runs with your "
                + "full permissions."
            : "No native code: this package is managed assemblies only.";
}
