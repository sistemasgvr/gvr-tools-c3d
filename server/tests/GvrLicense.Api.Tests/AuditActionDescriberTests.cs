using GvrLicense.Domain.Audit;
using Xunit;

namespace GvrLicense.Api.Tests;

public class AuditActionDescriberTests
{
    [Theory]
    [InlineData("license.create", null, "Licencia creada")]
    [InlineData("device.kick", null, "PC liberado")]
    [InlineData("license.activate", null, "Licencia activada con clave")]
    [InlineData("license.activate_free", null, "Alta plan Free")]
    [InlineData("plan.service_suspend", null, "Servicio del plan suspendido")]
    [InlineData("plan.service_resume", null, "Servicio del plan reactivado")]
    public void Describe_KnownActions(string action, string? details, string expected) =>
        Assert.Equal(expected, AuditActionDescriber.Describe(action, details));

    [Fact]
    public void Describe_StatusChange_ToSuspended() =>
        Assert.Equal(
            "Licencia suspendida",
            AuditActionDescriber.Describe("license_status_changed", """{"from":"Active","to":"Suspended"}"""));

    [Fact]
    public void Describe_StatusChange_ToActive() =>
        Assert.Equal(
            "Licencia reactivada",
            AuditActionDescriber.Describe("license_status_changed", """{"from":"Suspended","to":"Active"}"""));

    // El trigger audit_license_status_change de Postgres escribe el status como número (0/1/2),
    // no como texto -- este es el shape real que llega desde la base de datos.
    [Fact]
    public void Describe_StatusChange_NumericFromTrigger() =>
        Assert.Equal(
            "Licencia suspendida",
            AuditActionDescriber.Describe("license_status_changed", """{"from":0,"to":1}"""));
}

public class AuditDetailsFormatterTests
{
    [Fact]
    public void Summarize_ShowsIpFingerprintAndDeviceName()
    {
        var summary = AuditDetailsFormatter.Summarize(
            """{"fingerprint":"abcdef0123456789ffff","deviceName":"PC-OBRA","ip":"190.1.2.3"}""");

        Assert.Contains("IP 190.1.2.3", summary);
        Assert.Contains("PC PC-OBRA", summary);
        Assert.Contains("FP abcdef0123456789…", summary);
    }

    [Fact]
    public void TryGetIp_ReadsStringProperty() =>
        Assert.Equal("10.0.0.1", AuditDetailsFormatter.TryGetIp("""{"ip":"10.0.0.1"}"""));
}
