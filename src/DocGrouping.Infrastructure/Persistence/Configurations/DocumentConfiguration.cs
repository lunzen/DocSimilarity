using DocGrouping.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocGrouping.Infrastructure.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
	public void Configure(EntityTypeBuilder<Document> builder)
	{
		builder.ToTable("documents");

		builder.HasKey(d => d.Id);
		builder.Property(d => d.Id).HasColumnName("id");
		builder.Property(d => d.FileName).HasColumnName("file_name").HasMaxLength(500);
		builder.Property(d => d.FilePath).HasColumnName("file_path").HasMaxLength(1000);
		builder.Property(d => d.FileSizeBytes).HasColumnName("file_size_bytes");
		builder.Property(d => d.FileHash).HasColumnName("file_hash").HasMaxLength(64);
		builder.Property(d => d.OriginalText).HasColumnName("original_text");
		builder.Property(d => d.NormalizedText).HasColumnName("normalized_text");
		builder.Property(d => d.TextHash).HasColumnName("text_hash").HasMaxLength(64);
		builder.Property(d => d.FuzzyHash).HasColumnName("fuzzy_hash").HasMaxLength(64);
		builder.Property(d => d.WordCount).HasColumnName("word_count");
		builder.Property(d => d.DocumentType).HasColumnName("document_type").HasMaxLength(50);
		builder.Property(d => d.DocumentDate).HasColumnName("document_date");
		builder.Property(d => d.Parties).HasColumnName("parties").HasColumnType("jsonb");
		builder.Property(d => d.BatesRange).HasColumnName("bates_range").HasMaxLength(100);
		builder.Property(d => d.SourceFolder).HasColumnName("source_folder").HasMaxLength(500);
		builder.Property(d => d.Tags).HasColumnName("tags").HasColumnType("jsonb");
		builder.Property(d => d.CustomMetadata).HasColumnName("custom_metadata").HasColumnType("jsonb");
		builder.Property(d => d.IsCanonicalReference).HasColumnName("is_canonical_reference").HasDefaultValue(false);
		builder.Property(d => d.CreatedAt).HasColumnName("created_at");

		builder.HasIndex(d => d.TextHash).HasDatabaseName("ix_documents_text_hash");
		builder.HasIndex(d => d.FuzzyHash).HasDatabaseName("ix_documents_fuzzy_hash");
		builder.HasIndex(d => d.FileHash).HasDatabaseName("ix_documents_file_hash");
		builder.HasIndex(d => d.DocumentType).HasDatabaseName("ix_documents_document_type");
		builder.HasIndex(d => d.CreatedAt).HasDatabaseName("ix_documents_created_at");

		builder.HasIndex(d => new { d.IsCanonicalReference, d.DocumentType })
			.HasDatabaseName("ix_documents_canonical_type")
			.HasFilter("is_canonical_reference = true");
	}
}
