using Microsoft.AspNetCore.Mvc;
using Dapper;
using Microsoft.Data.SqlClient;

namespace ATU.AuthService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScannerController : ControllerBase
{
    private readonly string _connectionString;

    public ScannerController(IConfiguration config)
    {
        // Usamos "SqlServer" que es el nombre en tu appsettings.json
        _connectionString = config.GetConnectionString("SqlServer");
    }

    [HttpGet("catalogo")]
    public async Task<IActionResult> GetCatalogo()
    {
        // El SQL que necesita la DLL para no fallar
        const string sql = @"
            SELECT LTRIM(RTRIM(prod_clave)) as prod_clave, LTRIM(RTRIM(prod_tipo)) as prod_tipo
            FROM tb_cat_producto WHERE prod_status = 'A'
            ORDER BY LEN(prod_clave) DESC";

        try
        {
            using var db = new SqlConnection(_connectionString);
            // Dapper devuelve la lista directamente
            var productos = await db.QueryAsync(sql);
            return Ok(productos);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error: {ex.Message}");
        }
    }
}