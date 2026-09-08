# Activation

Ventanas WPF en español (mismo tema `GvrTheme` que el resto del add-in):

- `AccountLicenseWindow` — **Cambiar plan / Cuenta** unificada: resumen del plan, soporte, pegar key, Activar, Desactivar este PC. Único punto de entrada para activar/reactivar (arranque, kick, tool sin licencia válida, CTA de la cinta) -- `ActivateLicenseWindow` existió como una versión separada y más simple, pero auditoría del sistema encontró que duplicaba esta misma lógica con UX distinta según el punto de entrada, así que se retiró.
- `UpdateAvailableWindow` — aviso de actualización

Abrir vía `LicenseUi.ShowChangePlan` / `ShowAccount`.
