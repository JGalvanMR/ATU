using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using System.Timers;

namespace ATU.CamarasFrias;

[Activity(Label = "CÓDIGO OTP", NoHistory = true,
    ScreenOrientation = ScreenOrientation.Portrait)]
public class OTPDisplayActivity : Activity
{
    private TextView? txtOTP;
    private TextView? txtTimer;
    private TextView? txtProducto;
    private TextView? txtDetalle;
    private Button? btnCerrar;
    private Button? btnNuevo;

    private System.Timers.Timer? _timer;
    private DateTimeOffset _expiresAt;
    private bool _expirado = false;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Mantener pantalla encendida
        Window!.AddFlags(WindowManagerFlags.KeepScreenOn);
        // Prevenir screenshots (opcional, seguridad)
        Window.SetFlags(WindowManagerFlags.Secure, WindowManagerFlags.Secure);

        SetContentView(Resource.Layout.activity_otp_display);

        var otp = Intent?.GetStringExtra("OTP") ?? "ERROR";
        _expiresAt = DateTimeOffset.Parse(Intent?.GetStringExtra("ExpiresAt") ?? DateTimeOffset.MinValue.ToString("O"));
        var producto = Intent?.GetStringExtra("Producto") ?? "";
        var recibo = Intent?.GetStringExtra("Recibo") ?? "";
        var tarima = Intent?.GetStringExtra("Tarima") ?? "";
        var fechaCad = Intent?.GetStringExtra("FechaCad") ?? "";
        var supervisor = Intent?.GetStringExtra("SupervisorId") ?? "";

        txtOTP = FindViewById<TextView>(Resource.Id.txtOTP);
        txtTimer = FindViewById<TextView>(Resource.Id.txtTimer);
        txtProducto = FindViewById<TextView>(Resource.Id.txtProducto);
        txtDetalle = FindViewById<TextView>(Resource.Id.txtDetalle);
        btnCerrar = FindViewById<Button>(Resource.Id.btnCerrar);
        btnNuevo = FindViewById<Button>(Resource.Id.btnGenerarNuevo);

        // OTP enorme
        txtOTP!.Text = otp;
        txtOTP.SetTextSize(Android.Util.ComplexUnitType.Sp, 80);
        txtOTP.SetTextColor(Color.ParseColor("#00D4FF"));
        txtOTP.SetTypeface(null, TypefaceStyle.Bold);
        txtOTP.Gravity = GravityFlags.Center;

        txtProducto!.Text = producto;
        txtProducto.SetTextSize(Android.Util.ComplexUnitType.Sp, 24);
        txtProducto.SetTextColor(Color.White);

        txtDetalle!.Text = $"Recibo: {recibo}  |  Tarima: {tarima}\nCaduca: {fechaCad}\nSupervisor: {supervisor}";
        txtDetalle.SetTextColor(Color.ParseColor("#5A7FA8"));

        btnCerrar!.Click += (s, e) => Finish();
        btnNuevo!.Click += (s, e) =>
        {
            Finish(); // Volver a MainActivity
        };

        IniciarCountdown();
    }

    private void IniciarCountdown()
    {
        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += (s, e) =>
        {
            var segundos = (int)(_expiresAt - DateTimeOffset.UtcNow).TotalSeconds;

            RunOnUiThread(() =>
            {
                if (segundos <= 0 && !_expirado)
                {
                    _expirado = true;
                    txtOTP!.Text = "EXPIRADO";
                    txtOTP.SetTextColor(Color.Gray);
                    txtTimer!.Text = "Solicite nuevo código al supervisor";
                    txtTimer.SetTextColor(Color.Red);
                    _timer?.Stop();

                    // Vibrar
                    var vib = GetSystemService(VibratorService) as Vibrator;
                    vib?.Vibrate(new long[] { 0, 500, 200, 500 }, -1);
                }
                else if (!_expirado)
                {
                    txtTimer!.Text = $"⏱️ Válido: {segundos}s";

                    if (segundos <= 10)
                    {
                        txtTimer.SetTextColor(Color.Red);
                        txtOTP.SetTextColor(Color.Red);
                    }
                    else if (segundos <= 20)
                    {
                        txtTimer.SetTextColor(Color.ParseColor("#FFB800")); // Amarillo
                    }
                    else
                    {
                        txtTimer.SetTextColor(Color.ParseColor("#00FF88")); // Verde
                    }
                }
            });
        };
        _timer.Start();
    }

    protected override void OnDestroy()
    {
        _timer?.Stop();
        _timer?.Dispose();
        base.OnDestroy();
    }
}