using System.Reflection;

namespace ReScene.App.Core.Tests;

/// <summary>
/// A floor under how many tests this assembly exposes, so a run that discovers fewer than it should
/// fails instead of reporting a smaller green number. Sibling of the same guard in
/// <c>ReScene.Manager.Tests</c>; the two are deliberately independent, because a build or filter
/// problem can affect one assembly and not the other.
/// <para>
/// COUNTING RULE, stated because a count without one cannot be reproduced: this counts METHODS
/// carrying a test attribute — <c>Fact</c> or <c>Theory</c>, matched by attribute type NAME so
/// framework-derived attributes are included — across every non-abstract class in the assembly. A
/// theory counts ONCE regardless of how many data rows it expands to, so this number is
/// deliberately smaller than the run's reported total and moves only when someone adds or removes a
/// test method.
/// </para>
/// <para>
/// WHAT THIS DOES NOT CATCH. It does not notice tests that are discovered and then fail, which is
/// the ordinary case a test run already reports. It guards the quiet failure instead: methods that
/// stop being discovered at all — a file dropped from compilation, a stray <c>--filter</c> left in a
/// CI invocation, an attribute renamed by a framework upgrade — because that produces a smaller
/// GREEN run, and a smaller green number is the one nobody questions.
/// </para>
/// <para>
/// A test class made non-public is deliberately NOT claimed: xUnit's analyzer makes that a build
/// error (xUnit1000), so it can never reach a run. See the sibling guard in
/// <c>ReScene.Manager.Tests</c>, where that was established by trying it.
/// </para>
/// </summary>
public class TestDiscoveryFloorTests
{
    /// <summary>
    /// The number of test METHODS this assembly is known to expose, per the counting rule above.
    /// Raise it deliberately when tests are added; a drop is a defect until proven otherwise.
    /// </summary>
    private const int DiscoveryFloor = 679;

    private static readonly string[] TestAttributeNames = ["FactAttribute", "TheoryAttribute"];

    [Fact]
    public void ThisAssembly_ExposesAtLeastItsKnownTestCount()
    {
        Assembly assembly = typeof(TestDiscoveryFloorTests).Assembly;
        // PUBLIC and non-abstract, which is xUnit's own discovery rule. Counting every type
        // Assembly.GetTypes() returns would include internal classes xUnit never runs, so the floor
        // would stay put while a class quietly stopped being discovered — the exact failure this
        // guard claims to catch.
        int methods = assembly.GetTypes()
            .Where(t => (t.IsPublic || t.IsNestedPublic) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Count(m => m.GetCustomAttributes(inherit: true)
                .Any(a => TestAttributeNames.Contains(a.GetType().Name, StringComparer.Ordinal)));

        Assert.True(methods >= DiscoveryFloor,
            $"only {methods} test methods were found in {assembly.GetName().Name}, below the known floor of " +
            $"{DiscoveryFloor}. Tests have stopped being discovered — check that no class was made non-public " +
            "or abstract, no file left the compilation, and no filter is narrowing the run. If tests were " +
            $"deliberately removed, lower {nameof(DiscoveryFloor)} in the same commit.");
    }
}
