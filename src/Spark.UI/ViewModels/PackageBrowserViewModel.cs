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
    private PendingInstall? _pending;

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
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    public PackageBrowserViewModel(NodeLibrary library, PackageStore? store = null, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(library);

        _store = store ?? PackageStore.Default();
        _trust = PackageTrustStore.For(_store);
        _manager = new PackageManager(_store, library);
        _client = new NuGetPackageClient(source);

        SourceLabel = Describe(_client.Source);
        Status = "Search " + SourceLabel + " for packages tagged 'spark'.";

        RefreshInstalled();
    }

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
                .SearchAsync(Query, 30, cancellationToken).ConfigureAwait(true);

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

            Status = Results.Count == 0
                ? "Nothing found on " + SourceLabel + ". Spark packages carry the tag 'spark'."
                : string.Create(CultureInfo.InvariantCulture, $"{Results.Count} package(s) on {SourceLabel}.");
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

    /// <summary>Installs the prepared package, records the decision, and loads its nodes.</summary>
    /// <returns>A task that completes when the package is usable.</returns>
    public Task ConfirmAsync()
    {
        if (_pending is null)
        {
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
