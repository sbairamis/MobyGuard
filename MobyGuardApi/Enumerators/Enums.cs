namespace MobyGuardApi.Enumerators;

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
    InternalServerError
}
