using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spark.UI.Shell;

/// <summary>
/// The panes the shell can show. Named rather than indexed so a saved layout survives a pane being
/// added or reordered.
/// </summary>
public enum WorkspacePane
{
    /// <summary>The node library tree.</summary>
    Library,

    /// <summary>The node canvas.</summary>
    Canvas,

    /// <summary>The 3D viewport.</summary>
    Viewport,

    /// <summary>The properties inspector.</summary>
    Inspector,
}

/// <summary>
/// A serialisable, testable description of the shell's pane arrangement, with reset-to-default and
/// named presets.
/// </summary>
/// <remarks>
/// <para>
/// This is the idea taken from <c>RCS.Core/UI/Docking/DockLayout.cs</c> — a layout model that is
/// data rather than visual-tree state, so it can be saved, diffed, reset and unit-tested. The
/// docking implementation itself is <b>not</b> ported; that is <c>Dock.Avalonia</c>'s job.
/// </para>
/// <para>
/// <b>Presets are the reason this is a model and not four numbers on the window.</b> A user who
/// has dragged the library to a useless width and cannot get it back has a bad day; a user who can
/// press <i>Reset layout</i> or pick <i>Modelling</i> does not. Both are one assignment here.
/// </para>
/// </remarks>
public sealed class WorkspaceLayout
{
    private const double MinimumFraction = 0.08;
    private const double MaximumFraction = 0.60;

    private double _libraryFraction = 0.16;
    private double _inspectorFraction = 0.20;
    private double _canvasFraction = 0.55;

    /// <summary>The layout the application starts with and that <i>Reset layout</i> returns to.</summary>
    public static WorkspaceLayout Default => new();

    /// <summary>
    /// The width of the library pane as a fraction of the window width. Clamped to 0.08..0.60, so
    /// a corrupt or hand-edited settings file cannot produce a pane that is impossible to grab.
    /// </summary>
    public double LibraryFraction
    {
        get => _libraryFraction;
        set => _libraryFraction = Math.Clamp(value, MinimumFraction, MaximumFraction);
    }

    /// <summary>The width of the inspector pane as a fraction of the window width.</summary>
    public double InspectorFraction
    {
        get => _inspectorFraction;
        set => _inspectorFraction = Math.Clamp(value, MinimumFraction, MaximumFraction);
    }

    /// <summary>
    /// The height of the canvas as a fraction of the height it shares with the viewport. Clamped
    /// the same way.
    /// </summary>
    public double CanvasFraction
    {
        get => _canvasFraction;
        set => _canvasFraction = Math.Clamp(value, MinimumFraction, 1 - MinimumFraction);
    }

    /// <summary>Which panes are visible.</summary>
    public HashSet<WorkspacePane> VisiblePanes { get; } =
        [WorkspacePane.Library, WorkspacePane.Canvas, WorkspacePane.Viewport, WorkspacePane.Inspector];

    /// <summary>The named presets, keyed by the name shown in the workspace menu.</summary>
    /// <returns>A fresh dictionary; presets are values, not shared state.</returns>
    public static IReadOnlyDictionary<string, WorkspaceLayout> Presets() => new Dictionary<string, WorkspaceLayout>
    {
        ["Default"] = Default,

        // Modelling: the viewport is the thing being watched, so it takes most of the height and
        // the inspector goes away.
        ["Modelling"] = Configure(0.14, 0.08, 0.32, [WorkspacePane.Library, WorkspacePane.Canvas, WorkspacePane.Viewport]),

        // Authoring: the graph is the thing being edited, so the viewport shrinks to a check
        // rather than a subject.
        ["Authoring"] = Configure(0.20, 0.22, 0.78, [.. AllPanes()]),

        // Presenting: nothing but the two things an audience needs to see.
        ["Presenting"] = Configure(0.10, 0.10, 0.45, [WorkspacePane.Canvas, WorkspacePane.Viewport]),
    };

    /// <summary>Whether a pane is currently shown.</summary>
    /// <param name="pane">The pane.</param>
    /// <returns>True when visible.</returns>
    public bool IsVisible(WorkspacePane pane) => VisiblePanes.Contains(pane);

    /// <summary>Shows or hides a pane.</summary>
    /// <param name="pane">The pane.</param>
    /// <param name="visible">True to show it.</param>
    public void SetVisible(WorkspacePane pane, bool visible)
    {
        if (visible)
        {
            VisiblePanes.Add(pane);
        }
        else
        {
            VisiblePanes.Remove(pane);
        }
    }

    /// <summary>Copies another layout's values into this one, which is how a preset is applied.</summary>
    /// <param name="other">The layout to copy.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    public void CopyFrom(WorkspaceLayout other)
    {
        ArgumentNullException.ThrowIfNull(other);

        LibraryFraction = other.LibraryFraction;
        InspectorFraction = other.InspectorFraction;
        CanvasFraction = other.CanvasFraction;

        VisiblePanes.Clear();
        foreach (WorkspacePane pane in other.VisiblePanes)
        {
            VisiblePanes.Add(pane);
        }
    }

    /// <summary>Serialises the layout to JSON.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson() => JsonSerializer.Serialize(
        new LayoutRecord(LibraryFraction, InspectorFraction, CanvasFraction, [.. VisiblePanes]),
        LayoutJsonContext.Default.LayoutRecord);

    /// <summary>
    /// Reads a layout back from JSON, falling back to <see cref="Default"/> on anything malformed.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The layout, never null.</returns>
    /// <remarks>
    /// A settings file that fails to parse must not stop the application from starting. The
    /// worst outcome of a bad layout file is a default layout, and that is a recoverable Tuesday
    /// rather than a support case.
    /// </remarks>
    public static WorkspaceLayout FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default;
        }

        LayoutRecord? record;
        try
        {
            record = JsonSerializer.Deserialize(json, LayoutJsonContext.Default.LayoutRecord);
        }
        catch (JsonException)
        {
            return Default;
        }

        if (record is null)
        {
            return Default;
        }

        WorkspaceLayout layout = new()
        {
            LibraryFraction = record.Library,
            InspectorFraction = record.Inspector,
            CanvasFraction = record.Canvas,
        };

        layout.VisiblePanes.Clear();
        foreach (WorkspacePane pane in record.Panes ?? [])
        {
            layout.VisiblePanes.Add(pane);
        }

        if (layout.VisiblePanes.Count == 0)
        {
            layout.VisiblePanes.Add(WorkspacePane.Canvas);
        }

        return layout;
    }

    private static IEnumerable<WorkspacePane> AllPanes()
    {
        yield return WorkspacePane.Library;
        yield return WorkspacePane.Canvas;
        yield return WorkspacePane.Viewport;
        yield return WorkspacePane.Inspector;
    }

    private static WorkspaceLayout Configure(
        double library, double inspector, double canvas, WorkspacePane[] panes)
    {
        WorkspaceLayout layout = new()
        {
            LibraryFraction = library,
            InspectorFraction = inspector,
            CanvasFraction = canvas,
        };

        layout.VisiblePanes.Clear();
        foreach (WorkspacePane pane in panes)
        {
            layout.VisiblePanes.Add(pane);
        }

        return layout;
    }

    /// <summary>The on-disk shape of a layout. Separate from the model so the model can validate.</summary>
    /// <param name="Library">The library pane fraction.</param>
    /// <param name="Inspector">The inspector pane fraction.</param>
    /// <param name="Canvas">The canvas height fraction.</param>
    /// <param name="Panes">The visible panes.</param>
    public sealed record LayoutRecord(
        double Library, double Inspector, double Canvas, WorkspacePane[]? Panes);
}

/// <summary>
/// The source-generated JSON context for <see cref="WorkspaceLayout.LayoutRecord"/>. Generated
/// rather than reflective so the shell keeps working under trimming, which the desktop build will
/// eventually want.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WorkspaceLayout.LayoutRecord))]
public sealed partial class LayoutJsonContext : JsonSerializerContext
{
}
