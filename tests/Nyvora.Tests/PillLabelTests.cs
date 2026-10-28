using Nyvora.Core.Tasks;
using Xunit;

namespace Nyvora.Tests;

public class PillLabelTests
{
    [Theory]
    [InlineData(0, 0, "Nyvora")]
    [InlineData(0, 7, "7 open")]
    [InlineData(2, 7, "2 due · 7 open")]
    [InlineData(1, 0, "1 due")]
    public void ShowsWhatMattersMost(int due, int open, string expected) =>
        Assert.Equal(expected, PillLabel.For(due, open));
}
