using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using ATU.Shared;
using ATU.Shared.Models;
using System.Net.Http.Json;
using Android.Locations;
using Android.Content.PM;

namespace ATU.CamarasFrias;

[Activity(Label = "ATU - Cámaras Frías", MainLauncher = true,
    ScreenOrientation = ScreenOrientation.Portrait)]
public class MainActivity : Activity, ILocationListener
{
    private EditText? txtSupervisorId;
    private EditText? txtProducto;
    private EditText? txtRecibo;
    private EditText? txtTarima;
    private EditText? txtFechaCad;
    private Button? btnGenerarOTP;
    private TextView? txtStatus;
    private TextView? txtGeofence;

    private HttpClient _httpClient = new();
    private OfflineQueue<PendingOTPValidation> _offlineQueue;
    private LocationManager? _locationManager;
    private double? _currentLat;
    private double? _currentLon;

    // Secret único por dispositivo (generado al instalar/primer uso)
    private string _deviceSecret = "";
    private string _deviceId = "";

    // Zona de geofencing asignada a este dispositivo
    private string _assignedZoneId = "CAMARA-FRIA-PRINCIPAL";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        _offlineQueue = new OfflineQueue<PendingOTPValidation>("ATU.CamarasFrias", "pending_validations");
        _deviceId = GetDeviceId();
        LoadDeviceSecret();

        InitializeViews();
        InitializeLocation();
    }

    private void InitializeViews()
    {
        txtSupervisorId = FindViewById<EditText>(Resource.Id.txtResponsable);
        txtProducto = FindViewById<EditText>(Resource.Id.txtProducto);
        txtRecibo = FindViewById<EditText>(Resource.Id.txtRecibo);
        txtTarima = FindViewById<EditText>(Resource.Id.txtTarima);
        txtFechaCad = FindViewById<EditText>(Resource.Id.txtFechaCaducidad);
        btnGenerarOTP = FindViewById<Button>(Resource.Id.btnGenerarOTP);
        txtStatus = FindViewById<TextView>(Resource.Id.txtStatus);
        //txtGeofence = FindViewById<TextView>(Resource.Id.txtGeofence);

        btnGenerarOTP!.Click += async (s, e) => await GenerarOTP();

        // Cargar ID de supervisor guardado
        var prefs = GetSharedPreferences("ATU", FileCreationMode.Private);
        var savedSupervisor = prefs.GetString("SupervisorId", "");
        if (!string.IsNullOrEmpty(savedSupervisor))
            txtSupervisorId!.Text = savedSupervisor;
    }

    private void InitializeLocation()
    {
        _locationManager = GetSystemService(LocationService) as LocationManager;
        try
        {
            _locationManager?.RequestLocationUpdates(LocationManager.GpsProvider, 5000, 10, this);
            _locationManager?.RequestLocationUpdates(LocationManager.NetworkProvider, 5000, 10, this);
        }
        catch { /* Permisos no concedidos */ }
    }

    private async Task GenerarOTP()
    {
        var supervisorId = txtSupervisorId?.Text?.Trim() ?? "";
        var producto = txtProducto?.Text?.Trim() ?? "";
        var recibo = txtRecibo?.Text?.Trim() ?? "";
        var tarima = txtTarima?.Text?.Trim() ?? "";
        var fechaCad = txtFechaCad?.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(supervisorId) || string.IsNullOrEmpty(producto))
        {
            Toast.MakeText(this, "Complete supervisor y producto", ToastLength.Short).Show();
            return;
        }

        // Guardar supervisor para próxima vez
        var prefs = GetSharedPreferences("ATU", FileCreationMode.Private);
        prefs.Edit().PutString("SupervisorId", supervisorId).Commit();

        // 1. Validar geofencing localmente (rápido)
        var geofenceOk = await ValidarGeofenceLocal();
        if (!geofenceOk)
        {
            Toast.MakeText(this, "⚠️ Fuera de zona de cámaras frías autorizada", ToastLength.Long).Show();
            // Continuar igual (el servidor validará de nuevo)
        }

        // 2. Generar OTP criptográficamente (siempre funciona)
        var otp = ATUCore.GenerateOTP(_deviceSecret, producto, recibo, tarima, fechaCad, supervisorId);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(30);

        // 3. Intentar enviar al servidor (auditoría en tiempo real)
        var request = new GenerateOTPRequest(
            _deviceId, supervisorId, producto, recibo, tarima, fechaCad,
            _currentLat, _currentLon);

        bool online = await IntentarEnviarAlServidor(request);

        txtStatus!.Text = online
            ? "✅ En línea - OTP sincronizado"
            : $"⚠️ Offline - {_offlineQueue.Count} pendientes";

        // 4. Mostrar OTP en pantalla grande
        MostrarOTPEnPantalla(otp, expiresAt, producto, recibo, tarima, fechaCad, supervisorId);
    }

    private async Task<bool> ValidarGeofenceLocal()
    {
        // Validación simple: ¿tenemos coordenadas y estamos en rango aproximado?
        if (!_currentLat.HasValue || !_currentLon.HasValue) return true; // No podemos validar

        // Coordenadas de tu planta (ajustar)
        const double plantaLat = 20.6723;
        const double plantaLon = -103.3472;
        const double maxDistanceMeters = 500; // 500m de la planta

        var distance = CalcularDistancia(_currentLat.Value, _currentLon.Value, plantaLat, plantaLon);

        RunOnUiThread(() =>
        {
            txtGeofence!.Text = $"📍 {distance:F0}m de planta";
            txtGeofence.SetTextColor(distance <= maxDistanceMeters ? Android.Graphics.Color.Green : Android.Graphics.Color.Red);
        });

        return distance <= maxDistanceMeters;
    }

    private double CalcularDistancia(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Radio tierra en metros
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private async Task<bool> IntentarEnviarAlServidor(GenerateOTPRequest request)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await _httpClient.PostAsJsonAsync(
                "http://192.168.1.100:5001/api/otp/generate", request, cts.Token);

            if (response.IsSuccessStatusCode) return true;
        }
        catch { /* Sin conexión */ }

        // Encolar para sincronizar después
        var pending = new PendingOTPValidation(
            "", request.ProductoClave, request.Recibo, request.Tarima,
            request.FechaCaducidad, _deviceId, request.SupervisorId,
            DateTimeOffset.UtcNow, DateTimeOffset.MinValue, false, _assignedZoneId);

        _offlineQueue.Enqueue(pending);
        return false;
    }

    private void MostrarOTPEnPantalla(string otp, DateTimeOffset expiresAt,
        string producto, string recibo, string tarima, string fechaCad, string supervisorId)
    {
        var intent = new Intent(this, typeof(OTPDisplayActivity));
        intent.PutExtra("OTP", otp);
        intent.PutExtra("ExpiresAt", expiresAt.ToString("O"));
        intent.PutExtra("Producto", producto);
        intent.PutExtra("Recibo", recibo);
        intent.PutExtra("Tarima", tarima);
        intent.PutExtra("FechaCad", fechaCad);
        intent.PutExtra("SupervisorId", supervisorId);
        StartActivity(intent);
    }

    // ILocationListener implementation
    public void OnLocationChanged(Location location)
    {
        _currentLat = location.Latitude;
        _currentLon = location.Longitude;
    }
    public void OnProviderDisabled(string provider) { }
    public void OnProviderEnabled(string provider) { }
    public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }

    private string GetDeviceId()
    {
        var androidId = Android.Provider.Settings.Secure.GetString(ContentResolver!,
            Android.Provider.Settings.Secure.AndroidId);
        return androidId ?? "unknown";
    }

    private void LoadDeviceSecret()
    {
        var prefs = GetSharedPreferences("ATU.Secure", FileCreationMode.Private);
        _deviceSecret = prefs.GetString("DeviceSecret", "");

        if (string.IsNullOrEmpty(_deviceSecret))
        {
            // Generar nuevo secret criptográficamente seguro
            var bytes = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            _deviceSecret = Convert.ToBase64String(bytes);

            prefs.Edit().PutString("DeviceSecret", _deviceSecret).Commit();
        }
    }
}