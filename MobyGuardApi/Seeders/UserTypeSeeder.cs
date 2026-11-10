using MobyGuardApi.Database.Context;

namespace MobyGuardApi.Seeders;

public class UserTypeSeeder
{
    public static void Seed(DatabaseContext context)
    {
        context.UserTypes.Add(new Database.Models.UserType
        {
            Id = (int)Enumerators.PrimaryKeys.UserTypes.Admin,
            Type = "Admin",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        context.UserTypes.Add(new Database.Models.UserType
        {
            Id = (int)Enumerators.PrimaryKeys.UserTypes.User,
            Type = "User",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        context.UserTypes.Add(new Database.Models.UserType
        {
            Id = (int)Enumerators.PrimaryKeys.UserTypes.Service,
            Type = "Service",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        context.SaveChanges();
    }
}
