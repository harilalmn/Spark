using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;

namespace Spark.Engine;

/// <summary>
/// Reads the <c>&lt;summary&gt;</c> of a member out of the compiler-generated XML documentation
/// file beside an assembly, so a node's description is the comment its author already wrote.
/// </summary>
/// <remarks>
/// <para>
/// This is the direct answer to the <c>DocGenerator</c> failure ADR-0004 describes: 6,784 lines
/// around three hand-maintained dictionaries keyed by string, of which 101 of 108 constructors
/// rendered blank and seven entries pointed at members that no longer existed. Nothing here is
/// hand-maintained. If a summary is missing, the node simply has no description — which is a
/// visible gap rather than a wrong answer, and on <c>Spark.Nodes.Core</c> it cannot happen at all
/// because CS1591 is an error there.
/// </para>
/// <para>
/// A missing or malformed XML file is not an error. An assembly acquired from NuGet may ship
/// without one, and a node library with no tooltips is a great deal better than an importer that
/// refuses to load it.
/// </para>
/// </remarks>
public sealed class XmlDocumentation
{
    private static readonly XmlDocumentation EmptyInstance = new([]);

    private readonly Dictionary<string, string> _summaries;

    private XmlDocumentation(Dictionary<string, string> summaries) => _summaries = summaries;

    /// <summary>Documentation that knows nothing, which is what a missing file produces.</summary>
    public static XmlDocumentation Empty => EmptyInstance;

    /// <summary>How many summaries were read.</summary>
    public int Count => _summaries.Count;

    /// <summary>
    /// Loads the documentation file beside an assembly, or <see cref="Empty"/> when there is none.
    /// </summary>
    /// <param name="assembly">The assembly whose <c>.xml</c> sibling to read.</param>
    /// <returns>The documentation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is <see langword="null"/>.</exception>
    public static XmlDocumentation For(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string location = assembly.Location;
        if (string.IsNullOrEmpty(location))
        {
            return Empty;
        }

        string path = Path.ChangeExtension(location, ".xml");
        return File.Exists(path) ? Load(path) : Empty;
    }

    /// <summary>Loads a documentation file by path.</summary>
    /// <param name="path">The <c>.xml</c> file.</param>
    /// <returns>The documentation, or <see cref="Empty"/> when the file could not be parsed.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    public static XmlDocumentation Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Dictionary<string, string> summaries = new(StringComparer.Ordinal);

        try
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreWhitespace = false,
            };

            using XmlReader reader = XmlReader.Create(path, settings);
            string? currentMember = null;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "member")
                {
                    currentMember = reader.GetAttribute("name");
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element
                    || reader.Name != "summary"
                    || currentMember is null)
                {
                    continue;
                }

                string summary = Collapse(reader.ReadInnerXml());
                if (summary.Length > 0)
                {
                    summaries[currentMember] = summary;
                }
            }
        }
        catch (XmlException)
        {
            return Empty;
        }
        catch (IOException)
        {
            return Empty;
        }

        return new XmlDocumentation(summaries);
    }

    /// <summary>The summary for a member, or <see langword="null"/>.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The summary as one line of plain text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <see langword="null"/>.</exception>
    public string? SummaryOf(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return _summaries.TryGetValue(KeyOf(member), out string? summary) ? summary : null;
    }

    /// <summary>
    /// The documentation-comment id of a member: the <c>M:</c>, <c>P:</c>, <c>T:</c> or <c>F:</c>
    /// string the compiler writes into the XML file.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>The id.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <see langword="null"/>.</exception>
    public static string KeyOf(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return member switch
        {
            Type type => "T:" + TypeName(type),
            MethodBase method => "M:" + MethodName(method),
            PropertyInfo property => "P:" + TypeName(property.DeclaringType!) + "." + property.Name,
            FieldInfo field => "F:" + TypeName(field.DeclaringType!) + "." + field.Name,
            _ => "E:" + TypeName(member.DeclaringType!) + "." + member.Name,
        };
    }

    private static string MethodName(MethodBase method)
    {
        StringBuilder builder = new();
        builder.Append(TypeName(method.DeclaringType!)).Append('.');
        builder.Append(method is ConstructorInfo ? "#ctor" : method.Name);

        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            return builder.ToString();
        }

        builder.Append('(');
        for (int index = 0; index < parameters.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(ParameterTypeName(parameters[index].ParameterType));
        }

        builder.Append(')');
        return builder.ToString();
    }

    private static string ParameterTypeName(Type type) =>
        type.IsByRef ? TypeName(type.GetElementType()!) + "@" : TypeName(type);

    private static string TypeName(Type type)
    {
        if (type.IsArray)
        {
            return TypeName(type.GetElementType()!) + "[]";
        }

        string name = type.FullName ?? type.Name;
        return name.Replace('+', '.');
    }

    private static string Collapse(string xml)
    {
        StringBuilder builder = new(xml.Length);
        bool inTag = false;
        bool lastWasSpace = true;

        foreach (char character in xml)
        {
            if (character == '<')
            {
                inTag = true;
                continue;
            }

            if (character == '>')
            {
                inTag = false;
                continue;
            }

            if (inTag)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(character);
            lastWasSpace = false;
        }

        return builder.ToString().Trim();
    }
}
