using GuruBook.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuruBook.Modules.Notifications;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.Property(n => n.Kind).HasMaxLength(40);
        builder.Property(n => n.ParamsJson).HasMaxLength(4000);
        builder.Property(n => n.Link).HasMaxLength(200);
        builder.Property(n => n.OnceKey).HasMaxLength(100);
        builder.Property(n => n.GroupKey).HasMaxLength(100);

        // The list is newest first for one person; a once-key is unique for a person, so two requests at the same moment cannot both add it.
        builder.HasIndex(n => new { n.RecipientId, n.UpdatedAt });
        builder.HasIndex(n => new { n.RecipientId, n.OnceKey }).IsUnique();
        builder.HasIndex(n => new { n.RecipientId, n.GroupKey });
    }
}

internal sealed class PushDeviceConfiguration : IEntityTypeConfiguration<PushDevice>
{
    public void Configure(EntityTypeBuilder<PushDevice> builder)
    {
        builder.ToTable("push_devices");
        builder.Property(d => d.Endpoint).HasMaxLength(1000);
        builder.Property(d => d.P256dh).HasMaxLength(200);
        builder.Property(d => d.Auth).HasMaxLength(100);
        builder.Property(d => d.Language).HasMaxLength(5);

        builder.HasIndex(d => d.Endpoint).IsUnique();
        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => d.TenantId);
    }
}

internal sealed class AlertMuteConfiguration : IEntityTypeConfiguration<AlertMute>
{
    public void Configure(EntityTypeBuilder<AlertMute> builder)
    {
        builder.ToTable("alert_mutes");
        builder.Property(m => m.Topic).HasMaxLength(20);

        // One row stands for one silence, so setting the same one twice cannot make two.
        builder.HasIndex(m => new { m.UserId, m.StudentId, m.Topic }).IsUnique();
    }
}

internal sealed class ParentAlertSettingConfiguration : IEntityTypeConfiguration<ParentAlertSetting>
{
    public void Configure(EntityTypeBuilder<ParentAlertSetting> builder)
    {
        builder.ToTable("parent_alert_settings");
        builder.Property(s => s.AttendanceAlerts).HasMaxLength(20);
    }
}
