using DocGrouping.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocGrouping.Infrastructure.Persistence.Configurations;

public class BusinessRuleConfiguration : IEntityTypeConfiguration<BusinessRule>
{
	public void Configure(EntityTypeBuilder<BusinessRule> builder)
	{
		builder.ToTable("business_rules");

		builder.HasKey(r => r.Id);
		builder.Property(r => r.Id).HasColumnName("id");
		builder.Property(r => r.RuleId).HasColumnName("rule_id").HasMaxLength(100);
		builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(200);
		builder.Property(r => r.Description).HasColumnName("description");
		builder.Property(r => r.RuleType).HasColumnName("rule_type").HasMaxLength(50)
			.HasConversion<string>();
		builder.Property(r => r.Action).HasColumnName("action").HasMaxLength(50)
			.HasConversion<string>();
		builder.Property(r => r.Priority).HasColumnName("priority");
		builder.Property(r => r.Enabled).HasColumnName("enabled");
		builder.Property(r => r.Conditions).HasColumnName("conditions").HasColumnType("jsonb");
		builder.Property(r => r.CreatedAt).HasColumnName("created_at");
		builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

		builder.HasIndex(r => r.RuleId).IsUnique();
	}
}
