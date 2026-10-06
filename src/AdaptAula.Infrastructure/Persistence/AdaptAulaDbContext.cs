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
    public DbSet<ChangeLogRecord> ChangeLogRecords => Set<ChangeLogRecord>();
    public DbSet<CustomMeasure> CustomMeasures => Set<CustomMeasure>();
    public DbSet<ClassPack> ClassPacks => Set<ClassPack>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var stringListConverter = JsonValueConverters.ForStringList();
        var stringListComparer = JsonValueConverters.StringListComparer();

        modelBuilder.Entity<Assessment>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.LockedFields).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(a => a.ImageDataUris).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(a => a.ProtectedVocabulary).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.HasMany(a => a.Sections).WithOne().HasForeignKey(s => s.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Section>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.AssetRefs).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.HasMany(s => s.Questions).WithOne().HasForeignKey(q => q.SectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Question>(e =>
        {
            e.HasKey(q => q.Id);
            e.Property(q => q.ConstructTags).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(q => q.Options).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(q => q.AssetRefs).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(q => q.Analysis).HasConversion(JsonValueConverters.ForJson<QuestionAnalysis>())
                .Metadata.SetValueComparer(JsonValueConverters.JsonComparer<QuestionAnalysis>());
        });

        modelBuilder.Entity<StudentProfile>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Measures).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(p => p.Accommodations).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(p => p.Exceptions).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(p => p.Settings).HasConversion(JsonValueConverters.ForJson<Dictionary<string, string>>())
                .Metadata.SetValueComparer(JsonValueConverters.JsonComparer<Dictionary<string, string>>());
        });

        modelBuilder.Entity<AdaptationPlan>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Locks).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(p => p.Warnings).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(p => p.CurricularCriteriaIds).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(p => p.CurricularContentIds).HasConversion(stringListConverter).Metadata.SetValueComparer(stringListComparer);
            e.Property(p => p.ProfileSettings).HasConversion(JsonValueConverters.ForJson<Dictionary<string, string>>())
                .Metadata.SetValueComparer(JsonValueConverters.JsonComparer<Dictionary<string, string>>());
            e.Property(p => p.Style).HasConversion(JsonValueConverters.ForJson<DocumentStyle>())
                .Metadata.SetValueComparer(JsonValueConverters.JsonComparer<DocumentStyle>());

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
            e.Property(a => a.Proposal).HasConversion(JsonValueConverters.ForJson<QuestionProposal>())
                .Metadata.SetValueComparer(JsonValueConverters.JsonComparer<QuestionProposal>());
        });

        modelBuilder.Entity<ValidationResult>(e => e.HasKey(v => v.Id));
        modelBuilder.Entity<ExportVersion>(e => e.HasKey(v => v.Id));

        modelBuilder.Entity<ChangeLogRecord>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.PlanId);
        });

        modelBuilder.Entity<CustomMeasure>(e => e.HasKey(c => c.Id));

        modelBuilder.Entity<ClassPack>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.PlanIds).HasConversion(JsonValueConverters.ForJson<List<Guid>>())
                .Metadata.SetValueComparer(JsonValueConverters.JsonComparer<List<Guid>>());
        });
    }
}
