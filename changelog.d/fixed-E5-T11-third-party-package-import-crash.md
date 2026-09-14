Importing a NuGet package no longer fails when the package contains an abstract class with a
public constructor — the import threw and took every node in the assembly with it.

Abstract classes with public constructors are ordinary in libraries meant to be extended, and no
assembly written for Spark had one, so this affected third-party packages only.
