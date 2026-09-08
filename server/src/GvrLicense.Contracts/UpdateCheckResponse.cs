namespace GvrLicense.Contracts;

public sealed class UpdateCheckResponse
{
    public required bool UpdateAvailable { get; init; }
    public string? LatestVersion { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ReleaseNotes { get; init; }
}
