using GuruBook.Modules.Messaging.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuruBook.Modules.Messaging;

internal sealed class MessageSettingConfiguration : IEntityTypeConfiguration<MessageSetting>
{
    public void Configure(EntityTypeBuilder<MessageSetting> builder)
    {
        builder.ToTable("message_settings");
        builder.Property(s => s.Event).HasMaxLength(40);
        builder.Property(s => s.Way).HasMaxLength(20);
        builder.Property(s => s.Note).HasMaxLength(80);
        builder.HasIndex(s => new { s.TenantId, s.Event }).IsUnique();
    }
}

internal sealed class MessageTextConfiguration : IEntityTypeConfiguration<MessageText>
{
    public void Configure(EntityTypeBuilder<MessageText> builder)
    {
        builder.ToTable("message_texts");
        builder.Property(t => t.Event).HasMaxLength(40);
        builder.Property(t => t.Language).HasMaxLength(5);
        builder.Property(t => t.Text).HasMaxLength(600);
        builder.HasIndex(t => new { t.TenantId, t.Event, t.Language }).IsUnique();
    }
}
