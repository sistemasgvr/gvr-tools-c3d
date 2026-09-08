using System.Text.Json;
using GvrLicense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GvrLicense.Infrastructure;

public sealed class LicenseDbContext(DbContextOptions<LicenseDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<CompanyUser> CompanyUsers => Set<CompanyUser>();
    public DbSet<QuoteRequest> QuoteRequests => Set<QuoteRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Nombres de tabla/columna en snake_case a mano (sin EFCore.NamingConventions: todavía no
        // publica una versión compatible con EF Core 10 -- ver server/src/GvrLicense.Infrastructure/GvrLicense.Infrastructure.csproj).
        // Tienen que coincidir literal con Sql/ConsumeQuota.sql y Sql/AuditLogTrigger.sql.

        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("customer");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CompanyName).HasColumnName("company_name");
            e.Property(x => x.ContactName).HasColumnName("contact_name");
            e.Property(x => x.ContactEmail).HasColumnName("contact_email");
            e.Property(x => x.PaymentNotes).HasColumnName("payment_notes");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<Plan>(e =>
        {
            e.ToTable("plan");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.ServiceSuspended).HasColumnName("service_suspended").HasDefaultValue(false);

            ConfigureDictionaryJsonb(e.Property(x => x.Features).HasColumnName("features"));

            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<License>(e =>
        {
            e.ToTable("license");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Key).HasColumnName("key");
            e.Property(x => x.CustomerId).HasColumnName("customer_id");
            e.Property(x => x.PlanId).HasColumnName("plan_id");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.ValidUntil).HasColumnName("valid_until");
            e.Property(x => x.MaxUsers).HasColumnName("max_users");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            ConfigureDictionaryJsonb(e.Property(x => x.FeatureOverrides).HasColumnName("feature_overrides"));
            e.HasIndex(x => x.Key).IsUnique();
            e.HasOne(x => x.Customer).WithMany(c => c.Licenses).HasForeignKey(x => x.CustomerId);
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId);
        });

        modelBuilder.Entity<CompanyUser>(e =>
        {
            e.ToTable("company_user");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CustomerId).HasColumnName("customer_id");
            e.Property(x => x.FullName).HasColumnName("full_name");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.HasIndex(x => new { x.CustomerId, x.Email }).IsUnique();
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId);
        });

        modelBuilder.Entity<Device>(e =>
        {
            e.ToTable("device");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.LicenseId).HasColumnName("license_id");
            e.Property(x => x.CompanyUserId).HasColumnName("company_user_id");
            e.Property(x => x.Fingerprint).HasColumnName("fingerprint");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.ActivatedAtUtc).HasColumnName("activated_at_utc");
            e.Property(x => x.LastSeenUtc).HasColumnName("last_seen_utc");
            e.Property(x => x.LastIp).HasColumnName("last_ip");
            e.Property(x => x.SeenCount).HasColumnName("seen_count").HasDefaultValue(0);
            e.HasIndex(x => new { x.LicenseId, x.Fingerprint }).IsUnique();
            e.HasOne(x => x.License).WithMany(l => l.Devices).HasForeignKey(x => x.LicenseId);
            e.HasOne(x => x.CompanyUser).WithMany(u => u.Devices).HasForeignKey(x => x.CompanyUserId);
        });

        modelBuilder.Entity<UsageCounter>(e =>
        {
            e.ToTable("usage_counter");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.LicenseId).HasColumnName("license_id");
            e.Property(x => x.FeatureCode).HasColumnName("feature_code");
            e.Property(x => x.Period).HasColumnName("period");
            e.Property(x => x.QuotaLimit).HasColumnName("quota_limit");
            e.Property(x => x.Consumed).HasColumnName("consumed");
            e.HasIndex(x => new { x.LicenseId, x.FeatureCode, x.Period }).IsUnique();
            e.HasOne(x => x.License).WithMany(l => l.UsageCounters).HasForeignKey(x => x.LicenseId);
        });

        modelBuilder.Entity<UsageEvent>(e =>
        {
            e.ToTable("usage_event");
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.LicenseId).HasColumnName("license_id");
            e.Property(x => x.DeviceId).HasColumnName("device_id");
            e.Property(x => x.FeatureCode).HasColumnName("feature_code");
            e.Property(x => x.Quantity).HasColumnName("quantity");
            e.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc");
            e.Property(x => x.ReceivedAtUtc).HasColumnName("received_at_utc");
        });

        modelBuilder.Entity<Release>(e =>
        {
            e.ToTable("release");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.Channel).HasColumnName("channel");
            e.Property(x => x.Checksum).HasColumnName("checksum");
            e.Property(x => x.ArtifactLocation).HasColumnName("artifact_location");
            e.Property(x => x.Kind).HasColumnName("kind").HasDefaultValue(ReleaseKinds.Installer);
            e.Property(x => x.FileName).HasColumnName("file_name");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.SignatureBase64).HasColumnName("signature_base64");
            e.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_log");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.LicenseId).HasColumnName("license_id");
            e.Property(x => x.Actor).HasColumnName("actor");
            e.Property(x => x.Action).HasColumnName("action");
            e.Property(x => x.DetailsJson).HasColumnName("details_json").HasColumnType("jsonb");
            e.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc");
        });

        modelBuilder.Entity<AppSettings>(e =>
        {
            e.ToTable("app_settings");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SupportEmail).HasColumnName("support_email");
            e.Property(x => x.TermsOfServiceUrl).HasColumnName("terms_of_service_url");
            e.Property(x => x.PrivacyPolicyUrl).HasColumnName("privacy_policy_url");
        });

        modelBuilder.Entity<QuoteRequest>(e =>
        {
            e.ToTable("quote_request");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FullName).HasColumnName("full_name");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.Phone).HasColumnName("phone");
            e.Property(x => x.CompanyName).HasColumnName("company_name");
            e.Property(x => x.PlanCode).HasColumnName("plan_code");
            e.Property(x => x.Message).HasColumnName("message");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.SourceIp).HasColumnName("source_ip");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<AdminUser>(e =>
        {
            e.ToTable("admin_user");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Username).HasColumnName("username");
            e.Property(x => x.PasswordHash).HasColumnName("password_hash");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.HasIndex(x => x.Username).IsUnique();
        });

        // consume_quota() y el trigger de auditoría se crean vía migración con migrationBuilder.Sql(...)
        // apuntando a Sql/ConsumeQuota.sql y Sql/AuditLogTrigger.sql -- no tienen equivalente Fluent API
        // porque no son mapeo de entidades.
    }

    /// <summary>
    /// Npgsql 10 no serializa Dictionary&lt;&gt; a jsonb por defecto: exige EnableDynamicJson()
    /// global en el NpgsqlDataSource, un opt-in amplio que preferimos evitar. Conversión explícita
    /// vía System.Text.Json en su lugar -- más quirúrgico, sin estado global. Compartido entre
    /// Plan.Features y License.FeatureOverrides.
    /// </summary>
    private static void ConfigureDictionaryJsonb(PropertyBuilder<Dictionary<string, string>> property)
    {
        property
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>())
            .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, string>>(
                (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
                d => d.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
                d => new Dictionary<string, string>(d)));
    }
}
