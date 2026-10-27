using JuDesk.Api.Services;

namespace JuDesk.Tests.Unit;

public class SetupCodeTests
{
    [Fact]
    public void Codes_are_readable_grouped_and_random()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => SetupService.NewCode()).ToList();

        Assert.All(codes, c => Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", c));
        Assert.Equal(50, codes.Distinct().Count());
    }

    [Fact]
    public void Banner_contains_the_code_and_the_regen_hint()
    {
        var banner = SetupService.Banner("ABCD-EFGH-JKLM");

        Assert.Contains("ABCD-EFGH-JKLM", banner);
        Assert.Contains("/setup", banner);
        Assert.Contains("regen-setup-code", banner);
    }
}
