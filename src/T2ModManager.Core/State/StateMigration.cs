using T2ModManager.Core.Models;

namespace T2ModManager.Core.State;

public static class StateMigration
{
    public static bool PrepareModCatalog(InstalledState state)
    {
        if (state.ModCatalogRevision >= InstalledState.CurrentModCatalogRevision)
        {
            return false;
        }

        // Legacy builds displayed previously imported inactive packages immediately.
        // Start the new package-driven catalog empty, but retain active records so the
        // manager can still restore their game-file changes safely.
        state.Mods.RemoveAll(mod => !mod.Active);
        state.ModCatalogRevision = InstalledState.CurrentModCatalogRevision;
        return true;
    }
}
