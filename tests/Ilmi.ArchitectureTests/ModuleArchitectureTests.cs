using static Ilmi.ArchitectureTests.SolutionStructure;

namespace Ilmi.ArchitectureTests;

/// <summary>
/// Enforces Modular Monolith boundaries and the Clean Architecture dependency direction *inside every module*.
/// Rules are checked against project references, so a violating reference fails the build pipeline.
/// </summary>
public class ModuleArchitectureTests
{
    private const string SharedKernel = "Ilmi.SharedKernel";

    public static TheoryData<string> AllModules => new(Modules);

    [Theory, MemberData(nameof(AllModules))]
    public void Module_HasAllFiveLayerProjects(string module)
    {
        foreach (var layer in Layers)
            Assert.True(ProjectExists(ProjectName(module, layer)), $"Missing project {ProjectName(module, layer)}");
    }

    [Theory, MemberData(nameof(AllModules))]
    public void Contracts_ReferenceNothing(string module) =>
        Assert.Empty(ReferencesOf(ProjectName(module, "Contracts")));

    [Theory, MemberData(nameof(AllModules))]
    public void Domain_ReferencesOnlySharedKernel(string module) =>
        Assert.Equal([SharedKernel], ReferencesOf(ProjectName(module, "Domain")));

    [Theory, MemberData(nameof(AllModules))]
    public void Application_ReferencesOnlyOwnDomainContractsAndSharedKernel(string module)
    {
        var allowed = new[]
        {
            ProjectName(module, "Domain"), ProjectName(module, "Contracts"), SharedKernel
        };
        AssertOnlyAllowed(ProjectName(module, "Application"), module, allowed);
    }

    [Theory, MemberData(nameof(AllModules))]
    public void Infrastructure_ReferencesOnlyOwnApplication(string module) =>
        AssertOnlyAllowed(ProjectName(module, "Infrastructure"), module,
            [ProjectName(module, "Application"), ProjectName(module, "Domain"), ProjectName(module, "Contracts"), SharedKernel]);

    [Theory, MemberData(nameof(AllModules))]
    public void Presentation_DoesNotReferenceDomainOrInfrastructure(string module) =>
        AssertOnlyAllowed(ProjectName(module, "Presentation"), module,
            [ProjectName(module, "Application"), ProjectName(module, "Contracts"), SharedKernel]);

    [Theory, MemberData(nameof(AllModules))]
    public void Modules_ReachEachOtherOnlyThroughContracts(string module)
    {
        foreach (var layer in Layers)
        {
            var offending = ReferencesOf(ProjectName(module, layer))
                .Where(r => r.StartsWith("Ilmi.Modules.") && !r.StartsWith($"Ilmi.Modules.{module}."))
                .Where(r => !r.EndsWith(".Contracts"));
            Assert.Empty(offending);
        }
    }

    [Fact]
    public void SharedKernel_ReferencesNothing() =>
        Assert.Empty(ReferencesOf(SharedKernel));

    [Fact]
    public void Hosts_AreNotReferencedByAnyModule()
    {
        foreach (var module in Modules)
            foreach (var layer in Layers)
                Assert.DoesNotContain(ReferencesOf(ProjectName(module, layer)), r => r is "Ilmi.Api" or "Ilmi.Worker");
    }

    // A module project may reference the allowed projects plus any OTHER module's Contracts project.
    private static void AssertOnlyAllowed(string project, string module, string[] allowed)
    {
        var offending = ReferencesOf(project)
            .Where(r => !allowed.Contains(r))
            .Where(r => !(r.StartsWith("Ilmi.Modules.") && !r.StartsWith($"Ilmi.Modules.{module}.") && r.EndsWith(".Contracts")));
        Assert.Empty(offending);
    }
}
