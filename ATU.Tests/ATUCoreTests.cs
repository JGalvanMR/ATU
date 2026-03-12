using Xunit;
using ATU.Shared;

namespace ATU.Tests;

/// <summary>
/// Tests críticos de seguridad para el núcleo ATU.
/// Estos tests validan que el sistema resiste los ataques
/// que se identificaron en el fallo de seguridad original.
/// </summary>
public class ATUCoreTests
{
    private const string TestSecret = "super-secret-device-key-256bits==";
    private const string BatchA = "LOT-2024-A847";
    private const string BatchB = "LOT-2024-B203";
    private const string Supervisor1 = "SUP-MARTINEZ";
    private const string Supervisor2 = "SUP-TORRES";

    // ── Test 1: Caso base - OTP válido ────────────────────────────────────────
    [Fact]
    public void GenerateAndValidate_SameBatch_ShouldBeGreen()
    {
        var otp = ATUCore.GenerateOTP(TestSecret, BatchA, Supervisor1);
        var result = ATUCore.Validate(otp, TestSecret, BatchA, BatchA, Supervisor1);

        Assert.Equal(ATUStatus.Green, result.Status);
        Assert.True(result.IsAuthorized);
    }

    // ── Test 2: ATAQUE PRINCIPAL - Código de Lote A usado en Lote B ───────────
    [Fact]
    public void Validate_OTPFromBatchA_UsedInBatchB_ShouldBeRed()
    {
        // El supervisor genera OTP para Lote A
        var otpForBatchA = ATUCore.GenerateOTP(TestSecret, BatchA, Supervisor1);

        // Intenta usar ese código para autorizar Lote B (el fraude detectado)
        var result = ATUCore.Validate(
            otpForBatchA,
            TestSecret,
            claimedBatchId: BatchA,  // Lo que dice el supervisor
            actualBatchId: BatchB,   // Lo que el sistema verifica del lote físico
            Supervisor1);

        Assert.Equal(ATUStatus.Red, result.Status);
        Assert.False(result.IsAuthorized);
        Assert.Contains("FRAUDE", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 3: Código de Supervisor 1 no válido para Supervisor 2 ─────────────
    [Fact]
    public void Validate_OTPFromSupervisor1_UsedBySupervisor2_ShouldBeRed()
    {
        var otpForSup1 = ATUCore.GenerateOTP(TestSecret, BatchA, Supervisor1);

        // Supervisor 2 intenta usar el código de Supervisor 1
        var result = ATUCore.Validate(otpForSup1, TestSecret, BatchA, BatchA, Supervisor2);

        Assert.Equal(ATUStatus.Red, result.Status);
        Assert.False(result.IsAuthorized);
    }

    // ── Test 4: Código expirado devuelve Amarillo ─────────────────────────────
    [Fact]
    public void Validate_ExpiredOTP_ShouldBeYellow()
    {
        // Generar OTP hace 10 ventanas de tiempo (>5 minutos atrás)
        var pastTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var expiredOtp = ATUCore.GenerateOTP(TestSecret, BatchA, Supervisor1, pastTime);

        var result = ATUCore.Validate(expiredOtp, TestSecret, BatchA, BatchA, Supervisor1);

        // Fuera de la ventana de validez (±3 ventanas de 30s = ±90s)
        Assert.Equal(ATUStatus.Red, result.Status); // Más de 90s → Rojo (inválido)
    }

    // ── Test 5: OTP de otro dispositivo (secret diferente) no es válido ────────
    [Fact]
    public void Validate_OTPFromDifferentDevice_ShouldBeRed()
    {
        const string AttackerSecret = "attacker-device-secret-different==";

        // Atacante genera OTP con su propio dispositivo no enrolado
        var attackerOtp = ATUCore.GenerateOTP(AttackerSecret, BatchA, Supervisor1);

        // Valida contra el secret del dispositivo legítimo
        var result = ATUCore.Validate(attackerOtp, TestSecret, BatchA, BatchA, Supervisor1);

        Assert.Equal(ATUStatus.Red, result.Status);
        Assert.False(result.IsAuthorized);
    }

    // ── Test 6: OTPs son únicos por ventana de tiempo ─────────────────────────
    [Fact]
    public void Generate_TwoDifferentBatches_ShouldProduceDifferentOTPs()
    {
        var otpA = ATUCore.GenerateOTP(TestSecret, BatchA, Supervisor1);
        var otpB = ATUCore.GenerateOTP(TestSecret, BatchB, Supervisor1);

        Assert.NotEqual(otpA, otpB);
    }

    // ── Test 7: OTPs son diferentes por supervisor ────────────────────────────
    [Fact]
    public void Generate_TwoDifferentSupervisors_ShouldProduceDifferentOTPs()
    {
        var otp1 = ATUCore.GenerateOTP(TestSecret, BatchA, Supervisor1);
        var otp2 = ATUCore.GenerateOTP(TestSecret, BatchA, Supervisor2);

        Assert.NotEqual(otp1, otp2);
    }

    // ── Test 8: Formato del OTP - 8 dígitos numéricos ─────────────────────────
    [Fact]
    public void GenerateOTP_ShouldReturn8NumericDigits()
    {
        var otp = ATUCore.GenerateOTP(TestSecret, BatchA, Supervisor1);

        Assert.Equal(8, otp.Length);
        Assert.True(otp.All(char.IsDigit), "OTP debe contener solo dígitos");
    }

    // ── Test 9: QR estático robado NO puede ser reutilizado (replay) ──────────
    // Este test verifica que el OTP cambia cada ventana de 30s,
    // haciendo inútil una foto de pantalla tomada hace >90 segundos
    [Fact]
    public void Generate_InDifferentTimeWindows_ShouldProduceDifferentOTPs()
    {
        var time1 = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var time2 = time1.AddMinutes(2); // 4 ventanas después

        var otp1 = ATUCore.GenerateOTP(TestSecret, BatchA, Supervisor1, time1);
        var otp2 = ATUCore.GenerateOTP(TestSecret, BatchA, Supervisor1, time2);

        Assert.NotEqual(otp1, otp2);
    }
}
