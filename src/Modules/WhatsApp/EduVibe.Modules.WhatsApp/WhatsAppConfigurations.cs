using EduVibe.Modules.WhatsApp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduVibe.Modules.WhatsApp;

internal sealed class WhatsAppMessageConfiguration : IEntityTypeConfiguration<WhatsAppMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessage> builder)
    {
        builder.ToTable("messages");
        builder.Property(m => m.StudentName).HasMaxLength(200);
        builder.Property(m => m.Kind).HasMaxLength(30);
        builder.Property(m => m.ToPhone).HasMaxLength(20);
        builder.Property(m => m.ParamsJson).HasMaxLength(2000);
        builder.Property(m => m.DedupKey).HasMaxLength(100);
        builder.Property(m => m.Month).HasMaxLength(7);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(12);
        builder.Property(m => m.ProviderMessageId).HasMaxLength(200);
        builder.Property(m => m.Error).HasMaxLength(40);

        // The allowance counts a teacher's month; a delivery report finds its message by Meta's id; erasing a student finds theirs.
        builder.HasIndex(m => new { m.TenantId, m.Month });
        builder.HasIndex(m => new { m.TenantId, m.DedupKey });
        builder.HasIndex(m => m.ProviderMessageId);
        builder.HasIndex(m => m.StudentId);
        builder.HasIndex(m => m.Status);
    }
}

internal sealed class WhatsAppOptOutConfiguration : IEntityTypeConfiguration<WhatsAppOptOut>
{
    public void Configure(EntityTypeBuilder<WhatsAppOptOut> builder)
    {
        builder.ToTable("opt_outs");
        builder.Property(o => o.Phone).HasMaxLength(20);
        builder.HasIndex(o => new { o.TenantId, o.Phone }, "ux_opt_outs_tenant_phone").IsUnique();
    }
}
