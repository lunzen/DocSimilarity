using DocGrouping.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocGrouping.Infrastructure.Persistence.Configurations;

public class DocumentGroupConfiguration : IEntityTypeConfiguration<DocumentGroup>
{
	public void Configure(EntityTypeBuilder<DocumentGroup> builder)
	{
		builder.ToTable("document_groups");

		builder.HasKey(g => g.Id);
		builder.Property(g => g.Id).HasColumnName("id");
		builder.Property(g => g.GroupNumber).HasColumnName("group_number").ValueGeneratedOnAdd();
		builder.Property(g => g.Confidence).HasColumnName("confidence").HasMaxLength(20)
			.HasConversion<string>();
		builder.Property(g => g.MatchReason).HasColumnName("match_reason");
		builder.Property(g => g.CanonicalDocumentId).HasColumnName("canonical_document_id");
		builder.Property(g => g.DocumentCount).HasColumnName("document_count");
		builder.Property(g => g.CreatedAt).HasColumnName("created_at");
		builder.Property(g => g.UpdatedAt).HasColumnName("updated_at");

		builder.HasIndex(g => g.GroupNumber).IsUnique();

		builder.HasOne(g => g.CanonicalDocument)
			.WithMany()
			.HasForeignKey(g => g.CanonicalDocumentId)
			.OnDelete(DeleteBehavior.SetNull);
	}
}
