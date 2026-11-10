namespace MobyGuard.Enumerators;

public enum RepositoryStatus
{
    Success,
    Created,
    BadRequest,
    Unauthorized,
    Forbidden,
    Conflict,
    NotFound,
    TooManyRequests,
    BadGateway,
    InternalServerError
}
