using AdaptAula.Domain;
using Microsoft.EntityFrameworkCore;

namespace AdaptAula.Infrastructure.Persistence;

public class AdaptAulaDbContext : DbContext
{
    public AdaptAulaDbContext(DbContextOptions<AdaptAulaDbContext> options) : base(options) { }

    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<AdaptationPlan> AdaptationPlans => Set<AdaptationPlan>();
    public DbSet<AdaptedQuestion> AdaptedQuestions => Set<AdaptedQuestion>();
    public DbSet<ValidationResult> ValidationResults => Set<ValidationResult>();
    public DbSet<ExportVersion> ExportVersions => Set<ExportVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var stringListConverter = JsonValueConverters.ForStringList();
        var stringListComparer = JsonValueConverters.StringListComparer();

        modelBuilder.Entity<Assessment>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.LockedFields).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.HasMany(a => a.Sections).WithOne().HasForeignKey(s => s.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Section>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasMany(s => s.Questions).WithOne().HasForeignKey(q => q.SectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Question>(e =>
        {
            e.HasKey(q => q.Id);
            e.Property(q => q.ConstructTags).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(q => q.Options).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(q => q.AssetRefs).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
        });

        modelBuilder.Entity<StudentProfile>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Measures).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(p => p.Accommodations).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(p => p.Exceptions).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
        });

        modelBuilder.Entity<AdaptationPlan>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Locks).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(p => p.Warnings).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);

            var resolvedRulesConverter = JsonValueConverters.ForJson<Dictionary<Guid, List<ResolvedRule>>>();
            var resolvedRulesComparer = JsonValueConverters.JsonComparer<Dictionary<Guid, List<ResolvedRule>>>();
            e.Property(p => p.ResolvedRulesByQuestion).HasConversion(resolvedRulesConverter).Metadata.SetValueComparer(resolvedRulesComparer);
        });

        modelBuilder.Entity<AdaptedQuestion>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Supports).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);

            var changeLogConverter = JsonValueConverters.ForJson<List<ChangeLogEntry>>();
            var changeLogComparer = JsonValueConverters.JsonComparer<List<ChangeLogEntry>>();
            e.Property(a => a.ChangeLog).HasConversion(changeLogConverter).Metadata.SetValueComparer(changeLogComparer);
        });

        modelBuilder.Entity<ValidationResult>(e => e.HasKey(v => v.Id));
        modelBuilder.Entity<ExportVersion>(e => e.HasKey(v => v.Id));
    }
}
