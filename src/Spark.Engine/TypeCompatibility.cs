using System;
using System.Collections.Generic;
using System.Reflection;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// Which rule let two ports connect, in the order the rules are tried. The order is the contract:
/// an earlier rule wins, and the first six are the only ways a wire can exist.
/// </summary>
public enum PortCompatibility
{
    /// <summary>No rule matched. The wire is refused at creation time, never at run time.</summary>
    Incompatible = 0,

    /// <summary>The types are identical, or the source is assignable to the target.</summary>
    Direct = 1,

    /// <summary>A numeric widening that cannot lose information — <c>int</c> into <c>double</c>.</summary>
    NumericWidening = 2,

    /// <summary>A converter registered with the session. May be lossy, and says so.</summary>
    RegisteredConverter = 3,

    /// <summary>A user-defined <c>implicit operator</c> found by reflection on either type.</summary>
    ImplicitOperator = 4,

    /// <summary>
    /// The value is lifted in rank to reach the port — a scalar into a list port, or a list into a
    /// scalar port that will replicate over it.
    /// </summary>
    RankLifting = 5,

    /// <summary>The target port is declared <see cref="object"/> and takes anything.</summary>
    ObjectTarget = 6,
}

/// <summary>
/// The outcome of checking one prospective wire.
/// </summary>
/// <param name="Kind">Which rule matched, or <see cref="PortCompatibility.Incompatible"/>.</param>
/// <param name="IsLossy">
/// Whether the conversion may lose information. Lossy connections are accepted but shown yellow,
/// with a tooltip naming the conversion.
/// </param>
/// <param name="Explanation">
/// What happened, phrased for a user. On a refusal this is the message that has to be good enough
/// to act on without opening the source of either node.
/// </param>
public readonly record struct CompatibilityResult(PortCompatibility Kind, bool IsLossy, string Explanation)
{
    /// <summary>Whether a wire may be created.</summary>
    public bool IsAccepted => Kind != PortCompatibility.Incompatible;
}

/// <summary>
/// Decides whether an output port may be wired to an input port, at the moment the user draws the
/// wire rather than at the moment the graph runs.
/// </summary>
/// <remarks>
/// <para>
/// The rules are tried in a fixed order: same-name-different-assembly refusal first, then direct
/// assignability, numeric widening, a registered converter, a reflected <c>implicit operator</c>,
/// rank lifting, and finally an <see cref="object"/> target. Widening and upcasts are automatic;
/// narrowing, parsing and lossy conversions are not, because those are decisions a user should see
/// on the canvas as a node rather than have applied silently inside a wire.
/// </para>
/// <para>
/// <b>The same-name rule earns its place.</b> Two assemblies can each define <c>Acme.Widget</c>, and
/// wiring one into the other produces a runtime <i>cannot cast Widget to Widget</i> that is
/// genuinely impossible to act on. Catching it here turns it into a design-time message naming both
/// packages, which is a bug report a user can write.
/// </para>
/// </remarks>
public sealed class TypeCompatibility
{
    private readonly ConversionRegistry _converters;

    /// <summary>Creates a checker over a set of registered converters.</summary>
    /// <param name="converters">
    /// The converters this session knows about. Nothing here is global: a session owns its
    /// registry, so two sessions in one process cannot alter each other's wiring rules.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="converters"/> is <see langword="null"/>.</exception>
    public TypeCompatibility(ConversionRegistry converters)
    {
        ArgumentNullException.ThrowIfNull(converters);
        _converters = converters;
    }

    /// <summary>A checker with no registered converters.</summary>
    public static TypeCompatibility Default { get; } = new(new ConversionRegistry());

    /// <summary>Checks whether a source port may feed a target port.</summary>
    /// <param name="source">The output port.</param>
    /// <param name="target">The input port.</param>
    /// <returns>Which rule matched, and why.</returns>
    /// <exception cref="ArgumentNullException">Either port is <see langword="null"/>.</exception>
    public CompatibilityResult Check(PortDefinition source, PortDefinition target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        return Check(source.ValueType, target.ValueType, target.KeepStructure);
    }

    /// <summary>Checks whether a value of one type may feed a port of another.</summary>
    /// <param name="sourceType">The type the output port produces.</param>
    /// <param name="targetType">The type the input port declares.</param>
    /// <param name="targetKeepsStructure">
    /// Whether the target port is <see cref="KeepStructureAttribute"/>, in which case rank never
    /// blocks the connection.
    /// </param>
    /// <returns>Which rule matched, and why.</returns>
    /// <exception cref="ArgumentNullException">Either type is <see langword="null"/>.</exception>
    public CompatibilityResult Check(Type sourceType, Type targetType, bool targetKeepsStructure = false)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(targetType);

        // Rule 0, before everything else: two different types with the same full name. This is
        // checked first because every rule below would report it as an ordinary mismatch, and
        // "cannot connect Widget to Widget" is the least useful message in the product.
        if (sourceType != targetType
            && string.Equals(sourceType.FullName, targetType.FullName, StringComparison.Ordinal))
        {
            return new CompatibilityResult(
                PortCompatibility.Incompatible,
                false,
                $"Both ports are typed '{sourceType.FullName}', but from different assemblies: '{AssemblyNameOf(sourceType)}' and '{AssemblyNameOf(targetType)}'. Two packages have shipped the same type. Use one of them on both sides, or convert between them explicitly.");
        }

        if (targetType.IsAssignableFrom(sourceType))
        {
            return new CompatibilityResult(
                PortCompatibility.Direct,
                false,
                sourceType == targetType
                    ? $"Both ports are '{sourceType.Name}'."
                    : $"'{sourceType.Name}' is a '{targetType.Name}'.");
        }

        if (NumericConversions.IsWidening(sourceType, targetType))
        {
            return new CompatibilityResult(
                PortCompatibility.NumericWidening,
                false,
                $"'{sourceType.Name}' widens to '{targetType.Name}' with no loss.");
        }

        if (_converters.TryGet(sourceType, targetType, out ConversionRule? converter) && converter is not null)
        {
            return new CompatibilityResult(
                PortCompatibility.RegisteredConverter,
                converter.IsLossy,
                converter.IsLossy
                    ? $"'{sourceType.Name}' is converted to '{targetType.Name}', which may lose information."
                    : $"'{sourceType.Name}' is converted to '{targetType.Name}'.");
        }

        if (HasImplicitOperator(sourceType, targetType))
        {
            return new CompatibilityResult(
                PortCompatibility.ImplicitOperator,
                true,
                $"'{sourceType.Name}' defines an implicit conversion to '{targetType.Name}'. Spark cannot tell whether it loses information, so it is shown as a conversion.");
        }

        // Rank lifting. A list-of-T output into a T port is not an error: the node replicates over
        // it, which is the ordinary way anything gets built. A T output into a list-of-T port is
        // promotion, which is equally ordinary.
        Type sourceElement = InnermostElementOf(sourceType);
        Type targetElement = InnermostElementOf(targetType);

        if (sourceElement != sourceType || targetElement != targetType)
        {
            CompatibilityResult elementResult = Check(sourceElement, targetElement);
            if (elementResult.IsAccepted)
            {
                return new CompatibilityResult(
                    PortCompatibility.RankLifting,
                    elementResult.IsLossy,
                    $"'{sourceType.Name}' reaches '{targetType.Name}' by replicating or promoting: {elementResult.Explanation}");
            }
        }

        // Last, so that a port declared object still reports the specific rule that let the value
        // through where one applies. This is the catch-all that makes [KeepStructure] ports
        // unwireable-to only by the same-name rule above.
        if (targetKeepsStructure || targetType == typeof(object))
        {
            return new CompatibilityResult(
                PortCompatibility.ObjectTarget, false, "The target port accepts any value.");
        }

        return new CompatibilityResult(
            PortCompatibility.Incompatible,
            false,
            $"'{sourceType.Name}' cannot be connected to a port declared '{targetType.Name}'. Insert a conversion node between them — narrowing and parsing are never applied automatically, so that the conversion is visible on the canvas.");
    }

    private static Type InnermostElementOf(Type type)
    {
        Type current = type;
        while (PortDefinition.ElementTypeOf(current) is { } element)
        {
            current = element;
        }

        return current;
    }

    private static bool HasImplicitOperator(Type sourceType, Type targetType)
    {
        return Declares(sourceType) || Declares(targetType);

        bool Declares(Type declaring)
        {
            foreach (MethodInfo method in declaring.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "op_Implicit", StringComparison.Ordinal)
                    || method.ReturnType != targetType)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == sourceType)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static string AssemblyNameOf(Type type) => type.Assembly.GetName().Name ?? type.Assembly.FullName ?? "<unknown>";
}
