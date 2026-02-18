using DocGrouping.Infrastructure.TextProcessing;
using FluentAssertions;

namespace DocGrouping.Infrastructure.Tests;

public class DiffEngineTests
{
	private readonly DiffEngine _engine = new();

	[Fact]
	public void ComputeDiff_IdenticalTexts_AllSame()
	{
		var text = "Line 1\nLine 2\nLine 3";
		var result = _engine.ComputeDiff(text, text);

		result.Should().HaveCount(3);
		result.Should().AllSatisfy(l => l.Type.Should().Be(DiffType.Same));
	}

	[Fact]
	public void ComputeDiff_CompletelyDifferent_AllAddedAndRemoved()
	{
		var text1 = "Alpha\nBravo\nCharlie";
		var text2 = "Delta\nEcho\nFoxtrot";
		var result = _engine.ComputeDiff(text1, text2);

		result.Where(l => l.Type == DiffType.Removed).Should().HaveCount(3);
		result.Where(l => l.Type == DiffType.Added).Should().HaveCount(3);
		result.Where(l => l.Type == DiffType.Same).Should().BeEmpty();
	}

	[Fact]
	public void ComputeDiff_PartialOverlap_MixedResult()
	{
		var text1 = "Line 1\nLine 2\nLine 3\nLine 4";
		var text2 = "Line 1\nModified 2\nLine 3\nLine 5";
		var result = _engine.ComputeDiff(text1, text2);

		var same = result.Where(l => l.Type == DiffType.Same).Select(l => l.Content).ToList();
		same.Should().Contain("Line 1");
		same.Should().Contain("Line 3");

		result.Should().Contain(l => l.Type == DiffType.Removed && l.Content == "Line 2");
		result.Should().Contain(l => l.Type == DiffType.Added && l.Content == "Modified 2");
	}

	[Fact]
	public void ComputeDiff_EmptyFirst_AllAdded()
	{
		var result = _engine.ComputeDiff("", "Line 1\nLine 2");

		// Empty string split on \n gives [""], so first will have 1 entry
		result.Should().Contain(l => l.Type == DiffType.Added);
	}

	[Fact]
	public void ComputeDiff_EmptySecond_AllRemoved()
	{
		var result = _engine.ComputeDiff("Line 1\nLine 2", "");
		result.Should().Contain(l => l.Type == DiffType.Removed);
	}

	[Fact]
	public void ComputeDiff_NullInputs_HandledGracefully()
	{
		var result = _engine.ComputeDiff(null!, null!);
		result.Should().NotBeNull();
	}

	[Fact]
	public void ComputeDiff_LineNumbersAreCorrect()
	{
		var text1 = "A\nB\nC";
		var text2 = "A\nX\nC";
		var result = _engine.ComputeDiff(text1, text2);

		var sameA = result.First(l => l.Content == "A" && l.Type == DiffType.Same);
		sameA.Line1.Should().Be(1);
		sameA.Line2.Should().Be(1);

		var sameC = result.First(l => l.Content == "C" && l.Type == DiffType.Same);
		sameC.Line1.Should().Be(3);
		sameC.Line2.Should().Be(3);
	}

	[Fact]
	public void ComputeDiff_AddedLineAtEnd_Detected()
	{
		var text1 = "Line 1\nLine 2";
		var text2 = "Line 1\nLine 2\nLine 3";
		var result = _engine.ComputeDiff(text1, text2);

		result.Last().Type.Should().Be(DiffType.Added);
		result.Last().Content.Should().Be("Line 3");
	}

	[Fact]
	public void ComputeDiff_RemovedLineAtEnd_Detected()
	{
		var text1 = "Line 1\nLine 2\nLine 3";
		var text2 = "Line 1\nLine 2";
		var result = _engine.ComputeDiff(text1, text2);

		result.Last().Type.Should().Be(DiffType.Removed);
		result.Last().Content.Should().Be("Line 3");
	}
}
