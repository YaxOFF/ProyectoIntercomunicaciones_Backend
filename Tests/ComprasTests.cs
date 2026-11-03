extern alias tienda;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Banco.Api.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using BancoCliente = tienda::Tienda.Api.Services.BancoCliente;
using Producto = tienda::Tienda.Api.Data.Producto;
using TiendaDb = tienda::Tienda.Api.Data.TiendaDbContext;
using TiendaProgram = tienda::Program;

namespace Tests;

/// Banco y tienda en memoria; la tienda habla con el banco por el TestServer. Requiere `banco-db` y `tienda-db`.
public class ComprasTests(ComprasTests.Entorno e) : IClassFixture<ComprasTests.Entorno>
{
    public class Entorno : IDisposable
    {
        public BonosTests.BancoFactory Banco { get; } = new();
        public WebApplicationFactory<TiendaProgram> Tienda { get; }
        public int ProductoId { get; } // precio $120, existencia de sobra

        public Entorno()
        {
            Tienda = new WebApplicationFactory<TiendaProgram>().WithWebHostBuilder(b => b
                .UseEnvironment("Development")
                .UseSetting("Seguridad:KeysDir", Path.Combine(Path.GetTempPath(), "tienda-tests-keys"))
                .ConfigureTestServices(s => s.AddHttpClient<BancoCliente>()
                    .ConfigurePrimaryHttpMessageHandler(() => Banco.Server.CreateHandler())));
            using var scope = Tienda.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TiendaDb>();
            var p = db.Productos.FirstOrDefault(x => x.Nombre == "Producto de pruebas");
            if (p is null) db.Productos.Add(p = new Producto { Nombre = "Producto de pruebas", Precio = 120, Existencia = 0 });
            p.Existencia = 100_000;
            db.SaveChanges();
            ProductoId = p.Id;
        }

        public void Dispose() { Tienda.Dispose(); Banco.Dispose(); }
    }

    /// Lo que hará la app: AES-256-GCM del pedido + llave AES cifrada con RSA-OAEP-SHA256 de la tienda.
    private static async Task<object> Cifrar(HttpClient tienda, object pago)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(await tienda.GetStringAsync("/llave-publica"));
        var llave = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plano = JsonSerializer.SerializeToUtf8Bytes(pago, JsonSerializerOptions.Web);
        var cifrado = new byte[plano.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(llave, 16)) aes.Encrypt(nonce, plano, cifrado, tag);
        return new
        {
            llaveCifrada = Convert.ToBase64String(rsa.Encrypt(llave, RSAEncryptionPadding.OaepSHA256)),
            nonce = Convert.ToBase64String(nonce),
            datos = Convert.ToBase64String([.. cifrado, .. tag]),
        };
    }

    private async Task<HttpResponseMessage> Pagar(List<BonoFirmadoDto> bonos, int cantidad = 1, string email = "comprador@demo.com")
    {
        var t = e.Tienda.CreateClient();
        var paquete = await Cifrar(t, new { email, items = new[] { new { productoId = e.ProductoId, cantidad } }, bonos });
        return await t.PostAsJsonAsync("/compras", paquete);
    }

    private async Task<(HttpClient banco, List<BonoFirmadoDto> bonos)> Bonos(Dictionary<string, int> pedido)
    {
        var c = await BonosTests.ClienteConSesion(e.Banco);
        return (c, await BonosTests.Comprar(c, pedido));
    }

    [Fact]
    public async Task Compra_Ok_Canjea_Y_GuardaPedido()
    {
        var (banco, bonos) = await Bonos(new() { ["100"] = 2, ["20"] = 2 }); // $240 = 2 x $120
        var email = $"c{Guid.NewGuid():N}@demo.com";
        var r = await Pagar(bonos, cantidad: 2, email: email);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(240m, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetDecimal());

        Assert.Empty((await banco.GetFromJsonAsync<List<BonoFirmadoDto>>("/bonos"))!); // ya canjeados
        var pedidos = await e.Tienda.CreateClient().GetFromJsonAsync<JsonElement>($"/pedidos?email={email.ToUpper()}");
        Assert.Equal(1, pedidos.GetArrayLength());
        Assert.Equal(4, pedidos[0].GetProperty("bonosUsados").GetArrayLength());
    }

    [Fact]
    public async Task BonoAlterado_400_Y_NoSeCanjea()
    {
        var (banco, bonos) = await Bonos(new() { ["20"] = 1 });
        var r = await Pagar([bonos[0] with { Denominacion = 120 }]); // $20 -> $120
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Firma inválida", await r.Content.ReadAsStringAsync());
        Assert.Single((await banco.GetFromJsonAsync<List<BonoFirmadoDto>>("/bonos"))!);
    }

    [Fact]
    public async Task DobleGasto_409()
    {
        var (_, bonos) = await Bonos(new() { ["100"] = 1, ["20"] = 1 });
        Assert.Equal(HttpStatusCode.OK, (await Pagar(bonos)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Pagar(bonos)).StatusCode);
    }

    [Fact]
    public async Task SumaDistinta_400_Y_PaqueteBasura_400()
    {
        var (_, bonos) = await Bonos(new() { ["100"] = 1 });
        var r = await Pagar(bonos); // $100 para un total de $120
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("exacto", await r.Content.ReadAsStringAsync());

        var basura = new { llaveCifrada = Convert.ToBase64String(new byte[256]), nonce = "AAAAAAAAAAAAAAAA", datos = "AAAAAAAAAAAAAAAAAAAAAA==" };
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Tienda.CreateClient().PostAsJsonAsync("/compras", basura)).StatusCode);
    }
}
