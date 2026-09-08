namespace GvrTools.Civil3D.Infrastructure
{
    /// <summary>
    /// What the current binary was compiled against. Lets runtime code and user-facing messages
    /// explain *why* a given code path was chosen without duplicating the <c>#if</c> ladder.
    /// </summary>
    public static class C3DVersionInfo
    {
#if C3D2027
        public const int CompiledFor = 2027;
#elif C3D2026
        public const int CompiledFor = 2026;
#elif C3D2025
        public const int CompiledFor = 2025;
#elif C3D2024
        public const int CompiledFor = 2024;
#elif C3D2023
        public const int CompiledFor = 2023;
#elif C3D2022
        public const int CompiledFor = 2022;
#else
        public const int CompiledFor = 2021;
#endif
    }
}
