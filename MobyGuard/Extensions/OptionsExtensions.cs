namespace MobyGuard.Extensions;

public static class OptionsExtensions
{
    public static void AddValidatedOptions<T>(this IServiceCollection services) where T : class
    {
        services.AddOptions<T>()
            .BindConfiguration(typeof(T).Name)
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
