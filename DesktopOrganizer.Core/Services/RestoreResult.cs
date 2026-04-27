namespace DesktopOrganizer.Core.Services;

/// <summary>
/// Status final de uma tentativa de restauração de janela.
/// </summary>
public enum RestoreStatus
{
    Success = 0,
    Skipped = 1,
    Failed = 2
}

/// <summary>
/// Códigos de falha/skip granulares para diagnóstico. <see cref="None"/> é o
/// valor padrão para resultados de sucesso.
/// </summary>
public enum RestoreFailureCode
{
    None = 0,
    ExecutableNotFound = 1,
    LaunchFailed = 2,
    WindowNotFound = 3,
    VirtualDesktopMoveFailed = 4,
    AccessDenied = 5,
    RepositionFailed = 6
}

/// <summary>
/// Resultado de uma restauração individual. Construído via factory methods
/// para garantir consistência entre status e código.
/// </summary>
public sealed class RestoreResult
{
    public RestoreStatus Status { get; }
    public RestoreFailureCode FailureCode { get; }
    public string Message { get; }

    private RestoreResult(RestoreStatus status, RestoreFailureCode failureCode, string message)
    {
        Status = status;
        FailureCode = failureCode;
        Message = message ?? string.Empty;
    }

    public static RestoreResult Success(string message = "")
        => new(RestoreStatus.Success, RestoreFailureCode.None, message);

    public static RestoreResult Skipped(RestoreFailureCode code, string message = "")
        => new(RestoreStatus.Skipped, code, message);

    public static RestoreResult Failed(RestoreFailureCode code, string message = "")
        => new(RestoreStatus.Failed, code, message);
}
