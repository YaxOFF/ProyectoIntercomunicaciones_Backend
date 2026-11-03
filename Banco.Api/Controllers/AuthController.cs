using System.Security.Claims;
using System.Text;
using Banco.Api.Data;
using Banco.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Banco.Api.Controllers;

public record RegistroDto(string Nombre, string Email, string Password);
public record LoginDto(string Email, string Password);

[ApiController]
[Route("auth")]
public class AuthController(BancoDbContext db, CifradoAes aes, IConfiguration config) : ControllerBase
{
    private static readonly PasswordHasher<Usuario> Hasher = new();

    [HttpPost("registro")]
    public async Task<IActionResult> Registro(RegistroDto dto)
    {
        var email = dto.Email?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(dto.Nombre) || !email.Contains('@') || (dto.Password?.Length ?? 0) < 6)
            return BadRequest(new { error = "Nombre, email válido y contraseña de al menos 6 caracteres son obligatorios" });
        if (await db.Usuarios.AnyAsync(u => u.Email == email))
            return Conflict(new { error = "El email ya está registrado" });

        var u = new Usuario { Nombre = dto.Nombre.Trim(), Email = email, Saldo = BancoDbContext.SaldoInicial,
            ClabeCifrada = aes.Cifrar(CifradoAes.NuevaClabe()) };
        u.PasswordHash = Hasher.HashPassword(u, dto.Password!);
        db.Usuarios.Add(u);
        await db.SaveChangesAsync();
        db.Movimientos.Add(new Movimiento { UsuarioId = u.Id, Tipo = TipoMovimiento.DEPOSITO_INICIAL,
            Monto = BancoDbContext.SaldoInicial, Detalle = "Depósito inicial (mock)" });
        await db.SaveChangesAsync();
        return Created("/cuenta", new { token = CrearToken(u) });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var email = dto.Email?.Trim().ToLowerInvariant() ?? "";
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == email);
        if (u is null || Hasher.VerifyHashedPassword(u, u.PasswordHash, dto.Password ?? "") == PasswordVerificationResult.Failed)
            return Unauthorized(new { error = "Credenciales inválidas" });
        return Ok(new { token = CrearToken(u) });
    }

    private string CrearToken(Usuario u) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity([new Claim("sub", u.Id.ToString()), new Claim("email", u.Email), new Claim("name", u.Nombre)]),
        Issuer = "banco", Audience = "banco",
        Expires = DateTime.UtcNow.AddHours(2),
        SigningCredentials = new SigningCredentials(Program.LlaveJwt(config), SecurityAlgorithms.HmacSha256),
    });
}
