using FluentAssertions;
using Xunit;

namespace CliSharp.Tests.Unit;

public class SanityTest
{
    [Fact]
    public void Solution_ShouldBuildAndReferencesResolve()
    {
        // Verifies that the solution compiles and project references resolve correctly.
        // This test is replaced by real tests in Milestone 2.
        true.Should().BeTrue();
    }
}
