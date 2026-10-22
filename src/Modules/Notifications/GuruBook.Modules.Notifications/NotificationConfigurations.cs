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
