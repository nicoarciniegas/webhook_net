# Webhook .NET

Webhook server en C# con ASP.NET Core para recibir mensajes de chat.

## Requisitos

- .NET 8.0 SDK o superior

## Configuración

1. Configura las variables de entorno:
   - `PORT`: Puerto donde correrá el servidor (por defecto: 3000)
   - `VERIFY_TOKEN`: Token de verificación para el webhook

### Windows (PowerShell)
```powershell
$env:PORT="3000"
$env:VERIFY_TOKEN="tu_token_aqui"
```

### Linux/Mac
```bash
export PORT=3000
export VERIFY_TOKEN=tu_token_aqui
```

## Ejecutar

```bash
dotnet run
```

O para modo desarrollo con hot reload:

```bash
dotnet watch run
```

## Endpoints

### GET /
Verifica el webhook. Espera los parámetros:
- `hub.mode=subscribe`
- `hub.challenge=<valor>`
- `hub.verify_token=<tu_token>`

Retorna el challenge si el token coincide.

### POST /
Recibe los mensajes del webhook y los imprime en consola con formato JSON.

## Desplegar en Render

Ver la guía completa en [DEPLOY.md](DEPLOY.md)

Pasos rápidos:
1. Sube el código a GitHub/GitLab
2. Conecta el repositorio en Render
3. Configura la variable `VERIFY_TOKEN`
4. Render detectará el `render.yaml` y desplegará automáticamente

## Publicar localmente

Para crear un ejecutable:

```bash
dotnet publish -c Release -o ./publish
```

El ejecutable estará en la carpeta `publish/`.
