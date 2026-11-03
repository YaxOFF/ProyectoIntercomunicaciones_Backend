using System.Security.Cryptography;
using System.Text;
using Banco.Api.Data;
using Banco.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Banco.Api.Controllers;

public record BonoFirmadoDto(Guid Serie, int Denominacion, DateTime EmitidoEn, string Firma);
/// Cobro: monto a cobrar. Si los bonos suman más, el excedente vuelve como cambio. Null = sin cambio.
public record CanjearDto(List<Guid>? Series, int? Cobro);

[ApiController]
public class BonosController(BancoDbContext db, FirmaBonos firma, IConfiguration config) : ControllerBase
{
    public static readonly int[] Denominaciones = [500, 300, 100, 50, 20];
    private const int MaxBonosPorCompra = 100;

    private int UsuarioId => int.Parse(User.FindFirst("sub")!.Value);

    private static BonoFirmadoDto Dto(Bono b) => new(b.Serie, b.Denominacion, b.EmitidoEn, b.Firma);

    /// Crea, firma y agrega (sin guardar) bonos nuevos para el usuario.
    private List<Bono> Emitir(int usuarioId, IEnumerable<int> denominaciones)
    {
        var ahora = DateTime.UtcNow;
        ahora = new DateTime(ahora.Ticks - ahora.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc); // mensaje canónico sin fracciones
        var bonos = denominaciones.Select(d => new Bono { Serie = Guid.NewGuid(), Denominacion = d, PropietarioId = usuarioId, EmitidoEn = ahora }).ToList();
        foreach (var b in bonos) b.Firma = firma.Firmar(b);
        db.Bonos.AddRange(bonos);
        return bonos;
    }

    /// Cambio en la menor cantidad de bonos. Lo que no se puede formar con bonos ($10, $30) va al saldo.
    public static (List<int> bonos, int resto) Desglosar(int monto)
    {
        if (monto <= 0) return ([], 0);
        var previo = new int[monto + 1]; // denominación usada para llegar a s; 0 = inalcanzable
        var cuenta = new int[monto + 1];
        for (var s = 1; s <= monto; s++)
            foreach (var d in Denominaciones)
                if (d <= s && (s == d || previo[s - d] != 0) && (previo[s] == 0 || cuenta[s - d] + 1 < cuenta[s]))
                    (previo[s], cuenta[s]) = (d, cuenta[s - d] + 1);
        var alcanzable = monto;
        while (alcanzable > 0 && previo[alcanzable] == 0) alcanzable--;
        var bonos = new List<int>();
        for (var s = alcanzable; s > 0; s -= previo[s]) bonos.Add(previo[s]);
        return (bonos, monto - alcanzable);
    }

    [HttpGet("llave-publica")]
    public ContentResult LlavePublica() => Content(firma.LlavePublicaPem(), "application/x-pem-file");

    /// Body: `{ "500": 1, "100": 3 }`.
    [Authorize]
    [HttpPost("bonos/comprar")]
    public async Task<IActionResult> Comprar(Dictionary<string, int> pedido)
    {
        var cantidades = new List<(int denominacion, int cantidad)>();
        foreach (var (clave, cantidad) in pedido)
        {
            if (!int.TryParse(clave, out var d) || !Denominaciones.Contains(d))
                return BadRequest(new { error = $"Denominación inválida: {clave}. Válidas: {string.Join(", ", Denominaciones)}" });
            if (cantidad < 0) return BadRequest(new { error = "Las cantidades no pueden ser negativas" });
            if (cantidad > 0) cantidades.Add((d, cantidad));
        }
        var piezas = cantidades.Sum(c => c.cantidad);
        if (piezas == 0) return BadRequest(new { error = "Pide al menos un bono" });
        if (piezas > MaxBonosPorCompra) return BadRequest(new { error = $"Máximo {MaxBonosPorCompra} bonos por compra" });
        decimal total = cantidades.Sum(c => c.denominacion * c.cantidad);
        var usuarioId = UsuarioId;

        await using var tx = await db.Database.BeginTransactionAsync();
        // Descuento atómico: solo si el saldo alcanza (evita saldo negativo con compras concurrentes).
        var afectados = await db.Usuarios.Where(u => u.Id == usuarioId && u.Saldo >= total)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Saldo, u => u.Saldo - total));
        if (afectados == 0) return BadRequest(new { error = "Saldo insuficiente" });

        var bonos = Emitir(usuarioId, cantidades.SelectMany(c => Enumerable.Repeat(c.denominacion, c.cantidad)));
        db.Movimientos.Add(new Movimiento
        {
            UsuarioId = usuarioId, Tipo = TipoMovimiento.COMPRA_BONOS, Monto = -total,
            Detalle = "Bonos: " + string.Join(", ", cantidades.Select(c => $"{c.cantidad} x ${c.denominacion}")),
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Ok(bonos.Select(Dto));
    }

    [Authorize]
    [HttpGet("bonos")]
    public async Task<IEnumerable<BonoFirmadoDto>> Listar()
    {
        var usuarioId = UsuarioId;
        return (await db.Bonos.AsNoTracking().Where(b => b.PropietarioId == usuarioId && b.Estado == EstadoBono.EMITIDO)
            .OrderBy(b => b.EmitidoEn).ToListAsync()).Select(Dto);
    }

    /// Server-to-server (tienda → banco). Todo o nada: si una serie no existe o ya se canjeó → 409.
    [HttpPost("bonos/canjear")]
    public async Task<IActionResult> Canjear(CanjearDto dto, [FromHeader(Name = "X-Api-Key")] string? apiKey)
    {
        var esperado = config["Seguridad:ApiKeyTienda"] ?? throw new InvalidOperationException("Falta Seguridad:ApiKeyTienda");
        if (apiKey is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(apiKey), Encoding.UTF8.GetBytes(esperado)))
            return Unauthorized(new { error = "X-Api-Key inválida" });
        var series = dto.Series ?? [];
        if (series.Count == 0) return BadRequest(new { error = "Envía al menos una serie" });
        if (series.Distinct().Count() != series.Count) return BadRequest(new { error = "Series repetidas" });

        await using var tx = await db.Database.BeginTransactionAsync();
        var ahora = DateTime.UtcNow;
        // UPDATE condicional: de dos canjes simultáneos del mismo bono solo uno puede ganar.
        var afectados = await db.Bonos.Where(b => series.Contains(b.Serie) && b.Estado == EstadoBono.EMITIDO)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Estado, EstadoBono.CANJEADO).SetProperty(b => b.CanjeadoEn, ahora));
        if (afectados != series.Count) // sin Commit: la transacción hace rollback al salir
            return Conflict(new { error = "Algún bono no existe o ya fue canjeado; no se canjeó ninguno" });
        var canjeados = await db.Bonos.AsNoTracking().Where(b => series.Contains(b.Serie))
            .Select(b => new { b.Serie, b.Denominacion, b.PropietarioId }).ToListAsync();
        var total = canjeados.Sum(b => b.Denominacion);
        if (dto.Cobro is < 0 || dto.Cobro > total)
            return BadRequest(new { error = $"Los bonos suman ${total} y no cubren el cobro de ${dto.Cobro}" }); // rollback

        // El cambio es del dueño del primer bono (en la app todos los bonos de un pago son del mismo usuario).
        var duenoId = canjeados.First(b => b.Serie == series[0]).PropietarioId;
        var (piezas, resto) = Desglosar(total - (dto.Cobro ?? total));
        var cambio = Emitir(duenoId, piezas);
        if (resto > 0)
        {
            await db.Usuarios.Where(u => u.Id == duenoId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Saldo, u => u.Saldo + resto));
            db.Movimientos.Add(new Movimiento { UsuarioId = duenoId, Tipo = TipoMovimiento.CAMBIO, Monto = resto, Detalle = "Cambio que no cabe en bonos" });
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Ok(new
        {
            canjeados = canjeados.Select(b => new { b.Serie, b.Denominacion }), total,
            cambio = cambio.Select(Dto), cambioEnCuenta = resto,
        });
    }
}
