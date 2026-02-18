using DocGrouping.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocGrouping.Infrastructure.Persistence;

public class DocGroupingDbContext : DbContext
{
	public DocGroupingDbContext(DbContextOptions<DocGroupingDbContext> options) : base(options) { }

	public DbSet<Document> Documents => Set<Document>();
	public DbSet<DocumentGroup> DocumentGroups => Set<DocumentGroup>();
	public DbSet<DocumentGroupMembership> DocumentGroupMemberships => Set<DocumentGroupMembership>();
	public DbSet<MinHashSignature> MinHashSignatures => Set<MinHashSignature>();
	public DbSet<LshBucket> LshBuckets => Set<LshBucket>();
	public DbSet<BusinessRule> BusinessRules => Set<BusinessRule>();
	public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(DocGroupingDbContext).Assembly);
	}
}
