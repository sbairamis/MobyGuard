using MobyGuard.Enumerators;

namespace MobyGuard.Classes;

public class Result
{
    public RepositoryStatus Status { get; set; }
    public string ResponseMessage { get; set; } = string.Empty;

    public static Result Success(RepositoryStatus status = RepositoryStatus.Success, string responseMessage = "")
        => new() { Status = status, ResponseMessage = responseMessage };

    public static Result Failure(RepositoryStatus status, string responseMessage)
        => new() { Status = status, ResponseMessage = responseMessage };
}

public class Result<T> : Result
{
    public T? Data { get; set; }

    public static Result<T> Success(T? data, RepositoryStatus status = RepositoryStatus.Success, string responseMessage = "")
        => new() { Status = status, Data = data, ResponseMessage = responseMessage };

    public new static Result<T> Failure(RepositoryStatus status, string responseMessage)
        => new() { Status = status, ResponseMessage = responseMessage };
}
