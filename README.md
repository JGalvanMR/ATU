📦 **Nombre del Módulo:** ATU — Sistema de Autorización Transaccional Única

## 🧭 Propósito

ATU es un sistema de autorización transaccional diseñado para validar, mediante un código OTP de un solo uso, que el despacho de un pallet/lote en una cámara fría o zona de almacenamiento sea autorizado únicamente por la coincidencia verificada entre un operador con dispositivo enrolado y un supervisor que escanea físicamente el BatchID correcto. Su objetivo de negocio es prevenir el fraude en la liberación de pallets, asegurando que solo se despache el lote que realmente fue autorizado, y no un lote distinto sustituido fraudulentamente.

## ⚙️ Responsabilidades

- Generar códigos OTP de 8 dígitos mediante HMAC-SHA256, derivados de un secreto de dispositivo, una ventana de tiempo, el BatchID del lote y el ID del supervisor.
- Validar la correspondencia entre el BatchID reclamado (asociado al OTP generado) y el BatchID real escaneado físicamente por el supervisor al momento del despacho.
- Verificar que el dispositivo que solicita el OTP esté enrolado (fingerprint de hardware + user agent + plataforma).
- Aplicar geofencing para exigir que operador y supervisor se encuentren dentro de una misma zona geográfica antes de autorizar el despacho.
- Registrar de forma inmutable y append-only cada evento de autorización, éxito o intento fraudulento (Audit Service).
- Difundir en tiempo real los eventos de auditoría y alertas de fraude a un dashboard gerencial vía SignalR.
- Enrolar y revocar dispositivos autorizados a generar OTPs.
- Servir una PWA para el operador (generación/visualización del OTP) y otra para el supervisor (escaneo de QR y validación).
- Aplicar controles de seguridad perimetrales: autenticación JWT, rate limiting y validación de solicitudes en el API Gateway.

## 🔄 Flujo de Funcionamiento

1. El operador escanea el QR del lote (BatchID del producto) desde la PWA de Operador, en un dispositivo previamente enrolado.
2. El sistema verifica el enrolamiento del dispositivo mediante fingerprint de hardware.
3. El sistema genera un OTP mediante HMAC-SHA256, combinando: secreto del dispositivo + ventana de tiempo + BatchID + ID del supervisor.
4. El operador recibe un OTP de 8 dígitos y lo comunica verbalmente al supervisor (o lo ingresa mediante un PIN pad físico).
5. El supervisor ingresa el OTP recibido y escanea el BatchID real del pallet físico frente a él.
6. El sistema compara el BatchID reclamado (usado para generar el OTP) contra el BatchID real escaneado. Si no coinciden, se marca como fraude.
7. El sistema recalcula el OTP esperado y lo compara contra el ingresado usando comparación en tiempo constante.
8. El sistema determina un resultado tipo semáforo (verde/amarillo/rojo) y lo devuelve al supervisor.
9. Si el resultado es verde, se autoriza el despacho.
10. El sistema emite un evento del resultado al dashboard gerencial en tiempo real.

## 📐 Reglas de Negocio

**🔒 Restricciones**
- El dispositivo del operador debe estar enrolado (fingerprint verificado) para poder generar un OTP.
- El secreto de cifrado de cada dispositivo se almacena cifrado con AES-256 y nunca se hardcodea en el código; se gestiona mediante Azure Key Vault / KMS.
- El OTP tiene una vigencia (TTL) de 90 segundos.
- El OTP es de uso único; una vez utilizado se marca `IsUsed=true` en base de datos y no puede reutilizarse (mitigación de replay attack).
- Existe un límite de 5 intentos por minuto por IP (rate limiting) para prevenir fuerza bruta.
- Geofencing: cuando `Geofence__RequireBothActors=true`, se exige que ambos actores (operador y supervisor) estén dentro de una distancia máxima configurable (`Geofence__MaxProximityMeters`, valor por defecto documentado de 50 metros).

**✅ Validaciones**
- El BatchID reclamado al generar el OTP debe coincidir con el BatchID real escaneado por el supervisor; de lo contrario el resultado es rojo (fraude).
- El OTP ingresado debe provenir de un dispositivo enrolado; un OTP de un dispositivo no enrolado se marca en rojo.
- La comparación del OTP calculado vs. el ingresado se realiza en tiempo constante (`CryptographicOperations.FixedTimeEquals()`) para evitar timing attacks.
- Los eventos de auditoría son inmutables y de solo anexado (append-only), lo cual es en sí una regla de integridad de datos.

**🔁 Agrupaciones**
- No determinable con la información disponible.

**⚙️ Reglas Operativas**
- Semáforo de resultados con tres estados posibles:
  - 🟢 Verde: OTP válido, BatchID correcto, dentro de la ventana de tiempo → autoriza el despacho.
  - 🟡 Amarillo: OTP de una ventana anterior (30–90s) pero con BatchID correcto → se solicita un nuevo OTP.
  - 🔴 Rojo: se activa ante cualquiera de estos casos → BatchID distinto al generado (fraude), OTP de dispositivo no enrolado, OTP ya utilizado (replay), u OTP expirado (>90 segundos). La acción varía entre bloquear/alertar/registrar según el caso, y en el caso de expiración simplemente se solicita un nuevo OTP.
- Todo evento (autorización exitosa o intento de fraude) se registra y se notifica al dashboard gerencial.

## 🔗 Dependencias

- **Servicios internos:** ATU.AuthService (generación/validación de OTP, geofencing), ATU.AuditService (hub de eventos vía SignalR, alertas de fraude), Servicio de Enrolamiento de Dispositivos, ATU.CamaraFria, ATU.PWA (operador y supervisor), ATU.Shared, ATU.Tests.
- **API Gateway:** Ocelot / YARP, con rate limiting, autenticación JWT y validación de solicitudes.
- **Autenticación/Identidad:** proveedor OIDC vía `Auth__Authority` (Microsoft Entra ID / Azure AD, según patrón de URL `login.microsoftonline.com`), con audiencia `Auth__Audience=api://atu-sistema`.
- **Base de datos:** SQL Server o PostgreSQL (no determinable cuál se usa en producción; el README menciona ambos como opciones), con tablas `enrolled_devices`, `otp_records`, `audit_events`, `zones`.
- **Gestión de secretos:** Azure Key Vault / KMS.
- **Comunicación en tiempo real:** SignalR (WSS) para el AuditService y el dashboard.
- **Frontend:** PWA con `service-worker.js` (caché + push notifications).
- **Criptografía:** HMAC-SHA256 para generación de OTP; `CryptographicOperations.FixedTimeEquals()` para comparación segura.
- **Geolocalización:** datos de zonas en formato GeoJSON (polígonos).

## ⚠️ Riesgos Técnicos

- **Dependencia de la comunicación verbal/manual del OTP:** el operador transmite el OTP al supervisor verbalmente o vía PIN pad físico, lo cual introduce un vector de error humano o interceptación no cubierto por controles automatizados según lo documentado.
- **Ambigüedad en el motor de base de datos:** el README indica "SQL Server / PostgreSQL" sin especificar cuál es el usado realmente, lo que puede generar inconsistencias de documentación técnica o de despliegue.
- **Dependencia crítica de un servicio externo de gestión de secretos** (Azure Key Vault/KMS): si este servicio no está disponible, el sistema completo de generación/validación de OTP podría verse comprometido o interrumpido (no determinable con la información disponible si existe manejo de contingencia).
- **Acoplamiento a un proveedor de identidad específico** (Microsoft Entra ID/Azure AD) mediante `Auth__Authority`, lo que podría dificultar portabilidad a otros proveedores OIDC.
- **Ventana de tolerancia de OTP (amarillo, 30–90s):** aceptar OTPs de la ventana anterior amplía la superficie temporal de validez, lo cual es una decisión de UX/seguridad que debe evaluarse en cuanto a su impacto en el riesgo de fraude.
- **No se documenta manejo de errores, reintentos, ni resiliencia** entre AuthService, AuditService y la base de datos.
- **No se documenta el mecanismo exacto de "PIN pad físico"** ni su integración con el sistema, lo que representa una dependencia de hardware externo no detallada.

## 🧪 Casos Edge

- OTP correcto pero perteneciente a una ventana de tiempo anterior (30–90 segundos): tratado como amarillo, se solicita nuevo OTP.
- OTP correcto, pero generado para un BatchID distinto al escaneado por el supervisor: tratado como fraude (rojo).
- OTP válido pero proveniente de un dispositivo no enrolado: bloqueado.
- Intento de reutilizar un OTP ya usado (replay attack): bloqueado.
- OTP expirado más allá de 90 segundos: inválido.
- Escenario de "coordinación a distancia" (operador y supervisor no están físicamente juntos): mitigado mediante geofencing, pero no se documenta qué ocurre si el geofencing está deshabilitado (`Geofence__RequireBothActors=false`).
- No determinable con la información disponible: comportamiento del sistema ante fallos de conectividad de la PWA durante el flujo de autorización (aunque existe caché de service worker, no se detalla su rol en este flujo transaccional).

## 🧱 Suposiciones Detectadas

- Se asume que el operador y el supervisor son actores humanos distintos y físicamente presentes en el momento del despacho.
- Se asume que el "PIN pad físico" o la comunicación verbal del OTP es un canal confiable y no interceptable dentro del contexto operativo.
- Se asume disponibilidad continua del proveedor de identidad (Azure AD/Entra ID) y del Key Vault para el funcionamiento del sistema.
- Se asume que las zonas geográficas (GeoJSON) están correctamente configuradas y mantenidas para que el geofencing funcione de forma confiable.
- Se asume que los dispositivos enrolados no serán clonados o comprometidos más allá de lo cubierto por el fingerprint de HW+UA+Platform.

## 📈 Recomendaciones Técnicas

- Aclarar y documentar de forma definitiva cuál motor de base de datos (SQL Server o PostgreSQL) se usa realmente en producción, para evitar inconsistencias en despliegue y mantenimiento.
- Documentar explícitamente el comportamiento del sistema ante fallas o indisponibilidad del Key Vault/KMS y del proveedor de identidad (estrategias de degradación controlada o failover).
- Evaluar y documentar formalmente el riesgo de la comunicación verbal/PIN pad del OTP entre operador y supervisor, considerando canales más auditable (p. ej., mostrar el OTP cifrado directamente en la pantalla del supervisor).
- Definir y documentar el comportamiento esperado cuando `Geofence__RequireBothActors=false`, para dejar claro si esto es una configuración de contingencia o un riesgo de seguridad no mitigado.
- Especificar políticas de resiliencia (reintentos, circuit breakers) entre AuthService, AuditService y la base de datos, dado que el sistema maneja eventos críticos de fraude.
- Incluir documentación de pruebas automatizadas más allá de los "9 tests de seguridad críticos" mencionados, detallando su cobertura específica.

## 🧾 Resumen Ejecutivo

ATU es un sistema que evita que se despache el pallet equivocado de la cámara fría. Funciona como una doble verificación: un operador genera un código temporal ligado exactamente al lote que se va a mover, y un supervisor debe confirmar ese código además de escanear físicamente el lote real antes de autorizar la salida. Si el lote escaneado no coincide con el que originó el código, el sistema lo marca como fraude y bloquea el despacho, dejando registro inalterable del intento. Además, exige que ambos empleados estén físicamente en el mismo lugar al momento de la operación, y limita cuánto tiempo es válido cada código para reducir el riesgo de que alguien lo reutilice o lo intercepte. Todo evento —exitoso o sospechoso— queda visible en tiempo real para el equipo gerencial.