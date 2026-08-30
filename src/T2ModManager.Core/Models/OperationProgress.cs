namespace T2ModManager.Core.Models;

public sealed record OperationProgress(
    string Stage,
    string Message,
    double? Fraction = null);

