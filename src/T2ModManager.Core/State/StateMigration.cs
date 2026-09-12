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

        // Earlier development builds may have populated the shared per-user catalog
        // while validating package sets. Reset those inactive entries once for this
        // release so its first Mods view is empty. Retain active records so the
        // manager can still restore their game-file changes safely.
        state.Mods.RemoveAll(mod => !mod.Active);
        state.ModCatalogRevision = InstalledState.CurrentModCatalogRevision;
        return true;
    }
}
