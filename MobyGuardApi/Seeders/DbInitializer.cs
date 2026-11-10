using MobyGuardApi.Database.Context;
using MobyGuardApi.Database.Models;

namespace MobyGuardApi.Seeders;

public class DbInitializer
{
    public static void Initialize(DatabaseContext context)
    {
        if (!context.UserTypes.Any())
        {
            UserTypeSeeder.Seed(context);
            
            context.ResetIdentitySequence<UserType>();
        }
    }
}
