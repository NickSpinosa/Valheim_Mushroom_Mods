namespace SeparateSpawns
{
    /// <summary>
    /// When the FindSpawnPoint postfix should leave vanilla alone versus replace
    /// the StartTemple fallback with a Group Spawn.
    /// </summary>
    /// <remarks>
    /// Vanilla priority is logout (login only) → bed → StartTemple. The postfix
    /// must not run during the logout/bed wait: <c>usedLogoutPoint</c> stays
    /// false until logout succeeds, and overriding early redirects world
    /// streaming to the Group Spawn so the player never reappears where they
    /// logged out.
    /// </remarks>
    public static class SpawnOverrideDecision
    {
        public static bool ShouldLeaveVanillaAlone(
            bool respawnAfterDeath,
            bool haveLogoutPoint,
            bool usedLogoutPoint,
            bool haveCustomSpawnPoint)
        {
            // Logout already applied this call (vanilla clears HaveLogoutPoint first).
            if (usedLogoutPoint)
            {
                return true;
            }

            // Login with a saved logout point — including while its zone streams in.
            if (!respawnAfterDeath && haveLogoutPoint)
            {
                return true;
            }

            // Bed spawn still claimed (waiting for zone or bed object).
            if (haveCustomSpawnPoint)
            {
                return true;
            }

            return false;
        }
    }
}
