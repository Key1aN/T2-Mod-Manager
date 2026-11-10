using System.Text.Json;
using T2ModManager.Core.Models;
using T2ModManager.Core.Packages;

namespace T2ModManager.Core.State;

public sealed class StateStore(AppDataPaths paths)
{
    public async Task<InstalledState> LoadAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        if (!File.Exists(paths.StateFile))
        {
            return new InstalledState();
        }

        await using FileStream stream = new(
            paths.StateFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        InstalledState state = await JsonSerializer.DeserializeAsync<InstalledState>(
            stream,
            ManifestSerializer.JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Manager state file is empty.");

        if (state.SchemaVersion != 1)
        {
            throw new InvalidDataException("Unsupported manager state version.");
        }

        // Package paths are stored relative to the portable data folder so the
        // manager keeps working after its folder is moved or the drive letter changes.
        foreach (InstalledMod mod in state.Mods)
        {
            mod.PackagePath = paths.ResolveStoredPath(mod.PackagePath);
        }

        return state;
    }

    public async Task SaveAsync(InstalledState state, CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        string temporary = paths.StateFile + ".new";
        await using (FileStream stream = new(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                ToStoredForm(state),
                ManifestSerializer.JsonOptions,
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, paths.StateFile, overwrite: true);
    }

    private InstalledState ToStoredForm(InstalledState state)
    {
        InstalledState stored = new()
        {
            SchemaVersion = state.SchemaVersion,
            ModCatalogRevision = state.ModCatalogRevision,
            GameDirectory = state.GameDirectory,
            GameBuildId = state.GameBuildId,
            CurrentExecutableSha256 = state.CurrentExecutableSha256,
            CurrentArchiveSha256 = state.CurrentArchiveSha256,
            CurrentFeatureStateSha256 = state.CurrentFeatureStateSha256,
            UpdatedAtUtc = state.UpdatedAtUtc
        };
        stored.Mods.AddRange(state.Mods.Select(mod => new InstalledMod
        {
            Id = mod.Id,
            DisplayName = mod.DisplayName,
            Version = mod.Version,
            Description = mod.Description,
            PackagePath = paths.ToStoredPath(mod.PackagePath),
            Active = mod.Active,
            InstalledAtUtc = mod.InstalledAtUtc
        }));
        return stored;
    }
}
