using DocGrouping.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocGrouping.Infrastructure.Persistence.Configurations;

public class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
	public void Configure(EntityTypeBuilder<ProcessingJob> builder)
	{
		builder.ToTable("processing_jobs");

		builder.HasKey(j => j.Id);
		builder.Property(j => j.Id).HasColumnName("id");
		builder.Property(j => j.JobType).HasColumnName("job_type").HasMaxLength(50);
		builder.Property(j => j.Status).HasColumnName("status").HasMaxLength(20)
			.HasConversion<string>();
		builder.Property(j => j.TotalDocuments).HasColumnName("total_documents");
		builder.Property(j => j.ProcessedDocuments).HasColumnName("processed_documents");
		builder.Property(j => j.CurrentPhase).HasColumnName("current_phase").HasMaxLength(50);
		builder.Property(j => j.ErrorMessage).HasColumnName("error_message");
		builder.Property(j => j.CreatedAt).HasColumnName("created_at");
		builder.Property(j => j.StartedAt).HasColumnName("started_at");
		builder.Property(j => j.CompletedAt).HasColumnName("completed_at");
	}
}
