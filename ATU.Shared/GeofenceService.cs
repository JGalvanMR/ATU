namespace ATU.AuthService;

/// <summary>
/// Servicio de Geofencing para validar que el operador y supervisor
/// estén físicamente en la zona de embarque autorizada.
/// 
/// Estrategia de implementación:
///   - Las zonas se definen como polígonos GeoJSON en base de datos
///   - Se valida que AMBOS actores estén dentro de la zona
///   - Se valida que la distancia entre ellos no exceda un umbral (ej: 50m)
///     para evitar que uno esté en cámara y el otro afuera
/// </summary>
public class GeofenceService(IZoneRepository zoneRepo) : IGeofenceService
{
    // Distancia máxima entre operador y supervisor (metros)
    private const double MaxProximityMeters = 50.0;

    public async Task<GeofenceResult> ValidateProximityAsync(
        string zoneId,
        double operatorLat, double operatorLon,
        double? supervisorLat, double? supervisorLon)
    {
        var zone = await zoneRepo.GetByIdAsync(zoneId);
        if (zone is null)
            return new GeofenceResult(false, 0, "Zona no configurada.");

        // Validar que el operador esté dentro de la zona definida
        bool operatorInZone = IsPointInPolygon(operatorLat, operatorLon, zone.BoundaryPoints);
        if (!operatorInZone)
        {
            var distanceToZone = CalculateDistanceToNearestBoundary(
                operatorLat, operatorLon, zone.BoundaryPoints);
            return new GeofenceResult(false, distanceToZone,
                $"Operador fuera de zona '{zone.Name}'. Distancia al perímetro: {distanceToZone:F0}m");
        }

        // Si el supervisor también comparte ubicación, validar proximidad mutua
        if (supervisorLat.HasValue && supervisorLon.HasValue)
        {
            double distance = HaversineDistance(
                operatorLat, operatorLon,
                supervisorLat.Value, supervisorLon.Value);

            bool supervisorInZone = IsPointInPolygon(
                supervisorLat.Value, supervisorLon.Value, zone.BoundaryPoints);

            if (!supervisorInZone || distance > MaxProximityMeters)
            {
                return new GeofenceResult(false, distance,
                    $"Supervisor no está en la zona de embarque ({distance:F0}m de distancia).");
            }
        }

        return new GeofenceResult(true, 0, "Ambos actores dentro de la zona autorizada.");
    }

    /// <summary>
    /// Algoritmo Ray Casting para determinar si un punto está dentro de un polígono.
    /// </summary>
    private static bool IsPointInPolygon(double lat, double lon, GeoPoint[] polygon)
    {
        int n = polygon.Length;
        bool inside = false;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var vi = polygon[i];
            var vj = polygon[j];

            if ((vi.Latitude > lat) != (vj.Latitude > lat) &&
                lon < (vj.Longitude - vi.Longitude) * (lat - vi.Latitude)
                    / (vj.Latitude - vi.Latitude) + vi.Longitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Fórmula de Haversine para distancia entre dos coordenadas GPS (metros).
    /// </summary>
    private static double HaversineDistance(
        double lat1, double lon1,
        double lat2, double lon2)
    {
        const double R = 6371000; // Radio de la Tierra en metros
        double dLat = ToRad(lat2 - lat1);
        double dLon = ToRad(lon2 - lon1);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double CalculateDistanceToNearestBoundary(
        double lat, double lon, GeoPoint[] boundary)
    {
        return boundary
            .Select(p => HaversineDistance(lat, lon, p.Latitude, p.Longitude))
            .Min();
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;
}

// ── Modelos ───────────────────────────────────────────────────────────────────

public record GeoPoint(double Latitude, double Longitude);

public class LoadingZone
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required GeoPoint[] BoundaryPoints { get; init; }
    // Ejemplo: Zona de cámara frigorífica #3
    // BoundaryPoints definen el polígono del área de embarque
}

public record GeofenceResult(bool IsWithinZone, double DistanceMeters, string Message);

public interface IGeofenceService
{
    Task<GeofenceResult> ValidateProximityAsync(
        string zoneId,
        double operatorLat, double operatorLon,
        double? supervisorLat, double? supervisorLon);
}

public interface IZoneRepository
{
    Task<LoadingZone?> GetByIdAsync(string zoneId);
}
