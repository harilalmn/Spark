using System;
using System.Collections.Generic;

namespace Spark.Api;

/// <summary>
/// One line of a notice: a label and the text beside it.
/// </summary>
/// <param name="Label">What the line is, such as <c>Version</c>. May be empty for a paragraph.</param>
/// <param name="Text">The text.</param>
public readonly record struct NoticeLine(string Label, string Text);

/// <summary>
/// What Spark says about itself and about what it links: the version, the solid-modelling kernel,
/// and the licence notices that are obligations rather than courtesies (<c>E12-T18</c>,
/// <c>E13-T16</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists so that there is one text, not two.</b> The command line prints it from
/// <c>spark --version</c> and the desktop application shows it in its About box, and those are the
/// two places somebody with only a binary looks. Two copies of a licence notice is one copy that
/// eventually stops matching the build.
/// </para>
/// <para>
/// <b>The kernel line is a licence obligation.</b> The Open CASCADE exception requires prominent
/// notice in supporting documentation that the work makes use of facilities provided by the Open
/// CASCADE Technology software. It is stated whenever the provider is actually loaded, and its
/// absence is stated too — a user is entitled to know which build they have, because it decides
/// whether booleans, fillets and STEP work at all.
/// </para>
/// <para>
/// <b>Nothing here is legal advice</b>, and the six questions that are with counsel are
/// <c>Q13</c>. This class states what the build links; it does not decide what that obliges.
/// </para>
/// </remarks>
public static class ProductNotice
{
    /// <summary>The application's name, as it appears in a title bar and in About.</summary>
    public const string ProductName = "Spark";

    /// <summary>
    /// The one-line description of what a kernel-less build can and cannot do.
    /// </summary>
    /// <remarks>
    /// Worth stating positively first. A build without the provider is not a broken Spark — every
    /// curve, surface, mesh and file format works — and a notice that led with what is missing
    /// would read as an error message.
    /// </remarks>
    public const string NoKernelNotice =
        "No solid-modelling kernel is installed. Geometry, curves, surfaces, meshes and every file "
        + "format work; exact booleans, fillets, shelling and STEP do not.";

    /// <summary>
    /// Builds the notice for the current process.
    /// </summary>
    /// <param name="version">The application version, or null to omit the line.</param>
    /// <param name="kernelDescription">
    /// How the loaded solid-modelling kernel describes itself, or null when none is loaded.
    /// </param>
    /// <returns>The lines, in the order they should be shown.</returns>
    public static IReadOnlyList<NoticeLine> Build(string? version, string? kernelDescription)
    {
        List<NoticeLine> lines = [];

        if (!string.IsNullOrWhiteSpace(version))
        {
            lines.Add(new NoticeLine("Version", version));
        }

        lines.Add(new NoticeLine(
            "About",
            "Spark is a visual programming environment for design and engineering: build a graph, "
            + "see the geometry, save the file."));

        if (string.IsNullOrWhiteSpace(kernelDescription))
        {
            lines.Add(new NoticeLine("Solid modelling", NoKernelNotice));
        }
        else
        {
            lines.Add(new NoticeLine("Solid modelling", kernelDescription));
            lines.Add(new NoticeLine(
                "Open CASCADE",
                "This software makes use of facilities provided by the Open CASCADE Technology "
                + "software, licensed under LGPL-2.1 with the Open CASCADE exception. It is linked "
                + "dynamically and its libraries are replaceable. See THIRD-PARTY-NOTICES.md for "
                + "the full text and for the offer of source."));
        }

        lines.Add(new NoticeLine(
            "Licence",
            "Spark itself is MIT licensed. See LICENSE and THIRD-PARTY-NOTICES.md."));

        return lines;
    }

    /// <summary>The notice as plain text, one line per entry, for a console.</summary>
    /// <param name="version">The application version, or null.</param>
    /// <param name="kernelDescription">The kernel description, or null when none is loaded.</param>
    /// <returns>The notice, newline separated.</returns>
    public static string ToText(string? version, string? kernelDescription)
    {
        System.Text.StringBuilder text = new();

        foreach (NoticeLine line in Build(version, kernelDescription))
        {
            text.Append(line.Label).Append(": ").AppendLine(line.Text);
        }

        return text.ToString();
    }
}
