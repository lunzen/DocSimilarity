using DocGrouping.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocGrouping.Infrastructure.Persistence.Configurations;

public class LshBucketConfiguration : IEntityTypeConfiguration<LshBucket>
{
	public void Configure(EntityTypeBuilder<LshBucket> builder)
	{
		builder.ToTable("lsh_buckets");

		builder.HasKey(b => b.Id);
		builder.Property(b => b.Id).HasColumnName("id");
		builder.Property(b => b.BandIndex).HasColumnName("band_index");
		builder.Property(b => b.BucketHash).HasColumnName("bucket_hash").HasMaxLength(64);
		builder.Property(b => b.DocumentId).HasColumnName("document_id");

		builder.HasIndex(b => new { b.BandIndex, b.BucketHash })
			.HasDatabaseName("ix_lsh_buckets_band_bucket");

		builder.HasOne(b => b.Document)
			.WithMany(d => d.LshBuckets)
			.HasForeignKey(b => b.DocumentId)
			.OnDelete(DeleteBehavior.Cascade);
	}
}
