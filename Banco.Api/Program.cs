using System.Text.Json.Serialization;
using System.Text;
using Banco.Api.Data;
using Banco.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddDbContext<BancoDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Banco")));
builder.Services.AddSingleton<CifradoAes>();
builder.Services.AddSingleton<FirmaBonos>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = "banco", ValidAudience = "banco",
        IssuerSigningKey = Program.LlaveJwt(builder.Configuration),
    };
});
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BancoDbContext>();
    db.Database.Migrate();
    db.Sembrar(app.Services.GetRequiredService<CifradoAes>());
}

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", async (BancoDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.Problem("BD no disponible", statusCode: 503));

app.Run();

public partial class Program
{
    public static SymmetricSecurityKey LlaveJwt(IConfiguration config)
    {
        var secreto = config["Seguridad:JwtKey"] ?? throw new InvalidOperationException("Falta Seguridad:JwtKey");
        if (secreto.Length < 32) throw new InvalidOperationException("Seguridad:JwtKey debe tener al menos 32 caracteres");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secreto));
    }
}
