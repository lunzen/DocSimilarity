namespace DocGrouping.Infrastructure.TextProcessing;

public class StampGenerator
{
	private static readonly string[] Departments =
	[
		"LEGAL DEPARTMENT", "LAND DEPARTMENT", "ACCOUNTING DEPARTMENT",
		"OPERATIONS DEPARTMENT", "ENGINEERING DEPARTMENT",
		"REGULATORY AFFAIRS", "CORPORATE OFFICE"
	];

	private static readonly string[] FaxSenders =
	[
		"Smith & Associates", "Johnson Law Group", "Anderson Energy Partners",
		"Williams Petroleum Corp", "Davis Land Services", "Martinez & Thompson LLP"
	];

	private static readonly string[] AreaCodes = ["713", "214", "432", "281", "512", "817"];

	private readonly Random _rng = new();

	public string GenerateBatesStamp(string? prefix = null, int? number = null)
	{
		prefix ??= _rng.NextDouble() < 0.5
			? RandomLetters(2, 4)
			: $"{RandomLetters(2, 3)}-{RandomLetters(2, 2)}";

		number ??= _rng.Next(1000, 9999999);

		return $"BATES: {prefix}-{number:D8}";
	}

	public string GenerateReceivedStamp(string? date = null, string? department = null)
	{
		department ??= Departments[_rng.Next(Departments.Length)];
		date ??= DateTimeOffset.UtcNow
			.AddDays(-_rng.Next(0, 730))
			.ToString("MMM dd yyyy").ToUpperInvariant();

		return $"RECEIVED\n{department}\n{date}";
	}

	public string GenerateFaxHeader(string? fromParty = null, string? toParty = null)
	{
		fromParty ??= FaxSenders[_rng.Next(FaxSenders.Length)];
		toParty ??= "Document Review Department";
		var fromPhone = RandomPhone();
		var toPhone = RandomPhone();
		var pages = _rng.Next(1, 26);
		var dt = DateTimeOffset.UtcNow
			.AddDays(-_rng.Next(0, 365))
			.ToString("MM/dd/yyyy hh:mm tt");

		return $"""
			FAX TRANSMISSION

			FROM: {fromParty}
			FAX: {fromPhone}

			TO: {toParty}
			FAX: {toPhone}

			DATE: {dt}
			PAGES: {pages} (including cover)
			""".Replace("\t", "");
	}

	public string GeneratePageNumbers(int currentPage, int? totalPages = null, string style = "page_of")
	{
		return style switch
		{
			"dash" => $"- {currentPage} -",
			"simple" => $"Page {currentPage}",
			"slash" when totalPages.HasValue => $"{currentPage}/{totalPages}",
			"slash" => $"{currentPage}",
			_ when totalPages.HasValue => $"Page {currentPage} of {totalPages}",
			_ => $"Page {currentPage}",
		};
	}

	public string AddStampToDocument(string text, string stamp, string position = "top")
	{
		return position switch
		{
			"bottom" => $"{text}\n\n{stamp}",
			"both" => $"{stamp}\n\n{text}\n\n{stamp}",
			_ => $"{stamp}\n\n{text}",
		};
	}

	public string InsertPageNumbersThroughout(string text, int linesPerPage = 60, string style = "page_of")
	{
		var lines = text.Split('\n');
		var totalPages = (lines.Length + linesPerPage - 1) / linesPerPage;
		var result = new List<string>();
		var currentPage = 1;

		for (int i = 0; i < lines.Length; i++)
		{
			if (i > 0 && i % linesPerPage == 0)
			{
				result.Add("");
				result.Add(GeneratePageNumbers(currentPage, totalPages, style));
				result.Add("");
				currentPage++;
			}
			result.Add(lines[i]);
		}

		if (lines.Length % linesPerPage != 0)
		{
			result.Add("");
			result.Add(GeneratePageNumbers(currentPage, totalPages, style));
		}

		return string.Join('\n', result);
	}

	private string RandomLetters(int min, int max)
	{
		var len = _rng.Next(min, max + 1);
		return new string(Enumerable.Range(0, len)
			.Select(_ => (char)('A' + _rng.Next(26)))
			.ToArray());
	}

	private string RandomPhone()
	{
		var area = AreaCodes[_rng.Next(AreaCodes.Length)];
		return $"{area}-{_rng.Next(200, 1000)}-{_rng.Next(1000, 10000)}";
	}
}
