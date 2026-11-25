using System.Xml.Linq;
using static Ilmi.ArchitectureTests.SolutionStructure;

namespace Ilmi.ArchitectureTests;

/// <summary>
/// ASP.NET Core Identity is an Infrastructure detail of Users &amp; Identity. The Domain and Application layers must not depend on it:
/// they see only the Domain User and the IUserAccountManager port.
/// </summary>
public class IdentityIsolationTests
{
    private static string ProjectDir(string layer) =>
        Directory.GetDirectories(Path.Combine(Root, "src", "Modules", "UsersIdentity"), ProjectName("UsersIdentity", layer)).Single();

    private static IEnumerable<string> Packages(string layer) =>
        XDocument.Load(Directory.GetFiles(ProjectDir(layer), "*.csproj").Single())
            .Descendants("PackageReference").Select(e => (string)e.Attribute("Include")!);

    private static IEnumerable<string> SourceFiles(string layer) =>
        Directory.GetFiles(ProjectDir(layer), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Theory]
    [InlineData("Contracts")]
    [InlineData("Domain")]
    [InlineData("Application")]
    [InlineData("Presentation")]
    public void Layer_has_no_Identity_package(string layer) =>
        Assert.DoesNotContain(Packages(layer), p => p.Contains("Identity", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Infrastructure_owns_the_Identity_integration() =>
        Assert.Contains("Microsoft.AspNetCore.Identity.EntityFrameworkCore", Packages("Infrastructure"));

    [Theory]
    [InlineData("Domain")]
    [InlineData("Application")]
    [InlineData("Contracts")]
    public void Layer_source_never_mentions_Identity_types(string layer)
    {
        var offenders = SourceFiles(layer)
            .Where(f => File.ReadAllText(f) is var text &&
                        (text.Contains("Microsoft.AspNetCore.Identity") || text.Contains("UserManager<") || text.Contains("IdentityUser") ||
                         text.Contains("IdentityRole") || text.Contains("IdentityDbContext")))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void The_Domain_User_is_a_plain_class_and_Infrastructure_implements_IUserAccountManager()
    {
        var user = File.ReadAllText(Path.Combine(ProjectDir("Domain"), "Entities", "Users", "User.cs"));
        Assert.DoesNotContain(": IdentityUser", user);
        Assert.Contains("public sealed class User : IAuditable", user);

        var manager = File.ReadAllText(Directory.GetFiles(ProjectDir("Infrastructure"), "UserAccountManager.cs", SearchOption.AllDirectories).Single());
        Assert.Contains(": IUserAccountManager", manager);
        Assert.Contains("UserManager<ApplicationUser>", manager);
    }

    [Fact]
    public void Use_cases_depend_on_the_port_not_on_the_Identity_manager()
    {
        var handlers = SourceFiles("Application").Where(f => f.Contains($"{Path.DirectorySeparatorChar}Commands{Path.DirectorySeparatorChar}")).ToList();
        Assert.NotEmpty(handlers);
        Assert.Contains(handlers, f => File.ReadAllText(f).Contains("IUserAccountManager"));
        Assert.DoesNotContain(handlers, f => File.ReadAllText(f).Contains("UserManager<"));
    }
}
