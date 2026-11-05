using GuruBook.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuruBook.Modules.Payments;

internal sealed class BankDetailsConfiguration : IEntityTypeConfiguration<BankDetails>
{
    public void Configure(EntityTypeBuilder<BankDetails> builder)
    {
        builder.ToTable("bank_details");
        builder.Property(d => d.BankName).HasMaxLength(100);
        builder.Property(d => d.AccountName).HasMaxLength(100);
        builder.Property(d => d.AccountNumber).HasMaxLength(40);
        builder.Property(d => d.Branch).HasMaxLength(100);
        builder.Property(d => d.Note).HasMaxLength(300);
    }
}

internal sealed class PayQrConfiguration : IEntityTypeConfiguration<PayQr>
{
    public void Configure(EntityTypeBuilder<PayQr> builder)
    {
        builder.ToTable("qr_codes");
        builder.Property(q => q.ContentType).HasMaxLength(40);
    }
}

internal sealed class PaymentClaimConfiguration : IEntityTypeConfiguration<PaymentClaim>
{
    public void Configure(EntityTypeBuilder<PaymentClaim> builder)
    {
        builder.ToTable("payment_claims");
        builder.Property(c => c.Month).HasMaxLength(7);
        builder.Property(c => c.Reference).HasMaxLength(20);
        builder.Property(c => c.Note).HasMaxLength(200);
        builder.Property(c => c.SlipContentType).HasMaxLength(40);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(12);
        builder.Property(c => c.RejectReason).HasMaxLength(200);
        builder.Property(c => c.ReceiptNo).HasMaxLength(20);

        // The teacher's list is open claims first; a payer looks up the claim on one fee; the slip cleanup looks for decided ones.
        builder.HasIndex(c => new { c.TenantId, c.Status });
        builder.HasIndex(c => c.FeeDueId);
        builder.HasIndex(c => c.StudentId);
        builder.HasIndex(c => c.DecidedAt);
    }
}
