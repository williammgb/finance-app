namespace FinanceApp.Tests;

public class SmokeTests
{
    [Fact]
    public void AppAssemblyLoadsUnderItsOwnName()
    {
        var assembly = typeof(FinanceApp.Components.App).Assembly;

        Assert.Equal("FinanceApp", assembly.GetName().Name);
    }
}
