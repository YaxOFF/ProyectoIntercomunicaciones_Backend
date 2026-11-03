using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tienda.Api.Data;

namespace Tienda.Api.Controllers;

[ApiController]
[Route("productos")]
public class ProductosController(TiendaDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<Producto>> Listar() => await db.Productos.AsNoTracking().OrderBy(p => p.Id).ToListAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Producto>> Obtener(int id) =>
        await db.Productos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id) is { } p ? p : NotFound();
}
