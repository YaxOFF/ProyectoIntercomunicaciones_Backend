namespace Tienda.Api.Data;

public class Producto
{
    public int Id { get; set; }
    public required string Nombre { get; set; }
    public string Descripcion { get; set; } = "";
    public decimal Precio { get; set; } // múltiplo de 20
    public string? ImagenUrl { get; set; }
    public int Existencia { get; set; }
}

public enum EstadoPedido { PAGADO }

public class Pedido
{
    public int Id { get; set; }
    public required string CompradorEmail { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public decimal Total { get; set; }
    public EstadoPedido Estado { get; set; } = EstadoPedido.PAGADO;
    public List<PedidoItem> Items { get; set; } = [];
    public List<Guid> BonosUsados { get; set; } = [];
}

public class PedidoItem
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int ProductoId { get; set; }
    public Producto? Producto { get; set; }
    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
}
