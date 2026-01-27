# Guía de Despliegue en Render

## Pasos para desplegar

### 1. Preparar el repositorio Git

```bash
git init
git add .
git commit -m "Initial commit"
```

Sube tu código a GitHub, GitLab o Bitbucket.

### 2. Crear servicio en Render

1. Ve a [https://dashboard.render.com](https://dashboard.render.com)
2. Click en "New +" y selecciona "Web Service"
3. Conecta tu repositorio
4. Render detectará automáticamente el `render.yaml`

### 3. Configurar variables de entorno

En el dashboard de Render, configura:

- **VERIFY_TOKEN**: Tu token de verificación del webhook

Render automáticamente proporciona:
- **PORT**: Puerto donde debe escuchar tu aplicación (lo gestiona Render)

### 4. Desplegar

- Render construirá y desplegará automáticamente usando el Dockerfile
- El proceso toma unos 5-10 minutos la primera vez
- Obtendrás una URL como: `https://webhook-net-xxxx.onrender.com`

## Despliegue manual (sin render.yaml)

Si prefieres configurar manualmente:

1. **New Web Service** en Render
2. **Build Command**: `dotnet publish -c Release -o out`
3. **Start Command**: `dotnet out/webhook_net.dll`
4. **Environment**: Docker (recomendado) o Native
5. Agrega las variables de entorno necesarias

## Verificar el webhook

Cuando Meta/Facebook verifique tu webhook, enviará una petición GET:

```
GET https://tu-app.onrender.com/?hub.mode=subscribe&hub.verify_token=TU_TOKEN&hub.challenge=1234567890
```

Si todo está bien configurado, retornará el challenge y verás "WEBHOOK VERIFIED" en los logs.

## Troubleshooting

### El servicio no inicia
- Revisa los logs en el dashboard de Render
- Verifica que VERIFY_TOKEN esté configurado
- Asegúrate que el puerto se lea de la variable de entorno PORT

### Timeout en el health check
- Render hace health checks a tu servicio
- Asegúrate que la app esté escuchando en 0.0.0.0 (ya configurado)

### Plan gratuito
- El plan gratuito puede "dormir" después de 15 minutos de inactividad
- La primera petición después de dormir puede tardar ~30 segundos
- Para producción, considera el plan pago que mantiene el servicio activo 24/7

## Logs

Para ver los logs en tiempo real:
1. Ve a tu servicio en el dashboard de Render
2. Click en la pestaña "Logs"
3. Verás los mensajes de consola incluyendo los webhooks recibidos
