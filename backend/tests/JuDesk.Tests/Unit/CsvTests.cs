using JuDesk.Api.Services;

namespace JuDesk.Tests.Unit;

public class CsvTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has, comma", "\"has, comma\"")]
    [InlineData("has \"quote\"", "\"has \"\"quote\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("", "")]
    public void Fields_are_quoted_only_when_needed(string input, string expected)
    {
        Assert.Equal(expected, ScheduleService.Csv(input));
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+39 333", "'+39 333")]
    [InlineData("-x", "'-x")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tx", "'\tx")]
    public void Values_a_spreadsheet_would_run_as_formulas_are_neutralised(string input, string expected)
    {
        Assert.Equal(expected, ScheduleService.Csv(input));
    }
}
