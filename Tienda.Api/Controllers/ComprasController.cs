using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tienda.Api.Data;
using Tienda.Api.Services;

namespace Tienda.Api.Controllers;

public record ItemDto(int ProductoId, int Cantidad);
public record PagoDto(string? Email, List<ItemDto>? Items, List<BonoFirmado>? Bonos);

[ApiController]
public class ComprasController(TiendaDbContext db, DescifradoHibrido cifrado, BancoCliente banco) : ControllerBase
{
    private const int MaxPorProducto = 100;

    [HttpGet("llave-publica")]
    public ContentResult LlavePublica() => Content(cifrado.LlavePublicaPem(), "application/x-pem-file");

    /// Body: paquete híbrido `{llaveCifrada, nonce, datos}`; dentro va `{email, items, bonos}`.
    [HttpPost("compras")]
    public async Task<IActionResult> Comprar(PaqueteCifrado paquete)
    {
        if (cifrado.Descifrar(paquete) is not { } plano) return BadRequest(new { error = "No se pudo descifrar el paquete" });
        PagoDto? pago;
        try { pago = JsonSerializer.Deserialize<PagoDto>(plano, JsonSerializerOptions.Web); }
        catch (JsonException) { pago = null; }
        if (pago is null) return BadRequest(new { error = "Contenido del paquete inválido" });

        var email = pago.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || !email.Contains('@')) return BadRequest(new { error = "Email inválido" });
        var items = (pago.Items ?? []).GroupBy(i => i.ProductoId).Select(g => (id: g.Key, cantidad: g.Sum(i => i.Cantidad))).ToList();
        if (items.Count == 0) return BadRequest(new { error = "El carrito está vacío" });
        if (items.Any(i => i.cantidad <= 0 || i.cantidad > MaxPorProducto))
            return BadRequest(new { error = $"Cantidades entre 1 y {MaxPorProducto}" });
        var bonos = pago.Bonos ?? [];
        if (bonos.Count == 0) return BadRequest(new { error = "Envía al menos un bono" });
        if (bonos.DistinctBy(b => b.Serie).Count() != bonos.Count) return BadRequest(new { error = "Bonos repetidos" });

        var ids = items.Select(i => i.id).ToList();
        var productos = await db.Productos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        if (ids.FirstOrDefault(id => !productos.ContainsKey(id)) is var faltante and not 0)
            return BadRequest(new { error = $"No existe el producto {faltante}" });
        var total = items.Sum(i => productos[i.id].Precio * i.cantidad);

        try
        {
            var llaveBanco = await banco.LlavePublica();
            if (bonos.FirstOrDefault(b => !BancoCliente.Verificar(llaveBanco, b)) is { } falso)
                return BadRequest(new { error = $"Firma inválida en el bono {falso.Serie}" });
            var suma = bonos.Sum(b => b.Denominacion);
            if (suma < total)
                return BadRequest(new { error = $"Los bonos suman ${suma} y el total es ${total}: no alcanza" });

            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var (id, cantidad) in items)
            {
                var ok = await db.Productos.Where(p => p.Id == id && p.Existencia >= cantidad)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Existencia, p => p.Existencia - cantidad));
                if (ok == 0) return Conflict(new { error = $"No hay existencia suficiente de {productos[id].Nombre}" }); // rollback
            }

            // ponytail: si el banco canjea pero el commit local falla, los bonos se pierden; añadir reintento/compensación si importa.
            var (status, canje) = await banco.Canjear(bonos.Select(b => b.Serie), (int)total);
            if (status == HttpStatusCode.Conflict)
                return Conflict(new { error = "Algún bono ya fue gastado; no se cobró nada" });
            if (status != HttpStatusCode.OK)
                return StatusCode(502, new { error = $"El banco rechazó el canje ({(int)status})" });

            var pedido = new Pedido
            {
                CompradorEmail = email, Total = total, BonosUsados = bonos.Select(b => b.Serie).ToList(),
                Items = items.Select(i => new PedidoItem { ProductoId = i.id, Cantidad = i.cantidad, PrecioUnitario = productos[i.id].Precio }).ToList(),
            };
            db.Pedidos.Add(pedido);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { pedidoId = pedido.Id, total, pagado = suma, cambio = canje!.Cambio ?? [], canje.CambioEnCuenta });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { error = "El banco no está disponible" });
        }
    }

    [HttpGet("pedidos")]
    public async Task<IActionResult> Pedidos(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return BadRequest(new { error = "Falta ?email=" });
        email = email.Trim().ToLowerInvariant();
        return Ok(await db.Pedidos.AsNoTracking().Where(p => p.CompradorEmail == email).OrderByDescending(p => p.Fecha)
            .Select(p => new
            {
                p.Id, p.Fecha, p.Total, p.Estado, p.BonosUsados,
                Items = p.Items.Select(i => new { i.ProductoId, Producto = i.Producto!.Nombre, i.Cantidad, i.PrecioUnitario }),
            }).ToListAsync());
    }
}
