using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeSnapshot.Domain.Entities;

namespace TradeSnapshot.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("Accounts");
        b.Property(x => x.AccountCode).HasMaxLength(32).IsUnicode(false).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.BaseCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength().IsRequired();
        b.HasIndex(x => x.AccountCode).IsUnique().HasDatabaseName("UX_Accounts_AccountCode");
        b.HasData(SeedData.Accounts);
    }
}

internal sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> b)
    {
        b.ToTable("Instruments");
        b.Property(x => x.Isin).HasMaxLength(12).IsUnicode(false).IsFixedLength().IsRequired();
        b.Property(x => x.Symbol).HasMaxLength(16).IsUnicode(false).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength().IsRequired();
        b.HasIndex(x => x.Isin).IsUnique().HasDatabaseName("UX_Instruments_Isin");
        b.HasIndex(x => x.Symbol).HasDatabaseName("IX_Instruments_Symbol");
        b.HasData(SeedData.Instruments);
    }
}

internal sealed class InstrumentPriceConfiguration : IEntityTypeConfiguration<InstrumentPrice>
{
    public void Configure(EntityTypeBuilder<InstrumentPrice> b)
    {
        b.ToTable("InstrumentPrices", t => t.HasCheckConstraint("CK_InstrumentPrices_ClosePrice", "[ClosePrice] >= 0"));
        // Clustered PK (InstrumentId, PriceDate) is exactly the access path for the
        // "latest price on or before the valuation date" seek in the snapshot procedure.
        b.HasKey(x => new { x.InstrumentId, x.PriceDate }).HasName("PK_InstrumentPrices");
        b.HasOne(x => x.Instrument).WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasData(SeedData.Prices);
    }
}

internal sealed class FxRateConfiguration : IEntityTypeConfiguration<FxRate>
{
    public void Configure(EntityTypeBuilder<FxRate> b)
    {
        b.ToTable("FxRates", t => t.HasCheckConstraint("CK_FxRates_Rate", "[Rate] > 0"));
        b.HasKey(x => new { x.BaseCurrency, x.QuoteCurrency, x.RateDate }).HasName("PK_FxRates");
        b.Property(x => x.BaseCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
        b.Property(x => x.QuoteCurrency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
        b.Property(x => x.Rate).HasPrecision(19, 8);
        b.HasData(SeedData.FxRates);
    }
}
