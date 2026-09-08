using System.Security.Cryptography;
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
public class EditModel(
    LicenseDbContext db,
    IReleaseArtifactStore artifacts,
    ReleaseUploadProgressStore progressStore) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public ReleaseInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? ArtifactFile { get; set; }

    public string? ExistingFileName { get; private set; }
    public bool MinioConfigured => artifacts.IsConfigured;

    public async Task<IActionResult> OnGetAsync()
    {
        var release = await db.Releases.FindAsync(Id);
        if (release is null)
        {
            return NotFound();
        }

        ExistingFileName = release.FileName;
        Input = new ReleaseInput
        {
            Version = release.Version,
            Kind = release.Kind,
            Notes = release.Notes
        };
        return Page();
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
        var release = await db.Releases.FindAsync(Id);
        if (release is null)
        {
            return NotFound();
        }

        ExistingFileName = release.FileName;
        var progressId = TryReadProgressId();
        if (progressId is Guid startedId && ArtifactFile is { Length: > 0 })
        {
            progressStore.Start(startedId);
            progressStore.Set(startedId, 68, "Archivo recibido. Validando…");
        }

        try
        {
            if (!SemVersion.TryParse(Input.Version, out _))
            {
                FailProgress(progressId, "Versión SemVer inválida.");
                ModelState.AddModelError("Input.Version", "Usa una versión válida con formato MAJOR.MINOR.PATCH, por ejemplo 1.0.0.");
                return InvalidEditPage("Usa una versión válida con formato MAJOR.MINOR.PATCH, por ejemplo 1.0.0.");
            }

            var version = SemVersion.Normalize(Input.Version);
            var kind = string.Equals(Input.Kind, ReleaseKinds.Update, StringComparison.OrdinalIgnoreCase)
                ? ReleaseKinds.Update
                : ReleaseKinds.Installer;

            var existingVersions = await db.Releases
                .Where(r => r.Id != Id && r.Channel == "stable" && r.Kind == kind)
                .Select(r => r.Version)
                .ToListAsync();
            var highest = SemVersion.MaxOf(existingVersions);
            var keepingSameVersion = string.Equals(version, release.Version, StringComparison.Ordinal);
            if (!keepingSameVersion
                && highest is not null
                && SemVersion.TryParse(version, out var candidate)
                && candidate is not null
                && !candidate.IsGreaterThan(highest))
            {
                var kindLabel = kind == ReleaseKinds.Update ? "Update" : "Instalador";
                var message =
                    $"La versión debe ser mayor que la última publicada para {kindLabel} ({highest}).";
                FailProgress(progressId, message);
                ModelState.AddModelError("Input.Version", message);
                return InvalidEditPage(message);
            }

            if (ArtifactFile is { Length: > 0 })
            {
                if (!artifacts.IsConfigured)
                {
                    FailProgress(progressId, "MinIO no está configurado en el servidor.");
                    ModelState.AddModelError(nameof(ArtifactFile), "MinIO no está configurado en el servidor.");
                    return InvalidEditPage("MinIO no está configurado en el servidor.");
                }

                if (progressId is Guid checksumId)
                {
                    progressStore.Set(checksumId, 72, "Calculando checksum…");
                }

                await using var buffer = new MemoryStream();
                await ArtifactFile.CopyToAsync(buffer);
                buffer.Position = 0;
                release.Checksum = Convert.ToHexString(await SHA256.HashDataAsync(buffer)).ToLowerInvariant();
                buffer.Position = 0;
                release.ArtifactLocation = await artifacts.UploadAsync(
                    buffer,
                    version,
                    ArtifactFile.FileName,
                    ArtifactFile.ContentType ?? "application/octet-stream",
                    HttpContext.RequestAborted,
                    CreateMinioProgress(progressId));
                release.FileName = Path.GetFileName(ArtifactFile.FileName);
            }

            release.Version = version;
            release.Kind = kind;
            release.Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim();
            await db.SaveChangesAsync();

            if (progressId is Guid doneId && ArtifactFile is { Length: > 0 })
            {
                progressStore.Complete(doneId);
            }

            TempData["Saved"] = true;
            if (IsAjaxRequest())
            {
                return new JsonResult(new { redirect = Url.Page("/Admin/Releases/Edit", new { id = Id }) });
            }

            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            FailProgress(progressId, ex.Message);
            throw;
        }
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

    private IActionResult InvalidEditPage(string message)
    {
        if (!IsAjaxRequest())
        {
            return Page();
        }

        return BadRequest(new { message });
    }

    private bool IsAjaxRequest() =>
        string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    public sealed class ReleaseInput
    {
        public string Version { get; set; } = string.Empty;
        public string Kind { get; set; } = ReleaseKinds.Installer;
        public string? Notes { get; set; }
    }
}
