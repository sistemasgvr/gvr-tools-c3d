using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GvrLicense.Infrastructure;

/// <summary>
/// Solo para `dotnet ef migrations add/update` en tiempo de diseño. Por defecto apunta al Postgres
/// desechable de server/docker-compose.yml (esas credenciales no son un secreto real, ver ese
/// archivo); si la variable de entorno ConnectionStrings__Postgres está definida (mismo nombre que
/// usa el runtime en EasyPanel), la usa en su lugar -- así se puede migrar contra otra base sin
/// escribir esa cadena en ningún archivo del repo. En runtime, LicenseDbContext se registra con la
/// connection string de configuración normal (Program.cs), nunca con esto.
/// </summary>
public sealed class LicenseDbContextFactory : IDesignTimeDbContextFactory<LicenseDbContext>
{
    public LicenseDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=gvrlicense_dev;Username=gvrlicense_dev;Password=gvrlicense_dev";

        var optionsBuilder = new DbContextOptionsBuilder<LicenseDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new LicenseDbContext(optionsBuilder.Options);
    }
}
