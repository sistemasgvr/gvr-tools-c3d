namespace GvrTools.Licensing.Device
{
    /// <summary>
    /// Huella estable del PC para el bind node-locked del seat (docs/LICENSING_PLAN.md, "Huella de
    /// máquina"): hash de MachineGuid + volumen de sistema + SID de usuario. Nunca se envía el dato
    /// crudo al servidor, solo el hash.
    /// </summary>
    public interface IMachineFingerprint
    {
        string GetFingerprint();
    }
}
