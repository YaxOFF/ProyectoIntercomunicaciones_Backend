using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Tienda.Api.Data;
using Tienda.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddDbContext<TiendaDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Tienda")));
builder.Services.AddSingleton<DescifradoHibrido>();
builder.Services.AddHttpClient<BancoCliente>(c => c.BaseAddress = new Uri(builder.Configuration["Banco:Url"] ?? "http://localhost:5001/"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TiendaDbContext>();
    db.Database.Migrate();
    db.Sembrar();
}

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", async (TiendaDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.Problem("BD no disponible", statusCode: 503));

app.Run();

public partial class Program;
