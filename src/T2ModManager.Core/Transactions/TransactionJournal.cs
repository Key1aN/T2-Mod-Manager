namespace T2ModManager.Core.Transactions;

public enum TransactionPhase
{
    Prepared,
    ExeCommitted,
    DatCommitted,
    Completed,
    RollingBack,
    RolledBack
}

public sealed class TransactionJournal
{
    public int SchemaVersion { get; init; } = 1;
    public required string TransactionId { get; init; }
    public required string GameDirectory { get; init; }
    public required string StagedExecutable { get; init; }
    public required string StagedArchive { get; init; }
    public required string RollbackExecutable { get; init; }
    public required string RollbackArchive { get; init; }
    public required string ExpectedExecutableSha256 { get; init; }
    public required string ExpectedArchiveSha256 { get; init; }
    public TransactionPhase Phase { get; set; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

