using Banco.Api.Data;
using Banco.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Banco.Api.Controllers;

[ApiController]
[Authorize]
[Route("cuenta")]
public class CuentaController(BancoDbContext db, CifradoAes aes) : ControllerBase
{
    private int UsuarioId => int.Parse(User.FindFirst("sub")!.Value);

    [HttpGet]
    public async Task<IActionResult> Obtener()
    {
        var u = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == UsuarioId);
        if (u is null) return NotFound();
        var clabe = u.ClabeCifrada is null ? null : CifradoAes.Enmascarar(aes.Descifrar(u.ClabeCifrada));
        return Ok(new { u.Nombre, u.Email, u.Saldo, clabe });
    }

    [HttpGet("movimientos")]
    public async Task<List<Movimiento>> Movimientos() =>
        await db.Movimientos.AsNoTracking().Where(m => m.UsuarioId == UsuarioId).OrderByDescending(m => m.Fecha).ToListAsync();
}
