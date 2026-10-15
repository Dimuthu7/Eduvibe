using EduVibe.Modules.Plans.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduVibe.Modules.Plans;

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");
        builder.Property(p => p.Name).HasMaxLength(60);
        builder.Property(p => p.Description).HasMaxLength(300);
        builder.Property(p => p.MonthlyPrice).HasPrecision(12, 2);
        builder.HasIndex(p => p.Name).IsUnique();
    }
}

internal sealed class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
{
    public void Configure(EntityTypeBuilder<PlanFeature> builder)
    {
        builder.ToTable("plan_features");
        builder.Property(f => f.Key).HasMaxLength(50);
        builder.HasOne<Plan>().WithMany().HasForeignKey(f => f.PlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(f => new { f.PlanId, f.Key }).IsUnique();
    }
}

internal sealed class TeacherSubscriptionConfiguration : IEntityTypeConfiguration<TeacherSubscription>
{
    public void Configure(EntityTypeBuilder<TeacherSubscription> builder)
    {
        builder.ToTable("teacher_subscriptions");
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Note).HasMaxLength(300);
        builder.HasOne<Plan>().WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.TeacherId, s.StartsOn });
    }
}

internal sealed class PlanPaymentConfiguration : IEntityTypeConfiguration<PlanPayment>
{
    public void Configure(EntityTypeBuilder<PlanPayment> builder)
    {
        builder.ToTable("plan_payments");
        builder.Property(p => p.Amount).HasPrecision(12, 2);
        builder.Property(p => p.Method).HasMaxLength(20);
        builder.Property(p => p.Reference).HasMaxLength(100);
        builder.HasOne<TeacherSubscription>().WithMany().HasForeignKey(p => p.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.TeacherId, p.PaidOn });
    }
}
