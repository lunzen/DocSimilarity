using DocGrouping.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocGrouping.Infrastructure.Persistence.Configurations;

public class MinHashSignatureConfiguration : IEntityTypeConfiguration<MinHashSignature>
{
	public void Configure(EntityTypeBuilder<MinHashSignature> builder)
	{
		builder.ToTable("minhash_signatures");

		builder.HasKey(s => s.Id);
		builder.Property(s => s.Id).HasColumnName("id");
		builder.Property(s => s.DocumentId).HasColumnName("document_id");
		builder.Property(s => s.Signature).HasColumnName("signature");

		builder.HasIndex(s => s.DocumentId).IsUnique();

		builder.HasOne(s => s.Document)
			.WithOne(d => d.MinHashSignature)
			.HasForeignKey<MinHashSignature>(s => s.DocumentId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}
