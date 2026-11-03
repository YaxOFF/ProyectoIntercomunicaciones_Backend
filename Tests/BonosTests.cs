using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Banco.Api.Controllers;
using Banco.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Tests;

/// Pruebas de integración contra la BD real de Docker: `docker compose up -d banco-db`.
public class BonosTests(BonosTests.BancoFactory factory) : IClassFixture<BonosTests.BancoFactory>
{
    public class BancoFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment("Development")
                .UseSetting("Seguridad:KeysDir", Path.Combine(Path.GetTempPath(), "banco-tests-keys"));
    }

    private Task<HttpClient> ClienteConSesion() => ClienteConSesion(factory);

    internal static async Task<HttpClient> ClienteConSesion(BancoFactory factory)
    {
        var c = factory.CreateClient();
        var email = $"test{Guid.NewGuid():N}@demo.com";
        (await c.PostAsJsonAsync("/auth/registro", new { nombre = "Test", email, password = "secreto1" })).EnsureSuccessStatusCode();
        var login = await (await c.PostAsJsonAsync("/auth/login", new { email, password = "secreto1" })).Content.ReadFromJsonAsync<Dictionary<string, string>>();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!["token"]);
        return c;
    }

    internal static async Task<List<BonoFirmadoDto>> Comprar(HttpClient c, object pedido)
    {
        var r = await c.PostAsJsonAsync("/bonos/comprar", pedido);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<List<BonoFirmadoDto>>())!;
    }

    private static async Task<RSA> LlavePublica(HttpClient c)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(await c.GetStringAsync("/llave-publica"));
        return rsa;
    }

    private static Task<HttpResponseMessage> Canjear(HttpClient c, IEnumerable<Guid> series, string apiKey = "dev-api-key-tienda")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/bonos/canjear") { Content = JsonContent.Create(new { series }) };
        req.Headers.Add("X-Api-Key", apiKey);
        return c.SendAsync(req);
    }

    [Fact]
    public async Task FirmaValida_Verifica_Y_BonoAlterado_NoVerifica()
    {
        var c = await ClienteConSesion();
        var bono = (await Comprar(c, new Dictionary<string, int> { ["20"] = 1 })).Single();
        using var llave = await LlavePublica(c);

        Assert.True(FirmaBonos.Verificar(llave, bono.Serie, bono.Denominacion, bono.EmitidoEn, bono.Firma));
        Assert.False(FirmaBonos.Verificar(llave, bono.Serie, 500, bono.EmitidoEn, bono.Firma)); // $20 -> $500
        Assert.False(FirmaBonos.Verificar(llave, Guid.NewGuid(), bono.Denominacion, bono.EmitidoEn, bono.Firma));
        Assert.False(FirmaBonos.Verificar(llave, bono.Serie, bono.Denominacion, bono.EmitidoEn, "no-es-base64!"));
    }

    [Fact]
    public async Task Comprar_DescuentaSaldo_Y_ListaBonos()
    {
        var c = await ClienteConSesion();
        var bonos = await Comprar(c, new Dictionary<string, int> { ["500"] = 1, ["100"] = 3 });
        Assert.Equal(800, bonos.Sum(b => b.Denominacion));

        var cuenta = await c.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/cuenta");
        Assert.Equal(9200m, cuenta!["saldo"].GetDecimal());
        Assert.Equal(4, (await c.GetFromJsonAsync<List<BonoFirmadoDto>>("/bonos"))!.Count);
    }

    [Fact]
    public async Task Comprar_Invalido_400()
    {
        var c = await ClienteConSesion();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/bonos/comprar", new Dictionary<string, int> { ["70"] = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/bonos/comprar", new Dictionary<string, int> { ["500"] = 21 })).StatusCode); // $10,500 > saldo
    }

    [Fact]
    public async Task DobleCanje_409_Y_TodoONada()
    {
        var c = await ClienteConSesion();
        var bonos = await Comprar(c, new Dictionary<string, int> { ["50"] = 2 });

        Assert.Equal(HttpStatusCode.Unauthorized, (await Canjear(c, [bonos[0].Serie], "mala")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Canjear(c, [bonos[0].Serie])).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Canjear(c, [bonos[0].Serie])).StatusCode);

        // Lote con uno ya gastado: falla completo y el otro bono sigue disponible.
        Assert.Equal(HttpStatusCode.Conflict, (await Canjear(c, [bonos[0].Serie, bonos[1].Serie])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Canjear(c, [bonos[1].Serie])).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<List<BonoFirmadoDto>>("/bonos"))!);
    }
}
