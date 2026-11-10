using MobyGuard.Database.Context;
using MobyGuard.Database.Models;

namespace MobyGuard.Seeders;

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
