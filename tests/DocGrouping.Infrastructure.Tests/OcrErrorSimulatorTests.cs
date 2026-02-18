using DocGrouping.Infrastructure.TextProcessing;
using FluentAssertions;

namespace DocGrouping.Infrastructure.Tests;

public class OcrErrorSimulatorTests
{
	private readonly OcrErrorSimulator _simulator = new();

	[Fact]
	public void ApplyErrors_NoneLevel_ReturnsOriginalText()
	{
		var text = "This is a test document with some content.";
		var result = _simulator.ApplyErrors(text, "none");
		result.Should().Be(text);
	}

	[Fact]
	public void ApplyErrors_InvalidLevel_ReturnsOriginalText()
	{
		var text = "This is a test document.";
		var result = _simulator.ApplyErrors(text, "invalid_level");
		result.Should().Be(text);
	}

	[Fact]
	public void ApplyErrors_LightLevel_MostWordsUnchanged()
	{
		var text = string.Join(" ", Enumerable.Range(0, 500).Select(i => $"word{i}"));
		var result = _simulator.ApplyErrors(text, "light");

		var originalWords = text.Split(' ');
		var resultWords = result.Split(' ');

		resultWords.Should().HaveCount(originalWords.Length);

		// Most words should be unchanged at light level (1.5% rate)
		var changedCount = originalWords.Zip(resultWords).Count(pair => pair.First != pair.Second);
		changedCount.Should().BeLessThan(originalWords.Length / 5); // Certainly less than 20%
	}

	[Fact]
	public void ApplyErrors_HeavyLevel_MoreErrorsThanLight()
	{
		// Use a fixed-seed approach: run many times and check average
		var text = string.Join(" ", Enumerable.Range(0, 200).Select(i => $"document{i}"));

		int lightChanges = 0, heavyChanges = 0;
		for (int run = 0; run < 10; run++)
		{
			var light = _simulator.ApplyErrors(text, "light");
			var heavy = _simulator.ApplyErrors(text, "heavy");

			lightChanges += text.Split(' ').Zip(light.Split(' ')).Count(p => p.First != p.Second);
			heavyChanges += text.Split(' ').Zip(heavy.Split(' ')).Count(p => p.First != p.Second);
		}

		// On average, heavy should produce more errors than light
		heavyChanges.Should().BeGreaterThanOrEqualTo(lightChanges);
	}

	[Theory]
	[InlineData("light")]
	[InlineData("moderate")]
	[InlineData("heavy")]
	public void ApplyErrors_AllLevels_PreservesWhitespace(string level)
	{
		var text = "Hello world this is a test document for OCR errors";
		var result = _simulator.ApplyErrors(text, level);

		// Should have the same number of whitespace-separated tokens
		result.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
			.Should().Be(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
	}

	[Fact]
	public void ApplyErrors_ShortWords_SkippedByDesign()
	{
		// Words < 3 chars are skipped. Use all short words.
		var text = "a b c d e f g h i j k l m n o p q";
		var result = _simulator.ApplyErrors(text, "heavy");
		// Short words should remain unchanged since they're < 3 chars
		result.Should().Be(text);
	}
}
