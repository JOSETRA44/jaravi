---
tags: [jaravi, dashboard, wpf, mvvm, observabilidad]
---

# Dashboard

`Jaravi.Dashboard` es una GUI WPF con patrón MVVM que observa el ecosistema Jaravi en tiempo real. Nunca referencia al [[Motor (Engine)|Engine]] directamente; se comunica exclusivamente vía HTTP REST y WebSocket con el [[Servidor MCP]].

## Arquitectura

- `MainViewModel` — estado global: lista de sesiones, perfiles disponibles, conexión.
- `SessionViewModel` — modelo de una sesión individual con su colección de logs.
- `LogLineVm` — línea individual con discriminación de stream (`stdout`/`stderr`/`system`).

### Servicios

- `JaraviApiClient` — consume la API REST (`GET /api/sessions`, `POST /api/sessions`, etc.) con HttpClient.
- `EventStreamClient` — cliente WebSocket resiliente con **reconexión exponencial** (1s–30s). Escucha `/ws/events` y dispara eventos `EventReceived` y `ConnectionChanged`.

### UI

- Tema oscuro estilo Catppuccin Mocha (fondo `#1E1E2E`, texto `#CDD6F4`).
- Toolbar con selector de perfil, campo de tarea, workdir y botones Spawn/Kill/Refrescar.
- Panel izquierdo: lista de sesiones con indicador de estado por color.
- Panel derecho: consola de logs con colores por stream (stdout verde, stderr rojo, sistema azul).
- **Auto-scroll**: la ventana se desplaza al final cuando llegan nuevas líneas.
- Límite de 2000 líneas por sesión en memoria (las más antiguas se descartan).

## Uso

```bash
dotnet run --project Jaravi.Dashboard
```

La GUI se conecta a `http://localhost:5210` por defecto. Un indicador verde en la esquina superior derecha muestra el estado de conexión.

> [!note] Observador puro
> El Dashboard nunca envía comandos de control que no estén disponibles en la API REST pública. Es un ciudadano de primera clase del ecosistema, no un administrador privilegiado.

Véase también: [[Operacion]], [[Servidor MCP]]
