namespace GvrLicense.Domain.Formatting;

/// <summary>
/// GVR es una empresa peruana y su clientela también -- todo lo que se muestra en el panel admin y
/// en las páginas públicas debe verse en hora de Lima (UTC-5, sin horario de verano), aunque todo
/// se almacene y calcule internamente en UTC (guardar en UTC, mostrar en local -- práctica
/// estándar; los períodos de cuota/facturación siguen siendo por mes calendario UTC a propósito,
/// esto es solo para lo que ve una persona).
///
/// Nunca usar DateTimeOffset.ToLocalTime() para esto en el servidor: depende de la zona horaria
/// del sistema operativo del contenedor donde corre la API (normalmente UTC en Docker/EasyPanel),
/// no de dónde está la persona que mira la pantalla.
/// </summary>
public static class LimaTime
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public static DateTimeOffset ToLima(this DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    public static DateTimeOffset? ToLima(this DateTimeOffset? utc) => utc?.ToLima();

    private static TimeZoneInfo ResolveZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
        }
        catch (TimeZoneNotFoundException)
        {
            // Solo si el contenedor no trae tzdata completo (las imágenes oficiales de
            // mcr.microsoft.com/dotnet ya la traen) -- Lima es UTC-5 fijo todo el año, así que el
            // offset fijo es exacto, no una aproximación.
            return TimeZoneInfo.CreateCustomTimeZone("America/Lima (fijo)", TimeSpan.FromHours(-5), "Lima", "Lima");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("America/Lima (fijo)", TimeSpan.FromHours(-5), "Lima", "Lima");
        }
    }
}
