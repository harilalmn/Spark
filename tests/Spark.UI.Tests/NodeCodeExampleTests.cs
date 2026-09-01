using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Engine;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// Every generated code-block example on a node's help page is compiled, through the same
/// <see cref="ScriptNodeFactory"/> a real code block gets (<c>E10-T5</c>, <c>E11-T2</c>).
/// </summary>
/// <remarks>
/// <b>An example that does not compile is worse than no example</b>, because a reader who pastes it
/// blames themselves. This is the same argument <c>DocumentationSampleTests</c> makes for the
/// hand-written fences, applied to the generated ones - and it is stronger here, because these are
/// produced by a rule rather than typed, so one broken shape breaks a hundred pages at once.
/// </remarks>
public sealed class NodeCodeExampleTests
{
    [Fact]
    public void EveryNodeHasACodeExample()
    {
        List<string> missing = [.. Core()
            .Where(node => string.IsNullOrWhiteSpace(node.CodeExample))
            .Select(node => node.Key.Value)];

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryCodeExampleCompilesAndHasTheNodesPorts()
    {
        ScriptNodeFactory factory = new();
        List<string> failures = [];

        foreach (NodeDefinition node in Core())
        {
            if (string.IsNullOrWhiteSpace(node.CodeExample))
            {
                continue;
            }

            try
            {
                NodeDefinitionSource block = factory.Create(node.CodeExample);
                string[] expected = [.. node.Inputs.Select(port => port.Name)];
                string[] actual = [.. block.Inputs.Select(port => port.Name)];

                if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                {
                    failures.Add($"{node.Key.Value}: ports [{string.Join(", ", actual)}] but the node has [{string.Join(", ", expected)}]");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{node.Key.Value}: {ex.Message}{Environment.NewLine}{node.CodeExample}");
            }
        }

        Assert.Empty(failures);
    }

    private static IReadOnlyList<NodeDefinition> Core() =>
        [.. NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly).Nodes.Select(n => n.Definition)];
}
