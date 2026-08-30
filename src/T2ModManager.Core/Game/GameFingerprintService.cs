using T2ModManager.Core.Archives;
using T2ModManager.Core.Hashing;
using T2ModManager.Core.Models;
using T2ModManager.Core.Validation;

namespace T2ModManager.Core.Game;

public static class GameFingerprintService
{
    public static async Task<GameFingerprint> InspectAsync(
        GameInstallation installation,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(installation.ExecutablePath) || !File.Exists(installation.ArchivePath))
        {
            throw new FileNotFoundException("The selected folder must contain TMNT2.exe and TMNT.DAT.");
        }

        progress?.Report(new OperationProgress("verify", "Validating the 32-bit game executable…"));
        _ = PeValidator.ValidateWin32Executable(installation.ExecutablePath);

        progress?.Report(new OperationProgress("verify", "Validating the TMNT.DAT archive…"));
        _ = AfsArchive.Read(installation.ArchivePath);

        progress?.Report(new OperationProgress("hash", "Fingerprinting TMNT2.exe…", 0.1));
        string executableHash = await Sha256Service.HashFileAsync(
            installation.ExecutablePath,
            cancellationToken).ConfigureAwait(false);

        progress?.Report(new OperationProgress("hash", "Fingerprinting TMNT.DAT…", 0.2));
        string archiveHash = await Sha256Service.HashFileAsync(
            installation.ArchivePath,
            cancellationToken).ConfigureAwait(false);

        KnownGameBuild known = KnownGameBuilds.UsNaRetail;
        string buildId =
            new FileInfo(installation.ExecutablePath).Length == known.ExecutableSize &&
            new FileInfo(installation.ArchivePath).Length == known.ArchiveSize &&
            Sha256Service.EqualsHash(executableHash, known.ExecutableSha256) &&
            Sha256Service.EqualsHash(archiveHash, known.ArchiveSha256)
                ? known.BuildId
                : "unknown";

        progress?.Report(new OperationProgress("verify", "Game fingerprint completed.", 1.0));
        return new GameFingerprint(
            buildId,
            buildId == known.BuildId ? known.Region : "Unknown",
            "x86",
            new FileInfo(installation.ExecutablePath).Length,
            executableHash,
            new FileInfo(installation.ArchivePath).Length,
            archiveHash);
    }

    public static void RequireSupportedVanilla(GameFingerprint fingerprint)
    {
        KnownGameBuild known = KnownGameBuilds.UsNaRetail;
        if (fingerprint.BuildId != known.BuildId ||
            fingerprint.ExecutableSize != known.ExecutableSize ||
            fingerprint.ArchiveSize != known.ArchiveSize ||
            !Sha256Service.EqualsHash(fingerprint.ExecutableSha256, known.ExecutableSha256) ||
            !Sha256Service.EqualsHash(fingerprint.ArchiveSha256, known.ArchiveSha256))
        {
            throw new InvalidDataException(
                "This is not the verified untouched US/NA retail installation. " +
                "Restore the original TMNT2.exe and TMNT.DAT before first setup.");
        }
    }
}

