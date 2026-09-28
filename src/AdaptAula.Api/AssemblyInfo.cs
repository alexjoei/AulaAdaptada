using System.Runtime.CompilerServices;

// Lets tests exercise internal helpers directly (e.g. ExportService's filename builder) instead of
// only through full HTTP round-trips.
[assembly: InternalsVisibleTo("AdaptAula.Tests")]
