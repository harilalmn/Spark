using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// Turns the public members of an assembly into <see cref="NodeDefinition"/>s by reflection, with
/// no cooperation required from the assembly's author.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zero configuration is the whole design.</b> A public static method is a node with no
/// attribute on it at all, because third-party assemblies have to produce a sane library without
/// having been written for Spark. <c>Spark.Nodes.Core</c> goes through this same path — it cannot
/// reference <c>Spark.Engine</c> and therefore cannot register anything by hand — so the importer
/// cannot break for everyone else while our own library keeps working (ADR-0005, rule 2).
/// </para>
/// <para>
/// <b>Every public member is accounted for, in both directions.</b> The result is an
/// <see cref="ImportReport"/> in which each public member is either a node or an exclusion carrying
/// a reason. That is what the two-way coverage test asserts, and it is why the importer records a
/// reason for things it cannot yet handle instead of skipping them quietly. A silent skip passes
/// every test written after the fact.
/// </para>
/// <para>
/// <b>What this slice deliberately does not do.</b> Generic types and generic methods, extension
/// methods surfaced on their receiver, operator harvesting, nested types, indexers, events and
/// <c>ref</c> parameters are all excluded with a stated reason rather than imported. Each is a
/// design decision of its own — how does a user pick a type argument on a canvas? — and none of
/// them is needed to make a graph draw geometry.
/// </para>
/// <para>
/// <b>ADR-0004 dedup.</b> A public constructor is suppressed when a public static
/// <c>By*</c>/<c>From*</c>/<c>Create*</c> method on the same type returns that type and has the
/// same <i>parameter type sequence</i>. Types, not names: <c>centre</c> against <c>center</c> would
/// fail a name match and emit both nodes, which is the exact outcome the rule exists to prevent.
/// </para>
/// </remarks>
public static class NodeImporter
{
    private const string DefaultOutputPortName = "result";

    /// <summary>Imports every public type in an assembly.</summary>
    /// <param name="assembly">The assembly to read.</param>
    /// <param name="package">
    /// The package identity every generated <see cref="NodeKey"/> carries. Defaults to the
    /// assembly's simple name, which is what makes two packages' <c>Curve.Offset</c> distinct.
    /// </param>
    /// <returns>The nodes and the exclusions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is <see langword="null"/>.</exception>
    public static ImportReport Import(Assembly assembly, string? package = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string identity = package ?? assembly.GetName().Name ?? "Unknown";
        return Import(assembly.GetExportedTypes(), identity, XmlDocumentation.For(assembly));
    }

    /// <summary>Imports a specific set of types.</summary>
    /// <param name="types">The types to read. Non-public types are excluded with a reason.</param>
    /// <param name="package">The package identity every generated key carries.</param>
    /// <param name="documentation">
    /// Where node descriptions come from, or <see langword="null"/> for none.
    /// </param>
    /// <returns>The nodes and the exclusions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="package"/> is blank.</exception>
    public static ImportReport Import(
        IEnumerable<Type> types, string package, XmlDocumentation? documentation = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentException.ThrowIfNullOrWhiteSpace(package);

        XmlDocumentation docs = documentation ?? XmlDocumentation.Empty;
        List<Candidate> candidates = [];
        List<ExcludedMember> exclusions = [];

        foreach (Type type in types)
        {
            if (ExcludeTypeReason(type) is { } typeReason)
            {
                exclusions.Add(new ExcludedMember(type, typeReason));
                continue;
            }

            CollectType(type, docs, candidates, exclusions);
        }

        return new ImportReport(package, Build(candidates, package), exclusions);
    }

    private static string? ExcludeTypeReason(Type type)
    {
        if (type.GetCustomAttribute<NodeIgnoreAttribute>() is { } ignored)
        {
            return ignored.Reason;
        }

        if (type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
        {
            return "compiler-generated types are not part of the author's public surface.";
        }

        if (type.IsNested)
        {
            return "nested types are not imported in this slice; hoist the type to import it.";
        }

        if (type.IsGenericTypeDefinition || type.ContainsGenericParameters)
        {
            return "generic types are not imported in this slice: a canvas has no way to bind a type argument.";
        }

        if (type.IsEnum)
        {
            return "an enum is a value set, not an operation; its members become port literals.";
        }

        if (type.IsInterface)
        {
            return "an interface declares no implementation to invoke.";
        }

        if (typeof(Delegate).IsAssignableFrom(type))
        {
            return "a delegate type is a function value, not a node.";
        }

        if (typeof(Attribute).IsAssignableFrom(type))
        {
            return "an attribute is authoring metadata, and its members never run in a graph.";
        }

        return null;
    }

    private static void CollectType(
        Type type, XmlDocumentation docs, List<Candidate> candidates, List<ExcludedMember> exclusions)
    {
        MemberInfo[] members = type.GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        // ADR-0004. Collected before anything else, because deciding whether a constructor survives
        // needs the whole set of factories on the type, not the ones seen so far.
        List<MethodInfo> factories = [.. members
            .OfType<MethodInfo>()
            .Where(method => IsFacadeFactory(method, type))];

        foreach (MemberInfo member in members)
        {
            if (member.GetCustomAttribute<NodeIgnoreAttribute>() is { } ignored)
            {
                exclusions.Add(new ExcludedMember(member, ignored.Reason));
                continue;
            }

            switch (member)
            {
                case MethodInfo method:
                    Classify(method, type, docs, candidates, exclusions);
                    break;

                case ConstructorInfo constructor:
                    ClassifyConstructor(constructor, type, factories, docs, candidates, exclusions);
                    break;

                case PropertyInfo property:
                    ClassifyProperty(property, type, docs, candidates, exclusions);
                    break;

                case FieldInfo field:
                    exclusions.Add(new ExcludedMember(
                        field,
                        "a field is a value rather than an operation; a node that returns a constant is written as a method."));
                    break;

                case EventInfo eventInfo:
                    exclusions.Add(new ExcludedMember(
                        eventInfo, "an event is a callback, and a dataflow graph has nowhere to put one."));
                    break;

                case Type nested:
                    exclusions.Add(new ExcludedMember(
                        nested, "nested types are not imported in this slice; hoist the type to import it."));
                    break;

                default:
                    exclusions.Add(new ExcludedMember(
                        member, $"member kind {member.MemberType} is not imported in this slice."));
                    break;
            }
        }
    }

    private static void Classify(
        MethodInfo method,
        Type type,
        XmlDocumentation docs,
        List<Candidate> candidates,
        List<ExcludedMember> exclusions)
    {
        if (method.IsSpecialName)
        {
            exclusions.Add(new ExcludedMember(method, SpecialNameReason(method)));
            return;
        }

        if (method.ContainsGenericParameters)
        {
            exclusions.Add(new ExcludedMember(
                method, "generic methods are not imported in this slice: a canvas has no way to bind a type argument."));
            return;
        }

        if (method.IsDefined(typeof(ExtensionAttribute), inherit: false))
        {
            exclusions.Add(new ExcludedMember(
                method, "extension methods are not surfaced on their receiver in this slice."));
            return;
        }

        ParameterInfo[] parameters = method.GetParameters();

        if (parameters.Any(parameter => parameter.ParameterType.IsByRef && !parameter.IsOut))
        {
            exclusions.Add(new ExcludedMember(
                method, "a ref or in parameter is both an input and an output, which no port shape expresses."));
            return;
        }

        if (parameters.Any(parameter => parameter.ParameterType.IsPointer))
        {
            exclusions.Add(new ExcludedMember(method, "a pointer parameter has no graph value."));
            return;
        }

        if (method.ReturnType == typeof(void) && !parameters.Any(parameter => parameter.IsOut))
        {
            exclusions.Add(new ExcludedMember(
                method, "the method returns void and has no out parameter, so it produces no value a graph can carry."));
            return;
        }

        List<PortDefinition> inputs = [];
        if (!method.IsStatic)
        {
            inputs.Add(ReceiverPort(type));
        }

        List<PortDefinition> outputs = [];
        if (method.ReturnType != typeof(void))
        {
            outputs.Add(ReturnPort(method));
        }

        foreach (ParameterInfo parameter in parameters)
        {
            if (parameter.IsOut)
            {
                outputs.Add(OutPort(parameter));
            }
            else
            {
                inputs.Add(InputPort(parameter));
            }
        }

        candidates.Add(new Candidate(
            $"{type.Name}.{method.Name}",
            method,
            type,
            inputs,
            outputs,
            NodeInvoker.ForMethod(method),
            docs.SummaryOf(method)));
    }

    private static void ClassifyConstructor(
        ConstructorInfo constructor,
        Type type,
        List<MethodInfo> factories,
        XmlDocumentation docs,
        List<Candidate> candidates,
        List<ExcludedMember> exclusions)
    {
        if (constructor.ContainsGenericParameters)
        {
            exclusions.Add(new ExcludedMember(
                constructor, "generic types are not imported in this slice: a canvas has no way to bind a type argument."));
            return;
        }

        ParameterInfo[] parameters = constructor.GetParameters();

        if (parameters.Any(parameter => parameter.ParameterType.IsByRef || parameter.ParameterType.IsPointer))
        {
            exclusions.Add(new ExcludedMember(
                constructor, "a by-reference or pointer parameter has no port shape."));
            return;
        }

        if (MatchingFactory(constructor, factories) is { } factory)
        {
            exclusions.Add(new ExcludedMember(
                constructor,
                $"ADR-0004: superseded by the By* facade {type.Name}.{factory.Name}, which has the same parameter type sequence."));
            return;
        }

        List<PortDefinition> inputs = [.. parameters.Select(InputPort)];

        candidates.Add(new Candidate(
            $"{type.Name}.{ConstructorSuffix(parameters)}",
            constructor,
            type,
            inputs,
            [new PortDefinition(CamelCase(type.Name), type, PortDefinition.RankOfType(type))],
            NodeInvoker.ForConstructor(constructor),
            docs.SummaryOf(constructor)));
    }

    private static void ClassifyProperty(
        PropertyInfo property,
        Type type,
        XmlDocumentation docs,
        List<Candidate> candidates,
        List<ExcludedMember> exclusions)
    {
        if (property.GetIndexParameters().Length > 0)
        {
            exclusions.Add(new ExcludedMember(
                property, "an indexer is not imported in this slice; a list-item node covers the same ground."));
            return;
        }

        MethodInfo? getter = property.GetGetMethod();
        if (getter is null)
        {
            exclusions.Add(new ExcludedMember(
                property, "a write-only property only mutates, and graph values are immutable."));
            return;
        }

        List<PortDefinition> inputs = [];
        if (!getter.IsStatic)
        {
            inputs.Add(ReceiverPort(type));
        }

        candidates.Add(new Candidate(
            $"{type.Name}.{property.Name}",
            property,
            type,
            inputs,
            [new PortDefinition(
                CamelCase(property.Name), property.PropertyType, PortDefinition.RankOfType(property.PropertyType))],
            NodeInvoker.ForMethod(getter),
            docs.SummaryOf(property)));
    }

    private static string SpecialNameReason(MethodInfo method)
    {
        if (method.Name.StartsWith("op_", StringComparison.Ordinal))
        {
            return "operator harvesting is a later slice; the named method it forwards to is the node.";
        }

        if (method.Name.StartsWith("get_", StringComparison.Ordinal)
            || method.Name.StartsWith("set_", StringComparison.Ordinal))
        {
            return "a property accessor is accounted for by the property itself.";
        }

        return "a compiler-generated special-name method is not part of the author's public surface.";
    }

    private static bool IsFacadeFactory(MethodInfo method, Type type) =>
        method.IsStatic
        && !method.IsSpecialName
        && method.ReturnType == type
        && !method.ContainsGenericParameters
        && (method.Name.StartsWith("By", StringComparison.Ordinal)
            || method.Name.StartsWith("From", StringComparison.Ordinal)
            || method.Name.StartsWith("Create", StringComparison.Ordinal));

    private static MethodInfo? MatchingFactory(ConstructorInfo constructor, List<MethodInfo> factories)
    {
        Type[] wanted = [.. constructor.GetParameters().Select(parameter => parameter.ParameterType)];

        foreach (MethodInfo factory in factories)
        {
            Type[] offered = [.. factory.GetParameters().Select(parameter => parameter.ParameterType)];
            if (offered.Length == wanted.Length && offered.SequenceEqual(wanted))
            {
                return factory;
            }
        }

        return null;
    }

    private static PortDefinition ReceiverPort(Type type) =>
        new(CamelCase(type.Name), type, PortDefinition.RankOfType(type));

    private static PortDefinition InputPort(ParameterInfo parameter)
    {
        Type type = parameter.ParameterType;
        if (type.IsByRef)
        {
            type = type.GetElementType()!;
        }

        NodePortAttribute? port = parameter.GetCustomAttribute<NodePortAttribute>();
        ReplicationGuideAttribute? guide = parameter.GetCustomAttribute<ReplicationGuideAttribute>();

        return new PortDefinition(
            port?.Name ?? parameter.Name ?? $"arg{parameter.Position}",
            type,
            PortDefinition.RankOfType(type),
            port?.Description,
            parameter.GetCustomAttribute<KeepStructureAttribute>() is not null,
            parameter.GetCustomAttribute<NoReplicationAttribute>() is not null,
            guide?.Guide,
            DefaultValueOf(parameter, type));
    }

    /// <summary>
    /// The value an unwired port starts with.
    /// </summary>
    /// <remarks>
    /// Two cases the framework gets wrong for us. A <c>default</c> struct default arrives from
    /// reflection as <see langword="null"/>, and a port with no default at all still has to hold
    /// <i>something</i>, or every freshly placed node errors before the user has done anything.
    /// Both resolve to the type's zero value, which is the value the C# signature already means.
    /// </remarks>
    private static object? DefaultValueOf(ParameterInfo parameter, Type type)
    {
        if (parameter.HasDefaultValue && parameter.DefaultValue is { } declared)
        {
            return declared;
        }

        return type.IsValueType && Nullable.GetUnderlyingType(type) is null
            ? Activator.CreateInstance(type)
            : null;
    }

    private static PortDefinition ReturnPort(MethodInfo method)
    {
        NodePortAttribute? port = method.ReturnParameter.GetCustomAttribute<NodePortAttribute>();
        return new PortDefinition(
            port?.Name ?? DefaultOutputPortName,
            method.ReturnType,
            PortDefinition.RankOfType(method.ReturnType),
            port?.Description);
    }

    private static PortDefinition OutPort(ParameterInfo parameter)
    {
        Type type = parameter.ParameterType.GetElementType()!;
        NodePortAttribute? port = parameter.GetCustomAttribute<NodePortAttribute>();

        return new PortDefinition(
            port?.Name ?? parameter.Name ?? $"out{parameter.Position}",
            type,
            PortDefinition.RankOfType(type),
            port?.Description);
    }

    private static string ConstructorSuffix(ParameterInfo[] parameters)
    {
        if (parameters.Length == 0)
        {
            return "Create";
        }

        StringBuilder builder = new("By");
        foreach (ParameterInfo parameter in parameters)
        {
            builder.Append(PascalCase(parameter.Name ?? "arg"));
        }

        return builder.ToString();
    }

    private static IReadOnlyList<ImportedNode> Build(List<Candidate> candidates, string package)
    {
        Dictionary<string, int> nameCounts = new(StringComparer.Ordinal);
        foreach (Candidate candidate in candidates)
        {
            nameCounts[candidate.Name] = nameCounts.GetValueOrDefault(candidate.Name) + 1;
        }

        // Overloads stay one node each and are disambiguated by their differing parameter names
        // rather than by a numeric suffix (ADR-0004). A numeric suffix would make the second
        // overload's key depend on declaration order, which reflection does not guarantee.
        Dictionary<string, int> used = new(StringComparer.Ordinal);
        List<ImportedNode> nodes = new(candidates.Count);

        foreach (Candidate candidate in candidates)
        {
            string name = nameCounts[candidate.Name] > 1
                ? $"{candidate.Name}({string.Join(", ", candidate.Inputs.Select(port => port.Name))})"
                : candidate.Name;

            int seen = used.GetValueOrDefault(name);
            used[name] = seen + 1;
            if (seen > 0)
            {
                name = string.Create(CultureInfo.InvariantCulture, $"{name}#{seen + 1}");
            }

            SparkNodeAttribute? memberAttribute = candidate.Member.GetCustomAttribute<SparkNodeAttribute>();
            SparkNodeAttribute? typeAttribute = candidate.DeclaringType.GetCustomAttribute<SparkNodeAttribute>();

            bool sideEffect = candidate.Member.IsDefined(typeof(NodeSideEffectAttribute), inherit: false)
                || candidate.DeclaringType.IsDefined(typeof(NodeSideEffectAttribute), inherit: false);

            NodeDefinition definition = new(
                new NodeKey(package, memberAttribute?.Name ?? name),
                memberAttribute?.Name ?? name,
                candidate.Inputs,
                candidate.Outputs,
                candidate.Invoke,
                memberAttribute?.DefaultLacing ?? typeAttribute?.DefaultLacing ?? LacingMode.Longest,
                version: 1,
                isSideEffect: sideEffect,
                description: candidate.Description,
                category: memberAttribute?.Category ?? typeAttribute?.Category ?? NodeCategories.Custom);

            nodes.Add(new ImportedNode(definition, candidate.Member));
        }

        return nodes;
    }

    private static string CamelCase(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static string PascalCase(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    private sealed record Candidate(
        string Name,
        MemberInfo Member,
        Type DeclaringType,
        IReadOnlyList<PortDefinition> Inputs,
        IReadOnlyList<PortDefinition> Outputs,
        NodeInvocation Invoke,
        string? Description);
}
