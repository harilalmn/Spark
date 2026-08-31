using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Spark.Host;

namespace Spark.UI.ViewModels;

/// <summary>One local assembly in the list.</summary>
/// <param name="Path">The full path.</param>
/// <param name="Title">The file name.</param>
/// <param name="Detail">The second line: the folder, the hash, and what state it is in.</param>
/// <param name="NeedsAttention">
/// True when the file has changed since it was agreed to, or has gone missing.
/// </param>
public sealed record LocalReferenceRow(string Path, string Title, string Detail, bool NeedsAttention);

/// <summary>
/// The local assemblies a user has referenced, and what has happened to them since (<c>E7-T9</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Listing what is recorded never loads Roslyn.</b> The list comes from
/// <see cref="LocalReferenceStore"/>, which is a text file and a hash; only agreeing to an
/// assembly reaches for the compiler's catalogue. <c>E6-T14</c> says a graph with no script nodes
/// must never load <c>Spark.Scripting</c>, and a reference list that loaded it to draw itself
/// would break that for every session that opened this tab.
/// </para>
/// <para>
/// <b>Adding is two steps, as installing a package is.</b> Choosing a file shows what it is and
/// what agreeing means; nothing is referenced until the user says so. A DLL is code that will be
/// compiled against and whose types will run, which is the same class of decision as a package and
/// deserves the same gate.
/// </para>
/// <para>
/// <b>A rebuild is announced, never applied.</b> The watcher raises a change and this marks the
/// row; reloading is a button. A reference that swapped itself out underneath a running graph
/// would change what the graph computes without anybody asking.
/// </para>
/// </remarks>
public sealed partial class LocalReferencesViewModel : ObservableObject, IDisposable
{
    private readonly LocalReferenceStore _store;
    private readonly LocalReferenceWatcher _watcher = new();
    private readonly Func<Spark.Scripting.ReferenceCatalog?> _catalogue;
    private readonly Action<Action> _toUiThread;
    private LocalReference? _pending;
    private bool _disposed;

    [ObservableProperty]
    private string _status = "Add a .dll to use its types from a code block.";

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private bool _hasPendingTrust;

    /// <summary>Creates the list over a store.</summary>
    /// <param name="store">Where decisions are recorded, or null for the default.</param>
    /// <param name="catalogue">
    /// How to reach the compiler's reference catalogue, or null never to touch it. Called only
    /// when a reference is actually applied.
    /// </param>
    /// <param name="toUiThread">
    /// How to get back onto the UI thread from the watcher's thread-pool callback, or null to run
    /// it where it arrives — which is what a test wants.
    /// </param>
    public LocalReferencesViewModel(
        LocalReferenceStore? store = null,
        Func<Spark.Scripting.ReferenceCatalog?>? catalogue = null,
        Action<Action>? toUiThread = null)
    {
        _store = store ?? new LocalReferenceStore();
        _catalogue = catalogue ?? (() => null);
        _toUiThread = toUiThread ?? (work => work());

        _watcher.Changed += OnFileChanged;

        Refresh();
    }

    /// <summary>The assemblies recorded, newest last.</summary>
    public ObservableCollection<LocalReferenceRow> References { get; } = [];

    /// <summary>The path awaiting an answer, or null.</summary>
    public string? PendingPath => _pending?.Path;

    /// <summary>
    /// Applies every already-agreed assembly to the compiler's catalogue, and starts watching them.
    /// </summary>
    /// <returns>How many were applied.</returns>
    /// <remarks>
    /// <b>Only assemblies whose hash still matches are applied.</b> One that changed while Spark
    /// was closed is listed and marked, and is not compiled against until the user has looked at
    /// it — which is what <i>a changed hash re-prompts</i> means when the change happened between
    /// sessions rather than during one.
    /// </remarks>
    public int Apply()
    {
        Spark.Scripting.ReferenceCatalog? catalogue = _catalogue();
        int applied = 0;

        foreach (LocalReference reference in _store.All())
        {
            _ = _watcher.Watch(reference.Path);

            // Reload rather than Add, and count what came back rather than how much the
            // catalogue grew. Add returns the change in the catalogue's size, and the catalogue
            // also picks up assemblies the process has loaded since it was built - so on a cold
            // start it reports more than it was asked for. Found by a test expecting one.
            if (_store.IsTrusted(reference.Path) && catalogue is not null && catalogue.Reload(reference.Path))
            {
                applied++;
            }
        }

        Refresh();
        return applied;
    }

    /// <summary>
    /// Reads an assembly and shows what agreeing to it would mean. <b>Nothing is referenced.</b>
    /// </summary>
    /// <param name="path">The assembly's path.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    public void Choose(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        LocalReference reference = LocalReferenceStore.Look(path);

        if (!reference.Exists)
        {
            _pending = null;
            HasPendingTrust = false;
            Prompt = string.Empty;
            Status = $"'{path}' could not be read.";
            return;
        }

        _pending = reference;
        HasPendingTrust = true;
        Prompt = Describe(reference);
        Status = "Nothing has been referenced yet.";
    }

    /// <summary>Agrees to the chosen assembly, records its hash, and references it.</summary>
    /// <returns>True when it was applied.</returns>
    public bool Confirm()
    {
        if (_pending is null)
        {
            return false;
        }

        string path = _pending.Path;

        try
        {
            LocalReference agreed = _store.Trust(path);
            _ = _watcher.Watch(path);

            Spark.Scripting.ReferenceCatalog? catalogue = _catalogue();
            bool referenced = catalogue is not null && catalogue.Reload(path);

            Status = catalogue is null
                ? $"{agreed.Name} recorded. Scripting is off for this session, so nothing compiles against it yet."
                : referenced
                    ? $"{agreed.Name} is now referenced. Code blocks can use its types."
                    : $"{agreed.Name} was recorded but could not be read as an assembly.";

            return referenced;
        }
        catch (InvalidOperationException failure)
        {
            Status = failure.Message;
            return false;
        }
        finally
        {
            _pending = null;
            HasPendingTrust = false;
            Prompt = string.Empty;
            Refresh();
        }
    }

    /// <summary>Declines the chosen assembly. Nothing is recorded and nothing is referenced.</summary>
    public void Cancel()
    {
        _pending = null;
        HasPendingTrust = false;
        Prompt = string.Empty;
        Status = "Nothing was referenced.";
    }

    /// <summary>Offers the change a rebuilt assembly represents, so the user can agree again.</summary>
    /// <param name="row">The assembly to reload.</param>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is null.</exception>
    /// <remarks>
    /// <b>A reload is a fresh agreement, not a refresh.</b> The file says something different from
    /// what the user agreed to, and the row's own words are <i>a changed hash re-prompts</i>. In
    /// the ordinary case — a developer who has just rebuilt their own library — they
    /// glance at it and press the button, which costs a second and is the difference between
    /// re-running code they wrote and re-running code that changed underneath them.
    /// </remarks>
    public void Reload(LocalReferenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        Choose(row.Path);
    }

    /// <summary>Forgets an assembly and stops referencing it.</summary>
    /// <param name="row">The assembly to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is null.</exception>
    public void Remove(LocalReferenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        _ = _store.Forget(row.Path);
        _ = _watcher.Unwatch(row.Path);
        _ = _catalogue()?.Remove(row.Path);

        Status = $"{Path.GetFileName(row.Path)} is no longer referenced.";
        Refresh();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.Changed -= OnFileChanged;
        _watcher.Dispose();
    }

    private void OnFileChanged(object? sender, string path) => _toUiThread(() =>
    {
        if (_disposed)
        {
            return;
        }

        Refresh();

        if (_store.HasChanged(path))
        {
            Status = $"{Path.GetFileName(path)} has been rebuilt. Reload it to compile against the new one.";
        }
    });

    private void Refresh()
    {
        References.Clear();

        foreach (LocalReference reference in _store.All())
        {
            bool missing = !reference.Exists;
            bool changed = _store.HasChanged(reference.Path);

            string state = missing
                ? "missing"
                : changed
                    ? "rebuilt - reload to use it"
                    : "referenced";

            References.Add(new LocalReferenceRow(
                reference.Path,
                reference.Name,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{state} - {Folder(reference.Path)}"),
                missing || changed));
        }
    }

    private static string Folder(string path)
    {
        string? folder = Path.GetDirectoryName(path);

        return string.IsNullOrEmpty(folder) ? path : folder;
    }

    /// <summary>What agreeing to an assembly means, in the words a user needs before answering.</summary>
    private static string Describe(LocalReference reference) =>
        reference.Name + Environment.NewLine
        + "In: " + Folder(reference.Path) + Environment.NewLine
        + "Contents: SHA-256 " + reference.ShortHash + Environment.NewLine
        + Environment.NewLine
        + "Code blocks will be compiled against this assembly and its types will run with your "
        + "full permissions. Spark does not check who wrote it. You will be asked again if the "
        + "file changes.";
}
