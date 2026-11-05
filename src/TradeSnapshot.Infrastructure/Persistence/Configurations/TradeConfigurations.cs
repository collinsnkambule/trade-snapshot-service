using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeSnapshot.Domain.Entities;

namespace TradeSnapshot.Infrastructure.Persistence.Configurations;

internal sealed class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> b)
    {
        b.ToTable("Trades");
        b.Property(x => x.ExternalRef).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();

        // Idempotency anchor: two concurrent first-time submissions of the same external_ref cannot both insert.
        b.HasIndex(x => x.ExternalRef).IsUnique().HasDatabaseName("UX_Trades_ExternalRef");

        // Snapshot access path: all trades for one account (InstrumentId carried so no key lookup is needed).
        b.HasIndex(x => x.AccountId).IncludeProperties(x => x.InstrumentId).HasDatabaseName("IX_Trades_AccountId");

        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Instrument).WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Versions).WithOne(x => x.Trade).HasForeignKey(x => x.TradeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TradeVersionConfiguration : IEntityTypeConfiguration<TradeVersion>
{
    public void Configure(EntityTypeBuilder<TradeVersion> b)
    {
        b.ToTable("TradeVersions", t =>
        {
            t.HasCheckConstraint("CK_TradeVersions_Side", "[Side] IN (1, 2)");
            t.HasCheckConstraint("CK_TradeVersions_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_TradeVersions_Price", "[Price] >= 0");
        });
        b.Property(x => x.Fingerprint).HasMaxLength(32).IsFixedLength().IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(64).IsUnicode(false);

        b.HasIndex(x => new { x.TradeId, x.Version }).IsUnique().HasDatabaseName("UX_TradeVersions_TradeId_Version");

        // Effective-dating access path for "latest version with AsOfUtc <= cutoff" per trade. Unique because two
        // versions asserted at the same instant would be ambiguous. Covering, so the snapshot never touches the
        // clustered index for version rows.
        b.HasIndex(x => new { x.TradeId, x.AsOfUtc })
            .IsUnique()
            .IsDescending(false, true)
            .IncludeProperties(x => new { x.Side, x.Quantity, x.Price, x.TradeDate })
            .HasDatabaseName("UX_TradeVersions_TradeId_AsOfUtc");
    }
}

internal sealed class TradeIngestionLogConfiguration : IEntityTypeConfiguration<TradeIngestionLogEntry>
{
    public void Configure(EntityTypeBuilder<TradeIngestionLogEntry> b)
    {
        b.ToTable("TradeIngestionLog");
        b.Property(x => x.ExternalRef).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.AccountCode).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        b.Property(x => x.ShadowOutcome).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        b.Property(x => x.Reason).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.Fingerprint).HasMaxLength(32).IsFixedLength();
        b.Property(x => x.CorrelationId).HasMaxLength(64).IsUnicode(false);

        // AccountCode is stored as text, not an FK, so messages for unknown accounts are still recorded.
        // TradeId is a nullable FK (null for rejections).
        b.HasOne(x => x.Trade).WithMany().HasForeignKey(x => x.TradeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AccountCode, x.ReceivedAtUtc }).IsDescending(false, true).HasDatabaseName("IX_TradeIngestionLog_Account_ReceivedAt");
        b.HasIndex(x => x.ExternalRef).HasDatabaseName("IX_TradeIngestionLog_ExternalRef");
    }
}
