using GvrLicense.Domain.Entities;
using GvrLicense.Domain.Security;
using GvrLicense.Infrastructure;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
    ?? throw new InvalidOperationException("Falta la variable de entorno ConnectionStrings__Postgres.");

Console.Write("Usuario admin: ");
var username = (Console.ReadLine() ?? string.Empty).Trim();
if (string.IsNullOrEmpty(username))
{
    username = "admin";
}

Console.Write("Password: ");
var password = Console.ReadLine();
if (string.IsNullOrEmpty(password))
{
    throw new InvalidOperationException("Password vacío.");
}

var options = new DbContextOptionsBuilder<LicenseDbContext>().UseNpgsql(connectionString).Options;
await using var db = new LicenseDbContext(options);

if (await db.AdminUsers.AnyAsync(u => u.Username == username))
{
    Console.WriteLine($"Ya existe un admin con el usuario '{username}'. Nada que hacer -- usa /Admin/Users/Create ya logueado para agregar otro.");
    return;
}

db.AdminUsers.Add(new AdminUser
{
    Id = Guid.NewGuid(),
    Username = username,
    PasswordHash = PasswordHasher.Hash(password),
    IsActive = true,
    CreatedAtUtc = DateTimeOffset.UtcNow
});
await db.SaveChangesAsync();

Console.WriteLine($"Admin '{username}' creado en la base de datos. Ya puede iniciar sesión en /Admin/Login.");
