using GuruBook.Modules.Dashboard.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuruBook.Modules.Dashboard;

internal sealed class DashboardLayoutConfiguration : IEntityTypeConfiguration<DashboardLayout>
{
    public void Configure(EntityTypeBuilder<DashboardLayout> builder)
    {
        builder.ToTable("layouts");
        builder.Property(l => l.WidgetsJson).HasMaxLength(2000);

        // One layout per teacher, so two saves at the same moment cannot leave two. Named, because the tenant index every module gets is not unique.
        builder.HasIndex(l => l.TenantId, "ux_layouts_tenant_id").IsUnique();
    }
}
