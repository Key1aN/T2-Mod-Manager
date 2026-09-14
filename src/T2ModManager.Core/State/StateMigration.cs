using T2ModManager.Core.Models;

namespace T2ModManager.Core.State;

public static class StateMigration
{
    public static bool PrepareModCatalog(InstalledState state, bool preserveActiveRecovery)
    {
        if (state.ModCatalogRevision >= InstalledState.CurrentModCatalogRevision)
        {
            return false;
        }

        // Earlier builds shared one per-user catalog across every release. Clear it
        // once so a new release starts with no imported or checked mods. An active
        // record is retained only when its managed game installation still exists;
        // otherwise the disabled checkbox can never be safely deactivated.
        if (preserveActiveRecovery)
        {
            state.Mods.RemoveAll(mod => !mod.Active);
        }
        else
        {
            state.Mods.Clear();
            state.GameDirectory = null;
            state.GameBuildId = null;
            state.CurrentExecutableSha256 = null;
            state.CurrentArchiveSha256 = null;
            state.CurrentFeatureStateSha256 = null;
        }

        state.ModCatalogRevision = InstalledState.CurrentModCatalogRevision;
        return true;
    }
}
