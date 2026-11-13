using GuruBook.Modules.Support.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuruBook.Modules.Support;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.Property(t => t.Kind).HasMaxLength(20);
        builder.Property(t => t.Details).HasMaxLength(2000);
        builder.Property(t => t.Screen).HasMaxLength(100);
        builder.Property(t => t.ShotContentType).HasMaxLength(40);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(t => new { t.TenantId, t.Status });
    }
}

internal sealed class TicketReplyConfiguration : IEntityTypeConfiguration<TicketReply>
{
    public void Configure(EntityTypeBuilder<TicketReply> builder)
    {
        builder.ToTable("ticket_replies");
        builder.Property(r => r.Body).HasMaxLength(2000);
        builder.HasIndex(r => r.TicketId);
    }
}

internal sealed class SupportSettingsConfiguration : IEntityTypeConfiguration<SupportSettings>
{
    public void Configure(EntityTypeBuilder<SupportSettings> builder)
    {
        builder.ToTable("settings");
        builder.Property(s => s.Phone).HasMaxLength(20);
        builder.Property(s => s.Hours).HasMaxLength(100);
    }
}
