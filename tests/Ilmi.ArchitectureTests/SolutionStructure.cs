using System.Xml.Linq;

namespace Ilmi.ArchitectureTests;

/// <summary>Reads the solution's .csproj files so the rules can be checked without loading any assembly.</summary>
internal static class SolutionStructure
{
    public static readonly string[] Modules =
        ["UsersIdentity", "Family", "Notifications", "Platform", "Support", "Cms"];

    public static readonly string[] Layers = ["Contracts", "Domain", "Application", "Infrastructure", "Presentation"];

    public static string Root { get; } = FindRoot();

    public static string ProjectName(string module, string layer) => $"Ilmi.Modules.{module}.{layer}";

    /// <summary>Names of the projects that <paramref name="project"/> references.</summary>
    public static IReadOnlyList<string> ReferencesOf(string project)
    {
        var path = Directory.GetFiles(Path.Combine(Root, "src"), project + ".csproj", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(Root, "tests"), project + ".csproj", SearchOption.AllDirectories))
            .Single();

        return XDocument.Load(path)
            .Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/')))
            .ToList();
    }

    public static bool ProjectExists(string project) =>
        Directory.GetFiles(Path.Combine(Root, "src"), project + ".csproj", SearchOption.AllDirectories).Length == 1;

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ilmi.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Ilmi.sln not found above the test output folder.");
    }
}
