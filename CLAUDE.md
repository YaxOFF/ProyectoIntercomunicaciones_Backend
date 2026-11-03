# CLAUDE.md — Backend (Banco.Api + Tienda.Api)

## Al empezar cada sesión

1. Leer `PLAN.md` → **Avance** y seguir el **Protocolo por sesión**. Una fase por sesión.
2. `PLAN.md` es el plan maestro de este repo y de `../Frontend`. Avance y Bitácora solo viven aquí.

## Reglas duras

- Commits y push **sin ninguna firma de Claude**: nada de `Co-Authored-By`, nada de "Generated with Claude Code". Solo el mensaje Conventional Commit.
- Solo mock data (semillas). Nada de servicios externos reales.
- Respetar los formatos de **Bono firmado** y **Paquete de pago cifrado** de `PLAN.md`: la app Flutter depende de ellos.
- Nunca commitear `keys/` ni `.env`.
