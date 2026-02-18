using DocGrouping.Infrastructure.TextProcessing;
using FluentAssertions;

namespace DocGrouping.Infrastructure.Tests;

public class RedactionSimulatorTests
{
	private readonly RedactionSimulator _simulator = new();

	[Fact]
	public void ApplyRedactions_NoneLevel_ReturnsOriginalText()
	{
		var text = "Contact John at 555-123-4567 or john@example.com for $1,000,000.";
		var result = _simulator.ApplyRedactions(text, "none");
		result.Should().Be(text);
	}

	[Fact]
	public void ApplyRedactions_LightLevel_AddsRedactionMarkers()
	{
		var text = """
			Contact John Smith at 555-123-4567 or john@example.com.
			SSN: 123-45-6789. Amount: $1,000,000.00.
			Date: January 15, 2023. Account: ABC-12345678.
			Phone: 432-555-1234. Rate: 8.5%. Date: 01/15/2023.
			""";
		var result = _simulator.ApplyRedactions(text, "light");
		result.Should().Contain("[REDACTED");
	}

	[Fact]
	public void ApplyRedactions_HeavyLevel_MoreRedactionsThanLight()
	{
		var text = """
			Contact John Smith at 555-123-4567. SSN: 123-45-6789.
			Amount: $1,000,000.00. Date: January 15, 2023.
			Account: ABC-12345678. Rate: 8.5%. Phone: 432-555-1234.
			Email: john@example.com. Date: 01/15/2023.
			Contact Jane Doe at 555-987-6543. SSN: 987-65-4321.
			Amount: $2,500,000. Date: February 20, 2023.
			Account: XYZ-87654321. Rate: 12.3%. Phone: 713-555-9876.
			""";

		int lightCount = 0, heavyCount = 0;
		for (int i = 0; i < 10; i++)
		{
			var light = _simulator.ApplyRedactions(text, "light");
			var heavy = _simulator.ApplyRedactions(text, "heavy");
			lightCount += CountRedactions(light);
			heavyCount += CountRedactions(heavy);
		}

		heavyCount.Should().BeGreaterThan(lightCount);
	}

	[Fact]
	public void ApplyRedactions_RedactsPhoneNumbers()
	{
		var text = "Call us at 555-123-4567 for more information.";
		// Run enough times to ensure the phone gets picked
		var anyRedacted = false;
		for (int i = 0; i < 50; i++)
		{
			var result = _simulator.ApplyRedactions(text, "heavy");
			if (result.Contains("[REDACTED: PHONE]"))
			{
				anyRedacted = true;
				break;
			}
		}
		anyRedacted.Should().BeTrue("phone number should eventually be redacted");
	}

	[Fact]
	public void ApplyRedactions_RedactsDollarAmounts()
	{
		var text = "The total cost is $1,250,000.00 payable upon closing.";
		var anyRedacted = false;
		for (int i = 0; i < 50; i++)
		{
			var result = _simulator.ApplyRedactions(text, "heavy");
			if (result.Contains("[REDACTED: AMOUNT]"))
			{
				anyRedacted = true;
				break;
			}
		}
		anyRedacted.Should().BeTrue("dollar amount should eventually be redacted");
	}

	private static int CountRedactions(string text)
	{
		int count = 0, idx = 0;
		while ((idx = text.IndexOf("[REDACTED", idx, StringComparison.Ordinal)) >= 0)
		{
			count++;
			idx++;
		}
		return count;
	}
}
