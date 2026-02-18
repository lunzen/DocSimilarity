using DocGrouping.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocGrouping.Infrastructure.Persistence.Configurations;

public class DocumentGroupMembershipConfiguration : IEntityTypeConfiguration<DocumentGroupMembership>
{
	public void Configure(EntityTypeBuilder<DocumentGroupMembership> builder)
	{
		builder.ToTable("document_group_memberships");

		builder.HasKey(m => m.Id);
		builder.Property(m => m.Id).HasColumnName("id");
		builder.Property(m => m.DocumentId).HasColumnName("document_id");
		builder.Property(m => m.GroupId).HasColumnName("group_id");
		builder.Property(m => m.IsCanonical).HasColumnName("is_canonical");
		builder.Property(m => m.SimilarityScore).HasColumnName("similarity_score").HasPrecision(5, 4);

		builder.HasIndex(m => m.DocumentId).IsUnique();

		builder.HasOne(m => m.Document)
			.WithOne(d => d.GroupMembership)
			.HasForeignKey<DocumentGroupMembership>(m => m.DocumentId)
			.OnDelete(DeleteBehavior.Cascade);

		builder.HasOne(m => m.Group)
			.WithMany(g => g.Memberships)
			.HasForeignKey(m => m.GroupId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}
