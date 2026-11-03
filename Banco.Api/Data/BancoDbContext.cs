using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Banco.Api.Data;

public class BancoDbContext(DbContextOptions<BancoDbContext> options) : DbContext(options)
{
    public const decimal SaldoInicial = 10_000m;

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Bono> Bonos => Set<Bono>();
    public DbSet<Movimiento> Movimientos => Set<Movimiento>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Saldo).HasPrecision(12, 2);
        });
        b.Entity<Bono>(e =>
        {
            e.HasKey(x => x.Serie);
            e.Property(x => x.Estado).HasConversion<string>();
            e.HasIndex(x => new { x.PropietarioId, x.Estado });
        });
        b.Entity<Movimiento>(e =>
        {
            e.Property(m => m.Tipo).HasConversion<string>();
            e.Property(m => m.Monto).HasPrecision(12, 2);
            e.HasIndex(m => m.UsuarioId);
        });
    }

    public void Sembrar()
    {
        if (Usuarios.Any()) return;
        var hasher = new PasswordHasher<Usuario>();
        foreach (var (nombre, email) in new[] { ("Ana Demo", "ana@demo.com"), ("Luis Demo", "luis@demo.com") })
        {
            var u = new Usuario { Nombre = nombre, Email = email, Saldo = SaldoInicial };
            u.PasswordHash = hasher.HashPassword(u, "demo123");
            Usuarios.Add(u);
            SaveChanges();
            Movimientos.Add(new Movimiento { UsuarioId = u.Id, Tipo = TipoMovimiento.DEPOSITO_INICIAL, Monto = SaldoInicial, Detalle = "Depósito inicial (mock)" });
        }
        SaveChanges();
    }
}
