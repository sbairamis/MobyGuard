namespace MobyGuardApi.Helpers;

public class EnumsHelper
{
    public static Dictionary<string, int> EnumToDictionary<TEnum>() where TEnum : Enum
    {
        var enumValues = Enum.GetValues(typeof(TEnum)).Cast<TEnum>();

        return enumValues.ToDictionary(
            e => e.ToString(),
            e => (int)(object)e
        );
    }
}
