# ProyectoIntercomunicaciones — Backend

Backend del monedero digital de bonos: un **Banco** emite bonos firmados ($500, $300, $100, $50, $20) que el
usuario compra con su saldo, y una **Tienda online** los acepta como pago. App móvil (Flutter):
[ProyectoIntercomunicaciones_Frontend](https://github.com/YaxOFF/ProyectoIntercomunicaciones_Frontend).

Solo mock data (semillas). El análisis de seguridad está en [SEGURIDAD.md](SEGURIDAD.md).

| Pieza | Qué es | Puerto en el host |
|---|---|---|
| `banco-api` | `Banco.Api` (.NET 10): cuentas, JWT, bonos firmados, canje | `5001` |
| `tienda-api` | `Tienda.Api` (.NET 10): catálogo, pago cifrado, pedidos | `5002` |
| `banco-db` | PostgreSQL 17, BD `banco` | `5471` |
| `tienda-db` | PostgreSQL 17, BD `tienda` | `5472` |

Todos los puertos se publican solo en `127.0.0.1`. El emulador Android llega a ellos por `10.0.2.2`.

## Levantar todo con Docker

```bash
cp .env.example .env      # y llenar Seguridad__AesKey, Seguridad__JwtKey, Seguridad__ApiKeyTienda
docker compose up -d --build
curl http://localhost:5001/health
curl http://localhost:5002/productos
```

- Generar cada secreto: `python -c "import os,base64;print(base64.b64encode(os.urandom(32)).decode())"`.
- `docker compose` falla con un mensaje claro si falta algún secreto en `.env`.
- Al arrancar, cada API aplica sus migraciones y siembra los datos mock.
- Las llaves RSA se generan al primer arranque y viven en los volúmenes `banco-keys` y `tienda-keys`.
- **Ojo:** la CLABE en BD está cifrada con `Seguridad__AesKey`. Si cambias esa llave con datos ya existentes,
  `GET /cuenta` falla (AES-GCM detecta que no puede autenticar el dato). Usa la misma llave que
  `Banco.Api/appsettings.Development.json` si compartes la BD con `dotnet run`, o borra los datos con
  `docker compose down -v`.

## Desarrollo sin Docker para las APIs

```bash
docker compose up -d banco-db tienda-db
dotnet run --project Banco.Api     # :5001, entorno Development (secretos dev en appsettings.Development.json)
dotnet run --project Tienda.Api    # :5002
dotnet test                        # 8 pruebas; usan banco-db y tienda-db
```

No corras `banco-api`/`tienda-api` de Docker y `dotnet run` al mismo tiempo: usan los mismos puertos.
Peticiones de ejemplo: `Banco.Api/banco.http` y `Tienda.Api/tienda.http` (VS Code REST Client / Rider / VS).

## Usuarios demo

| Email | Contraseña | Saldo inicial |
|---|---|---|
| `ana@demo.com` | `demo123` | $10,000 |
| `luis@demo.com` | `demo123` | $10,000 |

Las cuentas nuevas también nacen con $10,000 y una CLABE mock.

## Endpoints

**Banco.Api (`:5001`)**

| Método y ruta | Auth | Qué hace |
|---|---|---|
| `POST /auth/registro` `{nombre,email,password}` | — | Crea usuario con $10,000 y CLABE cifrada. |
| `POST /auth/login` `{email,password}` | — | Devuelve `{token}` (JWT, 2 h). |
| `GET /cuenta` | JWT | `{nombre,email,saldo,clabe}` con la CLABE enmascarada. |
| `GET /cuenta/movimientos` | JWT | Historial de movimientos. |
| `POST /bonos/comprar` `{"500":1,"100":3}` | JWT | Descuenta saldo, emite y firma los bonos. |
| `GET /bonos` | JWT | Bonos del usuario en estado `EMITIDO`. |
| `GET /llave-publica` | — | PEM de la llave pública de firma. |
| `POST /bonos/canjear` `{series:[...]}` | `X-Api-Key` | Solo para la tienda. Todo o nada; 409 si alguno ya fue gastado. |
| `GET /health` | — | Estado de la BD. |

**Tienda.Api (`:5002`)**

| Método y ruta | Qué hace |
|---|---|
| `GET /productos`, `GET /productos/{id}` | Catálogo (precios múltiplos de $20). |
| `GET /llave-publica` | PEM de la llave pública de cifrado de la tienda. |
| `POST /compras` `{llaveCifrada,nonce,datos}` | Paquete cifrado híbrido. Verifica firmas, suma exacta, canjea en el banco, descuenta stock. `{pedidoId,total}`. |
| `GET /pedidos?email=` | Historial de pedidos de un comprador. |
| `GET /health` | Estado de la BD. |

Errores de `POST /compras`: 400 (paquete o bono inválido, suma distinta del total), 409 (sin stock o bono ya
gastado), 503 (banco no disponible).

## Formatos que la app debe respetar

**Bono firmado**

```json
{ "serie": "guid", "denominacion": 100, "emitidoEn": "2026-11-03T12:00:00Z", "firma": "base64" }
```

Mensaje firmado: `serie|denominacion|emitidoEn` en UTF-8, RSA-PSS + SHA-256 (salt de 32 bytes). `serie` en
minúsculas con guiones; `emitidoEn` en formato `yyyy-MM-ddTHH:mm:ssZ`.

**Paquete de pago cifrado (app → tienda)**

```json
{ "llaveCifrada": "base64 RSA-OAEP-SHA256(llave AES 256)", "nonce": "base64 12 bytes", "datos": "base64 AES-GCM(json)+tag" }
```

JSON interno: `{ "email": "...", "items": [{ "productoId": 1, "cantidad": 2 }], "bonos": [ <bonos firmados> ] }`.

## Estructura

```
Banco.Api/    Controllers (Auth, Cuenta, Bonos), Services (CifradoAes, FirmaBonos), Data (EF Core + migraciones)
Tienda.Api/   Controllers (Productos, Compras), Services (DescifradoHibrido, BancoCliente), Data
Tests/        xUnit + WebApplicationFactory (firma, canje, compras)
```
