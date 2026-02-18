using DocGrouping.Infrastructure.TextProcessing;
using FluentAssertions;

namespace DocGrouping.Infrastructure.Tests;

public class StampGeneratorTests
{
	private readonly StampGenerator _generator = new();

	[Fact]
	public void GenerateBatesStamp_WithPrefix_FormatsCorrectly()
	{
		var result = _generator.GenerateBatesStamp("ABC", 1234);
		result.Should().Be("BATES: ABC-00001234");
	}

	[Fact]
	public void GenerateBatesStamp_WithoutArgs_GeneratesValidStamp()
	{
		var result = _generator.GenerateBatesStamp();
		result.Should().StartWith("BATES: ");
		result.Should().Contain("-");
	}

	[Fact]
	public void GenerateReceivedStamp_ContainsDepartmentAndDate()
	{
		var result = _generator.GenerateReceivedStamp("JAN 15 2023", "LEGAL DEPARTMENT");
		result.Should().Contain("RECEIVED");
		result.Should().Contain("LEGAL DEPARTMENT");
		result.Should().Contain("JAN 15 2023");
	}

	[Fact]
	public void GenerateReceivedStamp_WithoutArgs_GeneratesValidStamp()
	{
		var result = _generator.GenerateReceivedStamp();
		result.Should().Contain("RECEIVED");
		result.Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public void GenerateFaxHeader_ContainsRequiredFields()
	{
		var result = _generator.GenerateFaxHeader("Test Sender", "Test Receiver");
		result.Should().Contain("FAX TRANSMISSION");
		result.Should().Contain("FROM: Test Sender");
		result.Should().Contain("TO: Test Receiver");
		result.Should().Contain("DATE:");
		result.Should().Contain("PAGES:");
	}

	[Theory]
	[InlineData("page_of", "Page 3 of 10")]
	[InlineData("dash", "- 3 -")]
	[InlineData("simple", "Page 3")]
	[InlineData("slash", "3/10")]
	public void GeneratePageNumbers_RespectsStyle(string style, string expected)
	{
		var result = _generator.GeneratePageNumbers(3, 10, style);
		result.Should().Be(expected);
	}

	[Fact]
	public void AddStampToDocument_TopPosition_PrependsStamp()
	{
		var result = _generator.AddStampToDocument("body text", "STAMP", "top");
		result.Should().StartWith("STAMP");
		result.Should().EndWith("body text");
	}

	[Fact]
	public void AddStampToDocument_BottomPosition_AppendsStamp()
	{
		var result = _generator.AddStampToDocument("body text", "STAMP", "bottom");
		result.Should().StartWith("body text");
		result.Should().EndWith("STAMP");
	}

	[Fact]
	public void InsertPageNumbersThroughout_AddsPageNumbers()
	{
		var text = string.Join('\n', Enumerable.Range(1, 120).Select(i => $"Line {i}"));
		var result = _generator.InsertPageNumbersThroughout(text, linesPerPage: 60);
		result.Should().Contain("Page 1 of");
	}
}
