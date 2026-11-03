using Banco.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<BancoDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Banco")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BancoDbContext>();
    db.Database.Migrate();
    db.Sembrar();
}

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", async (BancoDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.Problem("BD no disponible", statusCode: 503));

app.Run();
