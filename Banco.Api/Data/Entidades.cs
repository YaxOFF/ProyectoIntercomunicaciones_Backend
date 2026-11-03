namespace Banco.Api.Data;

public class Usuario
{
    public int Id { get; set; }
    public required string Nombre { get; set; }
    public required string Email { get; set; }
    public string PasswordHash { get; set; } = "";
    public decimal Saldo { get; set; }
    public string? ClabeCifrada { get; set; } // AES-256-GCM base64(nonce|cifrado|tag)
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}

public enum EstadoBono { EMITIDO, CANJEADO }

public class Bono
{
    public Guid Serie { get; set; }
    public int Denominacion { get; set; } // 500|300|100|50|20
    public int PropietarioId { get; set; }
    public Usuario? Propietario { get; set; }
    public DateTime EmitidoEn { get; set; }
    public EstadoBono Estado { get; set; } = EstadoBono.EMITIDO;
    public DateTime? CanjeadoEn { get; set; }
    public string Firma { get; set; } = "";
}

public enum TipoMovimiento { COMPRA_BONOS, DEPOSITO_INICIAL, CAMBIO }

public class Movimiento
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public TipoMovimiento Tipo { get; set; }
    public decimal Monto { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public string Detalle { get; set; } = "";
}
