using System.Security.Cryptography;
using System.Text.Json.Serialization;
using GvrLicense.Domain.Entities;
using GvrLicense.Domain.Versioning;
using GvrLicense.Infrastructure;
using GvrLicense.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Releases;

[RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
[RequestSizeLimit(524_288_000)]
public class IndexModel(
    LicenseDbContext db,
    IReleaseArtifactStore artifacts,
    ReleaseUploadProgressStore progressStore) : PageModel
{
    public List<ReleaseRow> Rows { get; private set; } = [];

    /// <summary>Enlace estable para enviar al cliente (redirige al último instalador en MinIO).</summary>
    public string PublicInstallerUrl { get; private set; } = string.Empty;

    public bool MinioConfigured { get; private set; }

    [BindProperty]
    public ReleaseInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? ArtifactFile { get; set; }

    public async Task OnGetAsync()
    {
        MinioConfigured = artifacts.IsConfigured;
        PublicInstallerUrl = $"{Request.Scheme}://{Request.Host}/download";

        var raw = await db.Releases.ToListAsync();
        Rows = raw
            .Select(r =>
            {
                SemVersion.TryParse(r.Version, out var sem);
                return new ReleaseRow(
                    r.Id,
                    r.Version,
                    r.Channel,
                    r.Kind,
                    r.FileName,
                    r.ArtifactLocation,
                    r.Notes,
                    r.PublishedAtUtc,
                    FileNameVersionLooksMismatched(r.FileName, r.Version),
                    KindSortOrder(r.Kind),
                    sem);
            })
            .OrderBy(r => r.KindOrder) // instalador primero
            .ThenByDescending(r => r.ParsedVersion)
            .ThenByDescending(r => r.PublishedAtUtc)
            .ToList();
    }

    public IActionResult OnGetUploadProgress(Guid progressId)
    {
        var snapshot = progressStore.Get(progressId);
        if (snapshot is null)
        {
            return new JsonResult(new { percent = 0, phase = "Esperando…", done = false, error = (string?)null });
        }

        return new JsonResult(new
        {
            percent = snapshot.Percent,
            phase = snapshot.Phase,
            done = snapshot.Done,
            error = snapshot.Error
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        MinioConfigured = artifacts.IsConfigured;
        var progressId = TryReadProgressId();
        if (progressId is Guid startedId)
        {
            progressStore.Start(startedId);
            progressStore.Set(startedId, 68, "Archivo recibido. Validando…");
        }

        try
        {
            if (!artifacts.IsConfigured)
            {
                FailProgress(progressId, "MinIO no está configurado en el servidor.");
                ModelState.AddModelError(string.Empty, "MinIO no está configurado en el servidor.");
                return await InvalidPageAsync();
            }

            if (ArtifactFile is null || ArtifactFile.Length == 0)
            {
                FailProgress(progressId, "Sube el archivo del instalador o del paquete de update.");
                ModelState.AddModelError(nameof(ArtifactFile), "Sube el archivo del instalador o del paquete de update.");
                return await InvalidPageAsync();
            }

            if (!SemVersion.TryParse(Input.Version, out _))
            {
                FailProgress(progressId, "Versión SemVer inválida.");
                ModelState.AddModelError("Input.Version", "Usa una versión válida con formato MAJOR.MINOR.PATCH, por ejemplo 1.0.0.");
                return await InvalidPageAsync();
            }

            var version = SemVersion.Normalize(Input.Version);
            var kind = string.Equals(Input.Kind, ReleaseKinds.Update, StringComparison.OrdinalIgnoreCase)
                ? ReleaseKinds.Update
                : ReleaseKinds.Installer;

            var existingVersions = await db.Releases
                .Where(r => r.Channel == "stable" && r.Kind == kind)
                .Select(r => r.Version)
                .ToListAsync();
            var highest = SemVersion.MaxOf(existingVersions);
            if (highest is not null
                && SemVersion.TryParse(version, out var candidate)
                && candidate is not null
                && !candidate.IsGreaterThan(highest))
            {
                var kindLabel = kind == ReleaseKinds.Update ? "Update" : "Instalador";
                var message =
                    $"La versión debe ser mayor que la última publicada para {kindLabel} ({highest}).";
                FailProgress(progressId, message);
                ModelState.AddModelError("Input.Version", message);
                return await InvalidPageAsync();
            }

            if (progressId is Guid checksumId)
            {
                progressStore.Set(checksumId, 72, "Calculando checksum…");
            }

            await using var buffer = new MemoryStream();
            await ArtifactFile.CopyToAsync(buffer);
            buffer.Position = 0;
            var checksum = Convert.ToHexString(await SHA256.HashDataAsync(buffer)).ToLowerInvariant();
            buffer.Position = 0;

            var objectKey = await artifacts.UploadAsync(
                buffer,
                version,
                ArtifactFile.FileName,
                ArtifactFile.ContentType ?? "application/octet-stream",
                HttpContext.RequestAborted,
                CreateMinioProgress(progressId));

            var fileName = Path.GetFileName(ArtifactFile.FileName);
            if (FileNameVersionLooksMismatched(fileName, version))
            {
                TempData["FileNameMismatch"] =
                    $"La versión del release es {version}, pero el archivo se llama «{fileName}». " +
                    "Conviene renombrar el .exe (ej. GvrTools-Setup-1.0.1.exe) antes de subir, " +
                    "para no confundir Instalador vs Update.";
            }

            db.Releases.Add(new Release
            {
                Id = Guid.NewGuid(),
                Version = version,
                Channel = "stable",
                Kind = kind,
                FileName = fileName,
                ArtifactLocation = objectKey,
                Checksum = checksum,
                Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
                SignatureBase64 = string.Empty,
                PublishedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            if (progressId is Guid doneId)
            {
                progressStore.Complete(doneId);
            }

            TempData["Saved"] = true;
            if (kind == ReleaseKinds.Installer)
                TempData["PublicUrl"] = $"{Request.Scheme}://{Request.Host}/download";
            if (IsAjaxRequest())
            {
                return new JsonResult(new { redirect = Url.Page("/Admin/Releases/Index") });
            }

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            FailProgress(progressId, ex.Message);
            throw;
        }
    }

    private static int KindSortOrder(string kind) =>
        string.Equals(kind, ReleaseKinds.Installer, StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    /// <summary>
    /// True si el nombre del archivo contiene un X.Y.Z distinto al campo Versión
    /// (ej. version=1.0.1 pero archivo GvrTools-Setup-1.0.0.exe).
    /// </summary>
    internal static bool FileNameVersionLooksMismatched(string? fileName, string version)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(version))
            return false;

        var match = System.Text.RegularExpressions.Regex.Match(
            fileName,
            @"(\d+\.\d+\.\d+)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        if (!SemVersion.TryParse(match.Groups[1].Value, out var inName) ||
            !SemVersion.TryParse(version, out var declared))
            return false;

        return inName!.ToString() != declared!.ToString();
    }

    private IProgress<(long Transferred, long Total)>? CreateMinioProgress(Guid? progressId)
    {
        if (progressId is not Guid id)
        {
            return null;
        }

        return new Progress<(long Transferred, long Total)>(tuple =>
        {
            var (transferred, total) = tuple;
            // % = avance real del archivo en MinIO (coincide con MB mostrados).
            var percent = total > 0
                ? (int)Math.Clamp(Math.Round(100d * transferred / total), 0, 99)
                : 0;
            var mb = transferred / (1024d * 1024d);
            var totalMb = total > 0 ? total / (1024d * 1024d) : 0;
            var phase = total > 0
                ? $"Subiendo a MinIO… {mb:0.0}/{totalMb:0.0} MB"
                : $"Subiendo a MinIO… {mb:0.0} MB";
            progressStore.Set(id, percent, phase);
        });
    }

    private void FailProgress(Guid? progressId, string message)
    {
        if (progressId is Guid id)
        {
            progressStore.Fail(id, message);
        }
    }

    private Guid? TryReadProgressId() =>
        Guid.TryParse(Request.Headers["X-Upload-Progress-Id"], out var id) ? id : null;

    private async Task<IActionResult> InvalidPageAsync()
    {
        await OnGetAsync();
        if (!IsAjaxRequest())
        {
            return Page();
        }

        var message = ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "No se pudo publicar el release."
                : error.ErrorMessage)
            .FirstOrDefault() ?? "No se pudo publicar el release.";
        return BadRequest(new { message });
    }

    private bool IsAjaxRequest() =>
        string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    public sealed record ReleaseRow(
        Guid Id,
        string Version,
        string Channel,
        string Kind,
        string? FileName,
        string ArtifactLocation,
        string? Notes,
        DateTimeOffset PublishedAtUtc,
        bool FileNameMismatch,
        [property: JsonIgnore] int KindOrder,
        [property: JsonIgnore] SemVersion? ParsedVersion);

    public sealed class ReleaseInput
    {
        public string Version { get; set; } = string.Empty;
        public string Kind { get; set; } = ReleaseKinds.Installer;
        public string? Notes { get; set; }
    }
}
