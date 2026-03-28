using ATU.Shared.Models;

namespace ATU.AuthService;

public class ZoneRepository : IZoneRepository
{
    // Base de datos en memoria de zonas (reemplazar con SQL Server en producción)
    private static readonly Dictionary<string, LoadingZone> _zones = new()
    {
        ["CAMARA-FRIA-PRINCIPAL"] = new LoadingZone
        {
            Id = "CAMARA-FRIA-PRINCIPAL",
            Name = "Cámara Fría Principal",
            BoundaryPoints = new[]
            {
                new GeoPoint(20.6721, -103.3475),
                new GeoPoint(20.6725, -103.3475),
                new GeoPoint(20.6725, -103.3470),
                new GeoPoint(20.6721, -103.3470)
            }
        },
        ["CAMARA-FRIA-SECUNDARIA"] = new LoadingZone
        {
            Id = "CAMARA-FRIA-SECUNDARIA",
            Name = "Cámara Fría Secundaria",
            BoundaryPoints = new[]
            {
                new GeoPoint(20.6730, -103.3480),
                new GeoPoint(20.6735, -103.3480),
                new GeoPoint(20.6735, -103.3475),
                new GeoPoint(20.6730, -103.3475)
            }
        },
        ["ZONA-EMBARQUES"] = new LoadingZone
        {
            Id = "ZONA-EMBARQUES",
            Name = "Zona de Embarques",
            BoundaryPoints = new[]
            {
                new GeoPoint(20.6715, -103.3470),
                new GeoPoint(20.6720, -103.3470),
                new GeoPoint(20.6720, -103.3465),
                new GeoPoint(20.6715, -103.3465)
            }
        }
    };

    public Task<LoadingZone?> GetByIdAsync(string zoneId)
    {
        _zones.TryGetValue(zoneId.ToUpperInvariant(), out var zone);
        return Task.FromResult(zone);
    }

    public Task<IEnumerable<LoadingZone>> GetAllAsync()
    {
        return Task.FromResult(_zones.Values.AsEnumerable());
    }
}