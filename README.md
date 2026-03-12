# 🔐 ATU — Sistema de Autorización Transaccional Única

## Arquitectura de Microservicios

```
┌─────────────────────────────────────────────────────────────────────┐
│                    ZONA DESMILITARIZADA (DMZ)                       │
│                                                                     │
│  📱 PWA Operador          📱 PWA Supervisor        💻 Dashboard     │
│  (Dispositivo Enrolado)   (Scanner de QR)          (Gerente)        │
└──────────────┬──────────────────────┬──────────────────┬───────────┘
               │ HTTPS/TLS 1.3        │                  │ SignalR WSS
               ▼                      ▼                  ▼
┌──────────────────────────────────────────────────────────────────┐
│                    API GATEWAY (Ocelot / YARP)                   │
│          Rate Limiting · JWT Auth · Request Validation           │
└──────┬────────────────────┬─────────────────────────────────────┘
       │                    │
       ▼                    ▼
┌─────────────┐    ┌────────────────┐    ┌──────────────────────┐
│ AUTH        │    │ AUDIT          │    │ DEVICE ENROLLMENT    │
│ SERVICE     │    │ SERVICE        │    │ SERVICE              │
│             │    │                │    │                      │
│ • GenerateOTP    │ • SignalR Hub  │    │ • Enrolar Dispositivo│
│ • ValidateOTP    │ • AuditFeed    │    │ • Revocar Dispositivo│
│ • Geofencing     │ • FraudAlerts  │    │ • Fingerprint Check  │
└──────┬──────┘    └───────┬────────┘    └──────────────────────┘
       │                   │
       ▼                   ▼
┌─────────────────────────────────────────────────────────────────┐
│                    DATOS (SQL Server / PostgreSQL)               │
│                                                                 │
│  enrolled_devices  │  otp_records  │  audit_events │  zones    │
│  (Secret cifrado   │  (TTL 90s,    │  (Inmutables, │  (GeoJSON │
│   en AES-256)      │   uso único)  │   append-only)│  polígonos│
└─────────────────────────────────────────────────────────────────┘
       │
       ▼
┌──────────────┐
│ Azure Key    │  ← Secrets de cifrado NUNCA en código
│ Vault / KMS  │
└──────────────┘
```

## Flujo de Autorización (Happy Path)

```
OPERADOR                    SISTEMA ATU                 SUPERVISOR
    │                            │                           │
    │ 1. Escanea QR de Lote      │                           │
    │    (BatchID del producto)  │                           │
    │──────────────────────────►│                           │
    │                            │ Verifica dispositivo      │
    │                            │ enrolado (fingerprint)    │
    │                            │                           │
    │                            │ Genera HMAC-SHA256(       │
    │                            │   Secret + TimeWindow +   │
    │                            │   BatchID + SupervisorID) │
    │                            │                           │
    │ 2. Recibe OTP de 8 dígitos │                           │
    │◄──────────────────────────│                           │
    │                            │                           │
    │ 3. Muestra OTP al          │                           │
    │    supervisor verbalmente  │                           │
    │    (o via PIN pad físico)  │                           │
    │                            │                           │
    │                            │    4. Supervisor ingresa  │
    │                            │       OTP + escanea el    │
    │                            │◄──────BatchID del pallet  │
    │                            │                           │
    │                            │ Valida: ¿ClaimedBatch     │
    │                            │         == ActualBatch?   │
    │                            │ Si NO → 🔴 FRAUDE         │
    │                            │                           │
    │                            │ Computa OTP esperado y    │
    │                            │ compara en tiempo const.  │
    │                            │                           │
    │                            │ 5. Resultado semáforo     │
    │                            │──────────────────────────►│
    │                            │                           │ 🟢 Verde
    │                            │                           │ = Despachar
    │                            │                           │
    │                            │ 6. Evento al dashboard    │
    │                            │──────────────► GERENTE   │
```

## Tabla de Respuestas del Semáforo

| Estado  | Condición | Acción |
|---------|-----------|--------|
| 🟢 Verde | OTP válido, BatchID correcto, dentro de ventana | Autorizar despacho |
| 🟡 Amarillo | OTP de ventana anterior (30-90s), BatchID correcto | Solicitar nuevo OTP |
| 🔴 Rojo | BatchID diferente al generado (FRAUDE) | Bloquear, alertar, registrar |
| 🔴 Rojo | OTP de otro dispositivo no enrolado | Bloquear, alertar |
| 🔴 Rojo | OTP ya utilizado (replay attack) | Bloquear, alertar |
| 🔴 Rojo | OTP expirado >90 segundos | Inválido, solicitar nuevo |

## Estructura de Archivos

```
ATU/
├── Shared/
│   ├── ATUCore.cs              ← 🔐 Lógica de hashing HMAC-SHA256
│   └── DeviceEnrollment.cs     ← 📱 Enrolamiento de dispositivos
├── AuthService/
│   ├── OTPController.cs        ← 🌐 API REST (Generate + Validate)
│   ├── GeofenceService.cs      ← 📍 Validación geoespacial
│   └── Program.cs              ← ⚙️ Configuración + DI
├── AuditService/
│   └── AuditHub.cs             ← 📡 SignalR + Publisher de eventos
├── PWA/
│   └── service-worker.js       ← 🔧 Caché + Push Notifications
└── ATUCoreTests.cs             ← ✅ 9 tests de seguridad críticos
```

## Variables de Entorno Requeridas

```bash
# Auth
Auth__Authority=https://login.microsoftonline.com/{tenant}/v2.0
Auth__Audience=api://atu-sistema

# Base de datos
ConnectionStrings__DefaultConnection=Server=...;Database=ATU;...

# Key Vault (NUNCA hardcodear secrets en código)
KeyVault__Uri=https://atu-kv.vault.azure.net/

# Orígenes permitidos para CORS
AllowedOrigins__0=https://atu.empresa.com

# Geofencing
Geofence__MaxProximityMeters=50
Geofence__RequireBothActors=true
```

## Seguridad: Vectores de Ataque Mitigados

| Ataque | Mitigación |
|--------|-----------|
| Foto de QR estático | OTP dinámico (ventana de 30s), QR ya no existe |
| Código de Lote A en Lote B | BatchID incluido en HMAC; mismatch = 🔴 FRAUDE |
| Dispositivo no enrolado | Fingerprint de HW+UA+Platform verificado |
| Replay attack (reutilizar código) | OTPRecord marcado `IsUsed=true` en DB |
| Timing attack | `CryptographicOperations.FixedTimeEquals()` |
| Brute force | Rate limiter: 5 intentos/min por IP |
| Escalación de privilegios | JWT con roles, políticas de autorización |
| Compromiso de secrets en DB | AES-256 + Azure Key Vault |
| Coordinación a distancia | Geofencing: ambos actores en misma zona |
