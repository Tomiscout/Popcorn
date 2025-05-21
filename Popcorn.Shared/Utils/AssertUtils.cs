namespace Popcorn.Shared.Utils;

public static class AssertUtils
{
    public static void AssertGuid(Guid? guid)
    {
        if (!guid.HasValue || guid.Value == Guid.Empty)
        {
            throw new ArgumentNullException($"Guid {guid} is empty");
        }
    }
}