using Microsoft.EntityFrameworkCore;

namespace Tienda.Api.Data;

public class TiendaDbContext(DbContextOptions<TiendaDbContext> options) : DbContext(options)
{
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Producto>(e =>
        {
            e.Property(p => p.Precio).HasPrecision(12, 2);
            e.ToTable(t => t.HasCheckConstraint("CK_Producto_PrecioMultiplo20", "\"Precio\" > 0 AND \"Precio\" % 20 = 0"));
        });
        b.Entity<Pedido>(e =>
        {
            e.Property(p => p.Total).HasPrecision(12, 2);
            e.Property(p => p.Estado).HasConversion<string>();
            e.HasIndex(p => p.CompradorEmail);
        });
        b.Entity<PedidoItem>().Property(i => i.PrecioUnitario).HasPrecision(12, 2);
    }

    public void Sembrar()
    {
        if (Productos.Any()) return;
        Productos.AddRange(
            new Producto { Nombre = "Audífonos inalámbricos", Descripcion = "Bluetooth 5.3, 30 h de batería", Precio = 800, Existencia = 15 },
            new Producto { Nombre = "Teclado mecánico", Descripcion = "Switches rojos, retroiluminado", Precio = 1200, Existencia = 10 },
            new Producto { Nombre = "Mouse gamer", Descripcion = "16,000 DPI, 6 botones", Precio = 460, Existencia = 20 },
            new Producto { Nombre = "Memoria USB 64 GB", Descripcion = "USB 3.2", Precio = 160, Existencia = 40 },
            new Producto { Nombre = "Cable USB-C", Descripcion = "1 m, carga rápida", Precio = 80, Existencia = 50 },
            new Producto { Nombre = "Taza de café", Descripcion = "Cerámica, 350 ml", Precio = 120, Existencia = 30 },
            new Producto { Nombre = "Libreta", Descripcion = "Pasta dura, 100 hojas", Precio = 60, Existencia = 60 },
            new Producto { Nombre = "Mochila para laptop", Descripcion = "Hasta 15.6 pulgadas, impermeable", Precio = 940, Existencia = 12 },
            new Producto { Nombre = "Bocina portátil", Descripcion = "Resistente al agua IPX7", Precio = 1500, Existencia = 8 },
            new Producto { Nombre = "Sticker pack", Descripcion = "10 stickers de redes", Precio = 20, Existencia = 100 });
        SaveChanges();
    }
}
