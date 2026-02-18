using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DocGrouping.Infrastructure.TextProcessing;

public class PdfTextExtractor
{
	public string ExtractText(string filePath)
	{
		using var document = PdfDocument.Open(filePath);
		var sb = new System.Text.StringBuilder();

		foreach (var page in document.GetPages())
		{
			sb.AppendLine(page.Text);
		}

		return sb.ToString();
	}

	public string ExtractText(byte[] pdfBytes)
	{
		using var document = PdfDocument.Open(pdfBytes);
		var sb = new System.Text.StringBuilder();

		foreach (var page in document.GetPages())
		{
			sb.AppendLine(page.Text);
		}

		return sb.ToString();
	}
}
