using JuDesk.Api.Services;

namespace JuDesk.Tests.Unit;

public class CsvTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has, comma", "\"has, comma\"")]
    [InlineData("has \"quote\"", "\"has \"\"quote\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    public void Fields_are_quoted_only_when_needed(string input, string expected)
    {
        Assert.Equal(expected, ScheduleService.Csv(input));
    }
}
