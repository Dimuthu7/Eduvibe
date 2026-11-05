using GuruBook.Modules.Sms.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuruBook.Modules.Sms;

internal sealed class SmsMessageConfiguration : IEntityTypeConfiguration<SmsMessage>
{
    public void Configure(EntityTypeBuilder<SmsMessage> builder)
    {
        builder.ToTable("texts");
        builder.Property(m => m.StudentName).HasMaxLength(200);
        builder.Property(m => m.Kind).HasMaxLength(30);
        builder.Property(m => m.ToPhone).HasMaxLength(20);
        builder.Property(m => m.Text).HasMaxLength(1000);
        builder.Property(m => m.Language).HasMaxLength(5);
        builder.Property(m => m.DedupKey).HasMaxLength(100);
        builder.Property(m => m.Month).HasMaxLength(7);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(12);
        builder.Property(m => m.ProviderMessageId).HasMaxLength(200);
        builder.Property(m => m.Error).HasMaxLength(40);

        // Tables are named for texts rather than messages because every module's tables share one database in the tests.
        // The allowance counts a teacher's month; a delivery report finds its text by the provider's id; erasing a student finds theirs.
        builder.HasIndex(m => new { m.TenantId, m.Month });
        builder.HasIndex(m => new { m.TenantId, m.DedupKey });
        builder.HasIndex(m => m.ProviderMessageId);
        builder.HasIndex(m => m.StudentId);
        builder.HasIndex(m => m.Status);
    }
}

internal sealed class SmsOptOutConfiguration : IEntityTypeConfiguration<SmsOptOut>
{
    public void Configure(EntityTypeBuilder<SmsOptOut> builder)
    {
        builder.ToTable("text_opt_outs");
        builder.Property(o => o.Phone).HasMaxLength(20);
        builder.HasIndex(o => o.Phone).IsUnique();
    }
}

internal sealed class SmsSettingConfiguration : IEntityTypeConfiguration<SmsSetting>
{
    public void Configure(EntityTypeBuilder<SmsSetting> builder)
    {
        builder.ToTable("channel_settings");
        builder.Property(s => s.Channel).HasMaxLength(30);
    }
}
