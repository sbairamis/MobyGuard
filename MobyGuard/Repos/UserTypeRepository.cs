using Microsoft.EntityFrameworkCore;
using MobyGuard.Classes;
using MobyGuard.Database.Context;
using MobyGuard.Database.Dtos.UserTypeDtos;
using MobyGuard.Enumerators;

namespace MobyGuard.Repos;

public class UserTypeRepository(DatabaseContext context, ILogger<UserTypeRepository> logger)
{
    public async Task<Result<List<UserTypeDto>>> GetAll()
    {
        try
        {
            var userTypes = await context.UserTypes
                .AsNoTracking()
                .OrderBy(userType => userType.Id)
                .Select(userType => new UserTypeDto
                {
                    Id = userType.Id,
                    Type = userType.Type,
                    CreatedAt = userType.CreatedAt,
                    UpdatedAt = userType.UpdatedAt
                })
                .ToListAsync();

            return Result<List<UserTypeDto>>.Success(userTypes);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while fetching user types.");

            return Result<List<UserTypeDto>>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result<UserTypeDto>> GetById(int id)
    {
        try
        {
            var userType = await context.UserTypes
                .AsNoTracking()
                .Where(ut => ut.Id == id)
                .Select(ut => new UserTypeDto
                {
                    Id = ut.Id,
                    Type = ut.Type,
                    CreatedAt = ut.CreatedAt,
                    UpdatedAt = ut.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (userType is null)
            {
                return Result<UserTypeDto>.Failure(RepositoryStatus.NotFound, ResponseMessages.UserTypeNotFound);
            }

            return Result<UserTypeDto>.Success(userType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while fetching user type with id: {Id}", id);

            return Result<UserTypeDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }
}
