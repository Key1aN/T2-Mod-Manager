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
                state,
                ManifestSerializer.JsonOptions,
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, paths.StateFile, overwrite: true);
    }
}

