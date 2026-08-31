using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Engine;

/// <summary>
/// The types a user can declare for a code block's input port, and the tokens they are written
/// as in a `.spark` file (<c>E6-T11</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A code block's input types normally come from its wires and this is the other source.</b>
/// Before anything is connected a port is <c>dynamic</c>, so the editor completes <c>radius.</c>
/// into the members of <c>object</c> — which is worse than offering nothing, because it looks like
/// an answer. A declaration gives the compiler a real type to work with at the moment the code is
/// being written, which is the moment completion is worth having.
/// </para>
/// <para>
/// <b>The catalogue is short on purpose, and every entry is a scalar.</b> A port that takes a list
/// is not a separate entry: replication already maps a list into a scalar port, and the design
/// language puts listness on the port's shape rather than in its name
/// (<c>docs/help/concepts/design-language.md</c> §7.6). Offering "list of number" beside "number"
/// would double the dropdown to say something the canvas already says, and would invite somebody
/// to declare a list where the lacing was going to handle it.
/// </para>
/// <para>
/// <b>The token is written to the file, and the <see cref="Type"/> is not.</b> An assembly-qualified
/// name would bind a saved graph to an assembly version; a short token survives a rename, a move
/// between assemblies and a target-framework bump. A token this does not recognise loads as no
/// declaration at all rather than failing the open — a file from a later version of Spark should
/// cost a user the setting, not the graph.
/// </para>
/// </remarks>
public static class ScriptInputTypes
{
    // Ordered as the dropdown shows them: the things people type first, then geometry, and
    // `anything` at the top because it is the default and the way back from a wrong choice.
    private static readonly (string Token, Type Type)[] CatalogueEntries =
    [
        ("any", typeof(object)),
        ("number", typeof(double)),
        ("integer", typeof(int)),
        ("text", typeof(string)),
        ("bool", typeof(bool)),
        ("degrees", typeof(Angle)),
        ("point", typeof(Point3d)),
        ("vector", typeof(Vector3d)),
        ("plane", typeof(Plane)),
        ("curve", typeof(Curve)),
        ("surface", typeof(Surface)),
        ("solid", typeof(Brep)),
        ("mesh", typeof(Mesh)),
    ];

    private static readonly Dictionary<string, Type> ByToken = BuildByToken();

    private static readonly Dictionary<Type, string> ByType = BuildByType();

    /// <summary>
    /// Every declarable type, in the order a dropdown should offer them.
    /// </summary>
    /// <remarks>
    /// <c>any</c> is first because it is what a port already is and is therefore the way back from
    /// a declaration the user regrets. Declaring it is not the same as declaring nothing — see
    /// <see cref="IsDefault"/>.
    /// </remarks>
    public static IReadOnlyList<(string Token, Type Type)> Catalogue => CatalogueEntries;

    /// <summary>The type a token names, or <see langword="null"/> if nothing does.</summary>
    /// <param name="token">The token as it appears in a file.</param>
    /// <returns>The type, or null for an unrecognised token.</returns>
    /// <remarks>
    /// Null rather than an exception. An unknown token is what a graph saved by a later version of
    /// Spark looks like, and refusing to open it would lose the whole document over one setting.
    /// </remarks>
    public static Type? Resolve(string? token) =>
        token is not null && ByToken.TryGetValue(token, out Type? type) ? type : null;

    /// <summary>The token for a type, or <see langword="null"/> if it is not declarable.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The token, or null when the type is outside the catalogue.</returns>
    /// <remarks>
    /// A type can reach a port without being in this list — a wire carries whatever its upstream
    /// output is declared as, and the catalogue is only what a person may *choose*. So a null here
    /// means "there is no way to write this down", which is the caller's cue to save nothing rather
    /// than to invent a spelling.
    /// </remarks>
    public static string? TokenFor(Type? type) =>
        type is not null && ByType.TryGetValue(type, out string? token) ? token : null;

    /// <summary>
    /// Whether a type is the one a port has when nothing has been declared for it.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>True for <see cref="object"/>.</returns>
    /// <remarks>
    /// Used by the panel to decide whether the dropdown is showing a real choice. The engine keeps
    /// the distinction — declaring <c>any</c> is a declaration, and it *overrides a wire*, which is
    /// occasionally what somebody wants — but a user who has never touched the dropdown and one who
    /// has set it back to <c>any</c> should not be told they are in different states.
    /// </remarks>
    public static bool IsDefault(Type? type) => type == typeof(object);

    private static Dictionary<string, Type> BuildByToken()
    {
        Dictionary<string, Type> map = new(StringComparer.Ordinal);
        foreach ((string token, Type type) in CatalogueEntries)
        {
            map[token] = type;
        }

        return map;
    }

    private static Dictionary<Type, string> BuildByType()
    {
        Dictionary<Type, string> map = [];
        foreach ((string token, Type type) in CatalogueEntries)
        {
            map[type] = token;
        }

        return map;
    }
}
