namespace Spark.Api;

/// <summary>
/// Turns the source of a code block into a node definition.
/// </summary>
/// <remarks>
/// <para>
/// <b>This interface is the whole reason a graph with no code blocks never loads the scripting
/// assembly.</b> A code block's node definition cannot come from a library — its ports depend on
/// what the user typed, so it belongs to one node instance and is built when the graph is opened.
/// That build needs Roslyn, and Roslyn is thirty megabytes nobody who drew a box should pay for.
/// So the engine holds this contract, the host supplies an implementation when scripting is
/// available, and a document with no scripts in it never asks.
/// </para>
/// <para>
/// It also gives <c>--no-script</c> its meaning at exactly the right place
/// (<c>E6-T16</c>): running with scripting disabled is passing no factory, and a graph that
/// contains a code block then <b>fails to open with a diagnostic that names the node</b> rather
/// than opening with a node quietly missing. **A Spark graph is executable code**, and a switch
/// that silently dropped the executable parts would be worse than no switch.
/// </para>
/// </remarks>
public interface IScriptNodeFactory
{
    /// <summary>
    /// Builds the node definition a piece of script describes.
    /// </summary>
    /// <param name="script">The source the user typed. Never null; may be empty.</param>
    /// <returns>
    /// The definition, whose ports are whatever the script's free identifiers and return shape
    /// imply.
    /// </returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="script"/> is null.</exception>
    /// <remarks>
    /// <b>A script that does not compile still produces a definition.</b> A node that vanished
    /// from the canvas because of a typo would take its wires with it, and the user would have to
    /// rebuild them after fixing a semicolon. The definition a broken script yields carries the
    /// ports that could still be inferred and reports the compilation failure when it is evaluated
    /// — which is where a failure belongs, because that is where the user is looking.
    /// </remarks>
    NodeDefinitionSource Create(string script);
}

/// <summary>
/// What a script node factory returns: enough to build a definition without the engine knowing how
/// a script becomes one.
/// </summary>
/// <param name="Name">The display name, which is the node's title on the canvas.</param>
/// <param name="ContentHash">
/// A stable hash of the script's meaning. Two nodes whose scripts hash the same share a compiled
/// assembly and a cache entry — which is what makes ten copies of a snippet compile once — so it
/// must change whenever behaviour changes and must not change when only whitespace does.
/// </param>
/// <param name="Inputs">The input ports the script's free identifiers imply.</param>
/// <param name="Outputs">The output ports its return shape implies.</param>
/// <param name="Invoke">What running the script does.</param>
public readonly record struct NodeDefinitionSource(
    string Name,
    string ContentHash,
    System.Collections.Generic.IReadOnlyList<ScriptPort> Inputs,
    System.Collections.Generic.IReadOnlyList<ScriptPort> Outputs,
    ScriptInvocation Invoke);

/// <summary>
/// Runs a script once, with one argument per input port, and returns one value per output port.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one invocation in Spark that takes a <see cref="System.Threading.CancellationToken"/>,
/// and the asymmetry is deliberate</b> (`E6-T17`). A library node is a method call that was written
/// by somebody who intended it to return; a code block is the only thing in a graph whose author
/// can write <c>while (true) { }</c> without meaning to, and the only thing that can therefore take
/// the application with it. Handing every node a token it ignores would spread the cost of that one
/// hazard across the whole node model.
/// </para>
/// <para>
/// <b>Holding the token is not the same as honouring it.</b> Nothing the compiler emits from a
/// user's source checks a token on its own, so this signature is the channel and not the mechanism:
/// the guard weaver (`E6-T4`) is what rewrites a loop to test it. What the token buys on its own is
/// that a script never *starts* once evaluation has been cancelled.
/// </para>
/// </remarks>
/// <param name="arguments">One argument per input port, in port order.</param>
/// <param name="cancellationToken">The evaluation's token.</param>
/// <returns>One value per output port, in port order.</returns>
public delegate object?[] ScriptInvocation(
    object?[] arguments, System.Threading.CancellationToken cancellationToken);

/// <summary>One port of a script node.</summary>
/// <param name="Name">The port's name, which is the identifier the script uses.</param>
/// <param name="ValueType">The type it carries.</param>
/// <param name="Description">What it is for, or null.</param>
public readonly record struct ScriptPort(
    string Name, System.Type ValueType, string? Description = null);
