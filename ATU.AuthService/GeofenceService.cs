namespace ATU.AuthService;

public interface IGeofenceService
{
    GeofenceResult ValidateProximity(Coordinate operatorCoordinate, Coordinate supervisorCoordinate, double maxDistanceMeters = 50);
}

public sealed class GeofenceService : IGeofenceService
{
    public GeofenceResult ValidateProximity(Coordinate operatorCoordinate, Coordinate supervisorCoordinate, double maxDistanceMeters = 50)
    {
        var distance = HaversineDistance(operatorCoordinate, supervisorCoordinate);
        return distance <= maxDistanceMeters
            ? new GeofenceResult(true, distance, "Operador y supervisor dentro de proximidad autorizada.")
            : new GeofenceResult(false, distance, "Operador y supervisor fuera de proximidad autorizada.");
    }

    private static double HaversineDistance(Coordinate p1, Coordinate p2)
    {
        const double earthRadius = 6_371_000;
        var dLat = ToRadians(p2.Latitude - p1.Latitude);
        var dLon = ToRadians(p2.Longitude - p1.Longitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRadians(p1.Latitude)) * Math.Cos(ToRadians(p2.Latitude))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return earthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => Math.PI * degrees / 180d;
}

public readonly record struct Coordinate(double Latitude, double Longitude);
public sealed record GeofenceResult(bool IsAllowed, double DistanceMeters, string Message);
