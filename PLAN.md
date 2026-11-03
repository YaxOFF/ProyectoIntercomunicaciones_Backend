# PLAN — Monedero digital de bonos (Banco + Tienda online + App Flutter)

Proyecto escolar (materia de Intercomunicaciones / Redes). Un **Banco de bonos digitales** emite bonos de
$500, $300, $100, $50 y $20; el usuario los compra con su saldo bancario (mock: $10,000), los guarda en un
**monedero digital (wallet)** en la app móvil Flutter y los gasta en una **Tienda online** cuyos productos
cuestan múltiplos de bonos. Además se debe **analizar, proponer e implementar seguridad** con los recursos
vistos en clase: criptografía simétrica, criptografía asimétrica y firma digital.

Solo **mock data** (semillas). Dos repos:

- Backend (.NET): `D:\YaxonFolder\Github\ProyectoRedes\Backend` → `https://github.com/YaxOFF/ProyectoIntercomunicaciones_Backend.git`
- Frontend (Flutter): `D:\YaxonFolder\Github\ProyectoRedes\Frontend` → `https://github.com/YaxOFF/ProyectoIntercomunicaciones_Frontend.git`

**Este archivo es el plan maestro de los dos repos.** El Avance y la Bitácora viven solo aquí.

## Decisiones tomadas

| Tema | Decisión |
|---|---|
| Backend | Una solución `ProyectoIntercomunicaciones.sln` con **2 APIs**: `Banco.Api` (puerto 5001) y `Tienda.Api` (puerto 5002). Controllers + Services, .NET 10, EF Core. |
| Bases de datos | **PostgreSQL en Docker**, 2 contenedores: `banco-db` (host 5471, BD `banco`) y `tienda-db` (host 5472, BD `tienda`). Migraciones EF + semilla automática al arrancar. |
| Mock data | Usuarios demo con saldo $10,000; cuentas nuevas también nacen con $10,000. Catálogo de tienda sembrado con precios múltiplos de $20. |
| Auth | Banco: registro/login con contraseña hasheada (PBKDF2 de `PasswordHasher`) y **JWT**. La tienda no tiene cuentas: el pedido guarda el email del comprador. |
| Seguridad (las 3) | Ver sección **Diseño de seguridad**: firma digital RSA de bonos, pago cifrado híbrido RSA-OAEP + AES-GCM, AES para wallet en el celular y dato sensible en BD. |
| Llaves | RSA 2048 generadas al primer arranque en `keys/` (gitignored, volumen en Docker). Banco: llave de firma. Tienda: llave de cifrado. Las públicas se exponen por `GET /llave-publica` (PEM). |
| Canje banco↔tienda | Server-to-server: `POST /bonos/canjear` en el banco con header `X-Api-Key` (secreto compartido en `.env`). Evita doble gasto. |
| Pago exacto | La compra exige que la suma de bonos sea **exactamente** el total (sin cambio). La app sugiere la combinación; si no existe, pide comprar bonos de $20. |
| App | Flutter en **emulador Android**. Base URLs `http://10.0.2.2:5001` (banco) y `http://10.0.2.2:5002` (tienda), en un solo archivo de config. HTTP en local (se documenta que en producción sería HTTPS/TLS). |
| Commits | Conventional Commits, **sin firma de Claude** (sin `Co-Authored-By`, sin "Generated with Claude Code"). |

## Modelo de datos (destino)

**BD `banco`**
- `Usuario`: `Id, Nombre, Email (único), PasswordHash, Saldo (decimal), ClabeCifrada (AES-GCM, base64), CreadoEn`.
- `Bono`: `Serie (Guid, PK), Denominacion (500|300|100|50|20), PropietarioId, EmitidoEn, Estado (EMITIDO|CANJEADO), CanjeadoEn?, Firma (base64)`.
- `Movimiento`: `Id, UsuarioId, Tipo (COMPRA_BONOS|DEPOSITO_INICIAL), Monto, Fecha, Detalle`.

**BD `tienda`**
- `Producto`: `Id, Nombre, Descripcion, Precio (múltiplo de 20), ImagenUrl?, Existencia`.
- `Pedido`: `Id, CompradorEmail, Fecha, Total, Estado (PAGADO)`, partidas `PedidoItem(ProductoId, Cantidad, PrecioUnitario)`, y `BonosUsados` (series).

**Bono firmado (formato que todos respetan)**
```json
{ "serie": "guid", "denominacion": 100, "emitidoEn": "2026-11-03T12:00:00Z", "firma": "base64" }
```
Mensaje firmado = texto canónico `serie|denominacion|emitidoEn` en UTF-8, **RSA-PSS + SHA-256** (salt = 32 bytes, largo del hash). `serie` en minúsculas con guiones; `emitidoEn` exactamente `yyyy-MM-ddTHH:mm:ssZ` (UTC, sin fracciones: el banco trunca a segundos). Ej: `29522387-40ec-4461-9c7f-e9abde06c340|500|2026-11-03T19:53:01Z`.

**Paquete de pago cifrado (app → tienda)**
```json
{ "llaveCifrada": "base64 RSA-OAEP-SHA256(llave AES 256)", "nonce": "base64 12 bytes", "datos": "base64 AES-GCM(json)+tag" }
```
`json` interno: `{ "email": "...", "items": [{ "productoId": 1, "cantidad": 2 }], "bonos": [ <bonos firmados> ] }`.

## Endpoints (destino)

**Banco.Api :5001**
- `POST /auth/registro` `{nombre,email,password}` → crea usuario con $10,000 y CLABE mock cifrada.
- `POST /auth/login` `{email,password}` → `{token}`.
- `GET /cuenta` (JWT) → `{nombre,email,saldo,clabe (descifrada, enmascarada)}`; `GET /cuenta/movimientos`.
- `POST /bonos/comprar` (JWT) `{ "500": 1, "100": 3 }` → descuenta saldo, emite y firma bonos, devuelve la lista.
- `GET /bonos` (JWT) → bonos del usuario (EMITIDO).
- `GET /llave-publica` → PEM de la llave de firma.
- `POST /bonos/canjear` (`X-Api-Key`) `{series:[...]}` → en una transacción: todos EMITIDO → CANJEADO; si alguno no existe o ya fue canjeado → 409 y no cambia nada.

**Tienda.Api :5002**
- `GET /productos`, `GET /productos/{id}`.
- `GET /llave-publica` → PEM de la llave de cifrado de la tienda.
- `POST /compras` (paquete cifrado) → descifra, valida stock, **verifica firma de cada bono** con la llave pública del banco, valida suma exacta, llama a `canjear` en el banco, descuenta stock, guarda pedido → `{pedidoId,total}`.
- `GET /pedidos?email=` → historial.

## Diseño de seguridad (lo que se presenta en clase)

| Amenaza | Técnica | Dónde |
|---|---|---|
| Falsificar o alterar un bono (cambiar $20 por $500) | **Firma digital** RSA-PSS/SHA-256: el banco firma, tienda y app verifican con la llave pública del banco. | `Banco.Api` firma, `Tienda.Api` y app verifican |
| Espiar el pago en la red (robar bonos en tránsito) | **Asimétrica + simétrica (esquema híbrido)**: la app cifra el pedido con AES-256-GCM y cifra esa llave con RSA-OAEP de la tienda. Solo la tienda descifra. | App → `Tienda.Api /compras` |
| Robo del celular / lectura del almacenamiento | **Simétrica** AES-256-GCM: el wallet se guarda cifrado con llave derivada de un PIN (PBKDF2). | App |
| Fuga de la BD del banco | **Simétrica** AES-GCM para la CLABE; contraseñas con hash PBKDF2 (no se cifran, se hashean). | `Banco.Api` |
| Gastar el mismo bono dos veces | Registro de canje transaccional en el banco (estado CANJEADO). | `Banco.Api /bonos/canjear` |
| Suplantar a la tienda ante el banco | `X-Api-Key` server-to-server. | Tienda → Banco |
| Sesiones | JWT con expiración. | Banco |

Fuera de alcance (se menciona como propuesta, no se implementa): TLS/HTTPS con certificados, HSM para llaves, rotación de llaves.

## Fases

Una fase por sesión. Cada fase deja todo funcionando. Fases 1-5 en `Backend`, 6-9 en `Frontend`.

### Fase 0 — Planear ✅
Clonar los dos repos vacíos, este `PLAN.md`, `CLAUDE.md` en ambos repos y en la carpeta padre.

### Fase 1 — Solución, Docker y BDs con semilla (Backend) ✅
Meta: las dos APIs arrancan contra sus dos Postgres y sirven datos mock.
- `ProyectoIntercomunicaciones.sln`, `Banco.Api/`, `Tienda.Api/` (`dotnet new webapi --use-controllers`), `.gitignore` (.NET + `keys/`), `.env.example`.
- `docker-compose.yml` con `banco-db` y `tienda-db` (postgres:17, volúmenes, puertos 5471/5472).
- EF Core + Npgsql en ambas: `BancoDbContext`, `TiendaDbContext`, entidades del modelo, migración inicial, `Database.Migrate()` + semilla al arrancar (2 usuarios demo con $10,000; ~10 productos).
- `GET /productos` en tienda; `GET /health` en ambas.
- Verificar: `docker compose up -d banco-db tienda-db`, `dotnet run` en cada API, `curl` a `/productos` y `/health`.

### Fase 2 — Banco: cuentas, JWT y AES en BD (Backend) ✅
- `AuthController` (registro/login), `PasswordHasher`, emisión de JWT; `CuentaController` (cuenta, movimientos).
- `Services/CifradoAes.cs` (AES-GCM, llave de `.env`) para CLABE mock al registrar; se muestra enmascarada.
- Verificar: archivo `Banco.Api/banco.http` con registro → login → cuenta; ver en Postgres que la CLABE está cifrada.

### Fase 3 — Banco: bonos firmados y canje (Backend) ✅
- `Services/LlavesRsa.cs` (genera/carga `keys/banco-firma.pem`), `Services/FirmaBonos.cs` (RSA-PSS SHA-256, mensaje canónico).
- `BonosController`: comprar (valida denominaciones y saldo, transacción, movimiento), listar, `llave-publica`, `canjear` con `X-Api-Key`.
- Proyecto `Tests/` (xUnit): firma válida verifica; bono alterado no verifica; doble canje → 409.
- Verificar: `dotnet test` + flujo en `banco.http`.

### Fase 4 — Tienda: pago cifrado híbrido y compra (Backend) ✅
- `Services/LlavesRsa` de la tienda (`keys/tienda-cifrado.pem`), `Services/DescifradoHibrido.cs` (RSA-OAEP-SHA256 + AES-GCM).
- `ComprasController`: descifrar → verificar firmas (llave pública del banco obtenida y cacheada por HTTP) → suma exacta → `canjear` → stock → pedido. `GET /pedidos?email=`.
- Tests: paquete cifrado de prueba (cliente C# en el test) compra OK; bono alterado → 400; bono ya gastado → 409; suma distinta → 400.
- Verificar: `dotnet test` y compra real con un bono emitido en la Fase 3.

### Fase 5 — Stack completo en Docker y docs del backend (Backend) ✅
- `Dockerfile` por API, `docker-compose.yml` con las 4 piezas, volumen de `keys/`, todo por `.env`.
- `README.md` (cómo levantar, endpoints, usuarios demo) y `SEGURIDAD.md` (análisis de amenazas + propuesta + qué se implementó, con los diagramas de flujo de firma y cifrado híbrido).
- Verificar: `docker compose up --build` limpio y flujo completo comprar bonos → comprar en tienda con `.http`.

### Fase 6 — App Flutter: base, login y registro (Frontend) ✅
- `flutter create` (paquete `proyecto_intercomunicaciones`, Android), `lib/config.dart` con URLs, cliente HTTP, guardado del JWT en `flutter_secure_storage`.
- Pantallas: Login, Registro, Inicio (nombre, saldo, CLABE enmascarada, movimientos).
- Verificar: `flutter analyze` sin errores y probar en emulador contra el backend.

### Fase 7 — App: comprar bonos y wallet cifrado (Frontend) ✅
- Pantalla Comprar bonos (contador por denominación, total, confirma). Pantalla Wallet (bonos con ✓ "firma válida" verificada en la app con la llave pública del banco).
- Wallet guardado cifrado con AES-GCM, llave derivada del PIN (PBKDF2); PIN se pide al abrir el wallet. Paquete cripto: `pointycastle` (+ `basic_utils` para PEM) — decidir y anotar en Bitácora.
- Verificar: comprar bonos, cerrar y reabrir app, el wallet se descifra con el PIN y falla con PIN incorrecto.
- Si no cabe: 7a comprar bonos + wallet en claro, 7b cifrado AES + verificación de firma.

### Fase 8 — App: tienda y pago cifrado (Frontend)
- Catálogo, carrito, checkout: elige la combinación exacta de bonos del wallet (subset-sum simple), arma el paquete híbrido (AES-GCM + RSA-OAEP con la llave de la tienda), envía, quita los bonos gastados del wallet. Historial de pedidos.
- Verificar: compra completa en emulador; repetir con los mismos bonos → error de doble gasto mostrado bien.

### Fase 9 — Pulido y demo (ambos repos)
- README del frontend, guion de demo (incluye "ataques": bono alterado y doble gasto rechazados), capturas en `docs/`.
- (opcional) Pantalla "Seguridad" en la app que muestre el paquete cifrado enviado, para la exposición.

## Protocolo por sesión

1. Leer **Avance** y tomar la primera fase sin marcar. Leer solo los archivos que esa fase toca.
2. Implementar la fase completa, nada de fases siguientes.
3. Verificar: backend → `docker compose up -d banco-db tienda-db`, `dotnet build`, `dotnet test` (desde Fase 3) y el flujo en los `.http`; frontend → `flutter analyze` y probar en el emulador Android con el backend corriendo.
4. Marcar la fase en **Avance** y escribir 2-5 líneas en **Bitácora** (este archivo, repo Backend).
5. Commit (Conventional Commits, **sin firma de Claude**) + `git push` en cada repo tocado.
6. Avisar al usuario que puede hacer `/clear` y continuar con la siguiente fase.

## Avance

- [x] Fase 0 — Planear
- [x] Fase 1 — Solución, Docker y BDs con semilla
- [x] Fase 2 — Banco: cuentas, JWT y AES en BD
- [x] Fase 3 — Banco: bonos firmados y canje
- [x] Fase 4 — Tienda: pago cifrado híbrido y compra
- [x] Fase 5 — Stack completo en Docker y docs del backend
- [x] Fase 6 — App Flutter: base, login y registro
- [x] Fase 7 — App: comprar bonos y wallet cifrado
- [ ] Fase 8 — App: tienda y pago cifrado
- [ ] Fase 9 — Pulido y demo

## Bitácora

- **2026-11-03 · Fase 0** — Repos clonados (vacíos). Decidido: 2 APIs en 1 solución, Postgres en Docker (2 contenedores), las 3 técnicas de seguridad combinadas, app en emulador Android. Plan maestro aquí; Frontend apunta a este archivo. Toolchain: .NET 10.0.401, Docker 29.8, Flutter en `C:\dev\flutter`.
- **2026-11-03 · Fase 1** — Solución `.sln` con `Banco.Api` (:5001) y `Tienda.Api` (:5002), EF Core 10 + Npgsql, migración `Inicial` en `Data/Migrations` de cada API, `Migrate()` + semilla al arrancar. Usuarios demo `ana@demo.com` / `luis@demo.com`, contraseña `demo123`, $10,000 + movimiento DEPOSITO_INICIAL; 10 productos (CHECK en BD: precio múltiplo de 20). `GET /health` (prueba conexión a BD), `GET /productos`, `GET /productos/{id}`. **Puertos de BD cambiados a 5471/5472** (5433/5434 ya ocupados por otros proyectos en esta máquina); compose con `name: intercom`. Verificado con curl. Pendiente Fase 2: `ClabeCifrada` es nullable, los usuarios semilla aún no tienen CLABE.
- **2026-11-03 · Fase 2** — `AuthController` (registro valida nombre/email/password>=6, email en minúsculas, 409 si existe; login 401 genérico), JWT HS256 2 h con claim `sub` (`MapInboundClaims=false`), `CuentaController` (`/cuenta`, `/cuenta/movimientos`). `Services/CifradoAes.cs`: AES-256-GCM, formato base64(nonce12|cifrado|tag16), CLABE mock `002180`+12 dígitos, se muestra `**************1234`. Llaves en config `Seguridad:AesKey` (32 bytes base64) y `Seguridad:JwtKey` (>=32 chars): valores dev en `appsettings.Development.json`, producción por env `Seguridad__*` (en `.env.example`). `Sembrar` asigna CLABE a usuarios sin ella. Enums se serializan como string. Verificado con curl (`banco.http`) y `psql`: CLABE guardada cifrada.
- **2026-11-03 · Fase 3** — `Services/FirmaBonos.cs`: RSA 2048 en `keys/banco-firma.pem` (se crea al primer arranque, carpeta por `Seguridad:KeysDir`), RSA-PSS SHA-256, `emitidoEn` truncado a segundos y formato canónico fijado en el modelo de datos. `BonosController`: `GET /llave-publica` (SPKI PEM `BEGIN PUBLIC KEY`), `POST /bonos/comprar` (máx 100 bonos, descuento atómico `UPDATE ... WHERE Saldo >= total` + bonos + movimiento `COMPRA_BONOS` con monto **negativo** en una transacción), `GET /bonos`, `POST /bonos/canjear` (`X-Api-Key` = `Seguridad:ApiKeyTienda`, comparación en tiempo constante; UPDATE condicional todo-o-nada; 401/400/409; responde `{canjeados:[{serie,denominacion}], total}` para que la tienda confirme montos). Proyecto `Tests/` (xUnit + `WebApplicationFactory`, contra `banco-db` de Docker): 4 pruebas verdes (firma válida/alterada, compra y saldo, inválidos 400, doble canje 409 y todo-o-nada). Verificado también con curl. Fase 4: la tienda necesita `Seguridad:ApiKeyTienda` igual y verificar con `RSASignaturePadding.Pss`.
- **2026-11-03 · Fase 4** — `Services/DescifradoHibrido.cs` (RSA 2048 en `keys/tienda-cifrado.pem`, RSA-OAEP-SHA256 + AES-256-GCM, `datos` = cifrado|tag16; cualquier fallo → 400), `Services/BancoCliente.cs` (typed HttpClient a `Banco:Url`, llave pública del banco cacheada estática, verificación RSA-PSS con el mismo mensaje canónico, canje con `X-Api-Key`). `ComprasController`: `GET /llave-publica`, `POST /compras` (orden: descifrar → validar → firmas → suma exacta → transacción: stock condicional → canje en el banco → pedido → commit; 409 sin stock o doble gasto, 503 banco caído) y `GET /pedidos?email=`. JSON del paquete en camelCase (`JsonSerializerOptions.Web`). Tests: `ComprasTests` (banco y tienda en TestServer, Tienda referenciada con `extern alias tienda`) — 8/8 verdes. Compra real verificada con ambas APIs corriendo (200, luego 409). Riesgo anotado: si el banco canjea y el commit local falla, los bonos se pierden.
- **2026-11-03 · Fase 5** — `Dockerfile` multi-stage por API (aspnet 10, usuario `app`, `:8080`, `/app/keys` como volumen `banco-keys`/`tienda-keys`), `.dockerignore`. `docker-compose.yml` con las 4 piezas: healthcheck `pg_isready` + `depends_on: service_healthy`, secretos obligatorios con `${VAR:?}` (compose falla si falta alguno en `.env`, **también para `up banco-db tienda-db`**), puertos API en `127.0.0.1:5001/5002` (`BANCO_PORT`/`TIENDA_PORT`), `Banco__Url=http://banco-api:8080/`. `README.md` y `SEGURIDAD.md` (amenazas, diagramas mermaid de firma e híbrido, pruebas, propuestas). Gotcha: la BD es compartida con `dotnet run`/tests, así que el `.env` local usa la misma `AesKey` que `appsettings.Development.json` (si no, `/cuenta` da 500 por GCM). Verificado: stack en Docker → login, comprar bonos, compra cifrada 200 y repetida 409; arranque limpio en proyecto aparte (`-p intercom-limpio`); `dotnet test` 8/8.
- **2026-11-03 · Fase 6** — `flutter create` (org `com.yaxoff`, solo Android, Flutter 3.44.4), deps `http` + `flutter_secure_storage`. `lib/config.dart` (URLs `10.0.2.2`, sobreescribibles con `--dart-define=BANCO_URL/TIENDA_URL`), `lib/api.dart` (cliente estático, JWT en secure storage, errores `{error}` del backend → `ApiError`, 401 borra sesión), `lib/pantallas/` login/registro/inicio (saldo, CLABE enmascarada, movimientos, pull-to-refresh, logout). Manifest: `INTERNET` + `usesCleartextTraffic` (HTTP local). Verificado: `flutter analyze` limpio; APK debug en AVD `aaocsa` (emulador en `%LOCALAPPDATA%\Android\Sdk\emulator`, adb en `C:\dev\android-sdk`) contra stack Docker: login malo → "Credenciales inválidas", login ana OK, sesión sobrevive reinicio, logout, validaciones de registro y registro nuevo con $10,000. Gotcha adb desde Git Bash: `MSYS_NO_PATHCONV=1`.
- **2026-11-03 · Fase 7** — Cripto: `pointycastle` 4 + `basic_utils` 5.8 (PEM SPKI → `RSAPublicKey`), todo en `lib/cripto.dart`: `verificarBono` (PSSSigner SHA-256, salt 32) y `LlaveWallet` (PBKDF2-HMAC-SHA256 100k iteraciones → AES-256-GCM, guardado `base64(salt16|nonce12|cifrado|tag16)`; PIN incorrecto = falla el tag). `lib/wallet.dart`: wallet por usuario en `flutter_secure_storage` (`wallet_<email>`), PBKDF2 en otro isolate (`compute`), `agregar` sin duplicar series y `reemplazar` (botón sincronizar con `GET /bonos`). Pantallas `wallet.dart` (crear PIN 4-8 dígitos con confirmación / abrir, lista con chip "Firma válida" verificada en la app) y `comprar_bonos.dart` (contadores, máx 100, confirmación). `flutter test` con fixture real del banco (`test/bono.json`, `test/banco.pem`): firma válida/alterada y PIN bueno/malo. Verificado en emulador: compra $720, saldo $9,280, reabrir app → PIN 9999 rechazado, 1234 abre con 4 bonos válidos. Fase 8: el checkout necesita el `Wallet` abierto (usar el de `WalletPantalla`) y quitar los bonos gastados con `reemplazar`/filtro.
