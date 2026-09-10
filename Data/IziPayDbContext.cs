using IZIPay.Models;
using Microsoft.EntityFrameworkCore;

namespace IZIPay.Data;

public sealed class IziPayDbContext(DbContextOptions<IziPayDbContext> options) : DbContext(options)
{
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentRecord>().Property(payment => payment.Status).HasMaxLength(32);
        modelBuilder.Entity<PaymentRecord>().Property(payment => payment.Provider).HasMaxLength(32);
    }
}