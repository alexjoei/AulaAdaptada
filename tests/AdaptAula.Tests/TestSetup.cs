using System.Runtime.CompilerServices;
using QuestPDF.Infrastructure;

namespace AdaptAula.Tests;

internal static class TestSetup
{
    // QuestPDF requires this to be set once before generating any document — Program.cs does it for
    // the running API, but the test process never runs Program.cs, so PdfExporterTests needs its own.
    [ModuleInitializer]
    public static void Init() => QuestPDF.Settings.License = LicenseType.Community;
}
