using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;

namespace Spark.Packages;

/// <summary>
/// The <c>tools/spark.json</c> manifest that makes a NuGet package a Spark package
/// (<c>E7-T1</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>NuGet is the registry, and this is the only thing added to it.</b> Protocol, hosting, auth,
/// SemVer, dependency resolution, private feeds and nuget.org's reach all come free by being an
/// ordinary NuGet package; building a registry would be re-solving every one of them worse. What
/// NuGet cannot say is <i>which assemblies in here are node libraries</i>, and that is the whole
/// job of this file.
/// </para>
/// <para>
/// <b>Two things mark a Spark package and both are required.</b> The <c>spark</c> tag, so a search
/// on nuget.org finds it and a human reading the listing knows what it is; and this manifest, so
/// the loader knows what to do with it. A tag with no manifest is a package claiming to be
/// something it has not said how to load, and a manifest with no tag is a package nobody will
/// find.
/// </para>
/// <para>
/// <b>Unknown properties are ignored rather than refused.</b> A package built against a later
/// Spark must still install into an earlier one if its assemblies are compatible, and refusing a
/// field this version has not heard of would make every manifest addition a breaking change.
/// </para>
/// </remarks>
public sealed class SparkPackageManifest
{
    /// <summary>The path inside a package where the manifest lives.</summary>
    public const string PathInPackage = "tools/spark.json";

    /// <summary>The tag a Spark package carries on nuget.org.</summary>
    public const string Tag = "spark";

    /// <summary>The manifest schema version this build writes and understands.</summary>
    public const int CurrentSchema = 1;

    private SparkPackageManifest(
        int schema, ImmutableArray<string> assemblies, string? displayName, string? description)
    {
        Schema = schema;
        Assemblies = assemblies;
        DisplayName = displayName;
        Description = description;
    }

    /// <summary>The manifest schema version the package was written against.</summary>
    public int Schema { get; }

    /// <summary>
    /// The assemblies to load node definitions from, by simple name, in the order given.
    /// </summary>
    /// <remarks>
    /// <b>Named rather than discovered.</b> A package's <c>lib</c> folder also holds everything it
    /// depends on, and reflecting over all of it would import nodes from libraries whose authors
    /// never intended them — every public static method in a maths helper would become a node. The
    /// author says which of their assemblies are node libraries, and only those are read.
    /// </remarks>
    public ImmutableArray<string> Assemblies { get; }

    /// <summary>What to call the package in the library panel, or null to use its id.</summary>
    public string? DisplayName { get; }

    /// <summary>One sentence about the package, or null.</summary>
    public string? Description { get; }

    /// <summary>
    /// Whether this build can load a package written against this manifest.
    /// </summary>
    /// <remarks>
    /// A newer schema is refused rather than guessed at. The manifest is how a package says what
    /// to load; misreading it does not fail here, it fails much later with a node that is absent
    /// and no explanation.
    /// </remarks>
    public bool IsReadable => Schema <= CurrentSchema;

    /// <summary>Parses a manifest.</summary>
    /// <param name="json">The file's text.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="SparkPackageException">
    /// The text is not valid JSON, is not an object, or names no assemblies. A package that names
    /// none has nothing to contribute, and installing it would appear to work and add no nodes.
    /// </exception>
    public static SparkPackageManifest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (JsonException error)
        {
            throw new SparkPackageException($"{PathInPackage} is not valid JSON: {error.Message}");
        }

        using (parsed)
        {
            JsonElement root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new SparkPackageException($"{PathInPackage}'s top level is not an object.");
            }

            int schema = root.TryGetProperty("schema", out JsonElement version)
                && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out int parsedSchema)
                    ? parsedSchema
                    : CurrentSchema;

            ImmutableArray<string>.Builder assemblies = ImmutableArray.CreateBuilder<string>();
            if (root.TryGetProperty("assemblies", out JsonElement list)
                && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in list.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } name)
                    {
                        // Tolerate an author writing "Acme.Nodes.dll"; the loader wants a simple name.
                        assemblies.Add(name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                            ? name[..^4]
                            : name);
                    }
                }
            }

            if (assemblies.Count == 0)
            {
                throw new SparkPackageException(
                    $"{PathInPackage} names no assemblies, so the package has no nodes to contribute. "
                    + "Add an \"assemblies\" array naming the libraries your nodes are in.");
            }

            return new SparkPackageManifest(
                schema,
                assemblies.ToImmutable(),
                Text(root, "displayName"),
                Text(root, "description"));
        }
    }

    /// <summary>Serialises a manifest, for a package author's build.</summary>
    /// <param name="assemblies">The assemblies to load nodes from.</param>
    /// <param name="displayName">What to call the package, or null.</param>
    /// <param name="description">One sentence, or null.</param>
    /// <returns>The manifest text, ending in a newline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assemblies"/> is null.</exception>
    /// <exception cref="ArgumentException">No assembly was named.</exception>
    public static string Write(
        IReadOnlyList<string> assemblies, string? displayName = null, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        if (assemblies.Count == 0)
        {
            throw new ArgumentException(
                "A Spark package manifest must name at least one assembly.", nameof(assemblies));
        }

        System.Text.StringBuilder text = new();
        text.Append("{\n");
        text.Append("  \"schema\": ").Append(CurrentSchema).Append(",\n");

        if (displayName is not null)
        {
            text.Append("  \"displayName\": ").Append(JsonSerializer.Serialize(displayName)).Append(",\n");
        }

        if (description is not null)
        {
            text.Append("  \"description\": ").Append(JsonSerializer.Serialize(description)).Append(",\n");
        }

        text.Append("  \"assemblies\": [\n");
        for (int i = 0; i < assemblies.Count; i++)
        {
            text.Append("    ").Append(JsonSerializer.Serialize(assemblies[i]));
            text.Append(i == assemblies.Count - 1 ? "\n" : ",\n");
        }

        text.Append("  ]\n}\n");
        return text.ToString();
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>Thrown when a package is not one Spark can use, with the reason.</summary>
/// <remarks>
/// A distinct type because installing a package has two quite different failure modes and a user
/// needs to tell them apart: the network or the feed said no, which is worth retrying, and the
/// package is not a Spark package, which is not.
/// </remarks>
public sealed class SparkPackageException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Why the package cannot be used.</param>
    public SparkPackageException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a cause.</summary>
    /// <param name="message">Why the package cannot be used.</param>
    /// <param name="innerException">The cause.</param>
    public SparkPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with no message.</summary>
    public SparkPackageException() : base("The package cannot be used by Spark.")
    {
    }
}
