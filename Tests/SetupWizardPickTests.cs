using ECAssistant.Core.Setup;
using Xunit;

namespace ECAssistantConsole.Tests;

public class SetupWizardPickTests
{
    private static ModelCatalogEntry Entry(string id, bool recommended, string category = "Chat") =>
        new() { Id = id, DisplayName = id, Recommended = recommended, Category = Enum.Parse<CatalogModelCategory>(category) };

    [Fact]
    public void ParsePicks_Numbers_ReturnSelected()
    {
        var list = new[] { Entry("a", false), Entry("b", false), Entry("c", false) };
        var result = SetupWizard.ParsePicks("1,3", list);
        Assert.Equal(new[] { "a", "c" }, result.Select(m => m.Id));
    }

    [Fact]
    public void ParsePicks_LetterA_SelectsRecommendedOnly()
    {
        var list = new[] { Entry("a", true), Entry("b", false), Entry("c", true) };
        var result = SetupWizard.ParsePicks("a", list);
        Assert.Equal(new[] { "a", "c" }, result.Select(m => m.Id));
    }

    [Fact]
    public void ParsePicks_LiteralAll_IsNotAValidPicker_SelectsNothing()
    {
        // Only the single letter "a" (case-insensitive) means "recommended"; the
        // literal word "all" is not a recognized picker token and selects nothing.
        var list = new[] { Entry("a", true), Entry("b", false), Entry("c", true) };
        Assert.Empty(SetupWizard.ParsePicks("all", list));
    }

    [Fact]
    public void ParsePicks_Empty_ReturnsNone()
    {
        var list = new[] { Entry("a", true) };
        Assert.Empty(SetupWizard.ParsePicks("", list));
    }

    [Fact]
    public void ParsePicks_OutOfRange_AndDuplicates_AreIgnored()
    {
        var list = new[] { Entry("a", false), Entry("b", false) };
        var result = SetupWizard.ParsePicks("5,1,1,0,-2", list);
        Assert.Equal(new[] { "a" }, result.Select(m => m.Id));
    }
}
