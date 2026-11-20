using Microsoft.EntityFrameworkCore;

namespace Agent.Api.Data;

public sealed class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public DbSet<AgentProfileRecord> AgentProfiles => Set<AgentProfileRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var profile = modelBuilder.Entity<AgentProfileRecord>();
        profile.ToTable("agent_profiles");
        profile.HasKey(x => x.Id);
        profile.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        profile.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        profile.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        profile.Property(x => x.Description).HasColumnName("description").HasMaxLength(1600).IsRequired();
        profile.Property(x => x.AvatarReference).HasColumnName("avatar_reference").HasMaxLength(500);
        profile.Property(x => x.Engine).HasColumnName("engine").HasMaxLength(32).IsRequired();
        profile.Property(x => x.CanReadRepositories).HasColumnName("can_read_repositories");
        profile.Property(x => x.CanWriteRepositories).HasColumnName("can_write_repositories");
        profile.Property(x => x.CanBrowseWeb).HasColumnName("can_browse_web");
        profile.Property(x => x.UseSemanticNavigation).HasColumnName("use_semantic_navigation");
        profile.Property(x => x.PreferUpdatedDocumentation).HasColumnName("prefer_updated_documentation");
        profile.Property(x => x.ApplyArchitectureRules).HasColumnName("apply_architecture_rules");
        profile.Property(x => x.AiStoConfiguration).HasColumnName("aisto_configuration").HasColumnType("jsonb");
        profile.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        profile.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        profile.HasIndex(x => new { x.UserId, x.UpdatedAtUtc });
    }
}

public sealed class AgentProfileRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? AvatarReference { get; set; }
    public string Engine { get; set; } = "codex";
    public bool CanReadRepositories { get; set; } = true;
    public bool CanWriteRepositories { get; set; }
    public bool CanBrowseWeb { get; set; } = true;
    public bool UseSemanticNavigation { get; set; } = true;
    public bool PreferUpdatedDocumentation { get; set; } = true;
    public bool ApplyArchitectureRules { get; set; } = true;
    public string? AiStoConfiguration { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
