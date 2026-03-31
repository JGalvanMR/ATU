using System;
using System.Collections.Generic;

namespace ATU.CamaraFria.Models;

public class LoginRequest
{
    public string EmployeeNumber { get; set; } = string.Empty;
    public string DeviceFingerprint { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
}

public class LoginResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public AuthData? Data { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class AuthData
{
    public string Token { get; set; } = string.Empty;
    public string SupervisorId { get; set; } = string.Empty;
    public string SupervisorName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool RequiresBiometricEnrollment { get; set; }
    public bool IsDeviceEnrolled { get; set; }
}