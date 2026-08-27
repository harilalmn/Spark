using System.Runtime.CompilerServices;

// The tests assert that changing a package's appearance SHARES its geometry arrays rather than
// copying them, which is the whole reason selection is free. That is a statement about the
// internal storage, and it is worth stating: the public surface deliberately exposes spans, which
// cannot be compared by reference.
[assembly: InternalsVisibleTo("Spark.Viewport.Tests")]
