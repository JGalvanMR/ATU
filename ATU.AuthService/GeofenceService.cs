using ATU.Shared.Models;

namespace ATU.AuthService;

public sealed class GeofenceService : IGeofenceService
{
    private readonly IZoneRepository _zoneRepo;
    private const double MaxProximityMeters = 50.0; // Distancia máxima entre operador y supervisor

    public GeofenceService(IZoneRepository zoneRepo)
    {
        _zoneRepo = zoneRepo ?? throw new ArgumentNullException(nameof(zoneRepo));
    }

    /// <summary>
    /// Valida que una coordenada esté dentro de una zona geográfica definida
    /// </summary>
    public async Task<GeofenceResult> ValidateAsync(string zoneId, double latitude, double longitude)
    {
        if (string.IsNullOrEmpty(zoneId))
        {
            return new GeofenceResult(false, double.MaxValue, "Zona no especificada.");
        }

        var zone = await _zoneRepo.GetByIdAsync(zoneId);
        if (zone is null)
        {
            return new GeofenceResult(false, double.MaxValue, $"Zona '{zoneId}' no configurada en el sistema.");
        }

        if (zone.BoundaryPoints == null || zone.BoundaryPoints.Length < 3)
        {
            return new GeofenceResult(false, 0, $"Zona '{zone.Name}' no tiene perímetro válido definido.");
        }

        // Validar que el punto esté dentro del polígono
        bool isInside = IsPointInPolygon(latitude, longitude, zone.BoundaryPoints);

        if (!isInside)
        {
            var distanceToZone = CalculateDistanceToNearestBoundary(latitude, longitude, zone.BoundaryPoints);
            return new GeofenceResult(
                false,
                distanceToZone,
                $"Ubicación fuera de '{zone.Name}'. Distancia al perímetro: {distanceToZone:F0} metros.");
        }

        return new GeofenceResult(
            true,
            0,
            $"Ubicación válida dentro de '{zone.Name}'.");
    }

    /// <summary>
    /// Valida proximidad entre dos actores (operador y supervisor)
    /// </summary>
    public GeofenceResult ValidateProximity(Coordinate operatorCoord, Coordinate supervisorCoord, double maxDistanceMeters = 50)
    {
        var distance = HaversineDistance(operatorCoord, supervisorCoord);

        return distance <= maxDistanceMeters
            ? new GeofenceResult(true, distance, $"Operador y supervisor dentro de rango ({distance:F1}m).")
            : new GeofenceResult(false, distance, $"Distancia excesiva ({distance:F1}m). Máximo permitido: {maxDistanceMeters}m.");
    }

    /// <summary>
    /// Algoritmo Ray Casting para determinar si un punto está dentro de un polígono
    /// </summary>
    private static bool IsPointInPolygon(double lat, double lon, GeoPoint[] polygon)
    {
        if (polygon.Length < 3) return false;

        bool inside = false;
        int j = polygon.Length - 1;

        for (int i = 0; i < polygon.Length; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];

            // Verificar si el punto está entre las latitudes del segmento
            if (((pi.Latitude > lat) != (pj.Latitude > lat)) &&
                // Verificar si está a la izquierda del segmento (intersección con rayo horizontal)
                (lon < (pj.Longitude - pi.Longitude) * (lat - pi.Latitude) / (pj.Latitude - pi.Latitude) + pi.Longitude))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Calcula distancia al punto del perímetro más cercano
    /// </summary>
    private static double CalculateDistanceToNearestBoundary(double lat, double lon, GeoPoint[] boundary)
    {
        if (boundary.Length == 0) return double.MaxValue;

        return boundary
            .Select(p => HaversineDistance(
                new Coordinate(lat, lon),
                new Coordinate(p.Latitude, p.Longitude)))
            .Min();
    }

    /// <summary>
    /// Fórmula de Haversine para distancia entre dos coordenadas GPS
    /// </summary>
    private static double HaversineDistance(Coordinate c1, Coordinate c2)
    {
        const double R = 6371000; // Radio de la Tierra en metros

        double dLat = ToRadians(c2.Latitude - c1.Latitude);
        double dLon = ToRadians(c2.Longitude - c1.Longitude);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRadians(c1.Latitude)) * Math.Cos(ToRadians(c2.Latitude)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}

/// <summary>
/// Estructura auxiliar para coordenadas
/// </summary>
public readonly record struct Coordinate(double Latitude, double Longitude);