using GuruBook.Modules.Fees.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuruBook.Modules.Fees;

internal sealed class FeeDueConfiguration : IEntityTypeConfiguration<FeeDue>
{
    public void Configure(EntityTypeBuilder<FeeDue> builder)
    {
        builder.ToTable("fee_dues");
        builder.Property(d => d.Month).HasMaxLength(7);
        builder.Property(d => d.AmountDue).HasPrecision(12, 2);
        builder.HasIndex(d => new { d.EnrolmentId, d.Month }).IsUnique();
        builder.HasIndex(d => new { d.ClassId, d.Month });
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.Property(p => p.ReceiptNo).HasMaxLength(20);
        builder.Property(p => p.Amount).HasPrecision(12, 2);
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.VoidReason).HasMaxLength(200);
        builder.Ignore(p => p.IsVoid);
        builder.HasIndex(p => new { p.TenantId, p.ReceiptNo }).IsUnique();
        builder.HasIndex(p => p.EnrolmentId);
    }
}

internal sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("payment_allocations");
        builder.Property(a => a.Amount).HasPrecision(12, 2);
        builder.HasOne<Payment>().WithMany().HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FeeDue>().WithMany().HasForeignKey(a => a.FeeDueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.PaymentId);
        builder.HasIndex(a => a.FeeDueId);
    }
}
