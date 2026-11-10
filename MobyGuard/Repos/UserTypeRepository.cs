using Microsoft.EntityFrameworkCore;
using MobyGuard.Classes;
using MobyGuard.Database.Context;
using MobyGuard.Database.Dtos.UserTypeDtos;
using MobyGuard.Database.Models;
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

    public async Task<Result<UserTypeDto>> Add(CreateUserTypeDto createUserTypeDto)
    {
        try
        {
            var typeName = createUserTypeDto.Type.Trim();

            if (await context.UserTypes.AnyAsync(userType => userType.Type.ToLower() == typeName.ToLower()))
            {
                return Result<UserTypeDto>.Failure(RepositoryStatus.Conflict, ResponseMessages.UserTypeExists);
            }

            var entity = new UserType
            {
                Type = typeName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.UserTypes.Add(entity);

            await context.SaveChangesAsync();

            return Result<UserTypeDto>.Success(new UserTypeDto
            {
                Id = entity.Id,
                Type = entity.Type,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while adding user type with name: {Type}", createUserTypeDto.Type);

            return Result<UserTypeDto>.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> Update(int id, UpdateUserTypeDto updateUserTypeDto)
    {
        try
        {
            var userType = await context.UserTypes.FindAsync(id);

            if (userType is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.UserTypeNotFound);
            }

            var typeName = updateUserTypeDto.Type.Trim();

            if (typeName != userType.Type
                && await context.UserTypes.AnyAsync(ut => ut.Id != id && ut.Type.ToLower() == typeName.ToLower()))
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.UserTypeExists);
            }

            userType.Type = typeName;
            userType.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while updating user type with id: {Id}", id);

            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }

    public async Task<Result> DeleteById(int id)
    {
        try
        {
            var userType = await context.UserTypes.FindAsync(id);

            if (userType is null)
            {
                return Result.Failure(RepositoryStatus.NotFound, ResponseMessages.UserTypeNotFound);
            }

            if (Enum.IsDefined(typeof(PrimaryKeys.UserTypes), id))
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.CannotDeleteSystemUserType);
            }

            if (await context.Users.AnyAsync(user => user.UserTypeId == id))
            {
                return Result.Failure(RepositoryStatus.Conflict, ResponseMessages.UserTypeInUse);
            }

            context.UserTypes.Remove(userType);

            await context.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while deleting user type with id: {Id}", id);

            return Result.Failure(RepositoryStatus.InternalServerError, ResponseMessages.InternalServerError);
        }
    }
}
