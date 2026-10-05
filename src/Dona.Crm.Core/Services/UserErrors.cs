namespace Dona.Crm.Web.Services;

/// <summary>Decides what a user may see for a failed operation: deliberate Russian domain messages pass through, everything else becomes a fallback.</summary>
public static class UserErrors
{
    /// <summary>Returns <paramref name="exception"/>'s message only for deliberate domain errors (InvalidOperationException or ArgumentException with Cyrillic text); otherwise <paramref name="fallback"/>. The full exception is logged.</summary>
    public static string Describe(Exception exception, string fallback)
    {
        if (IsUserFacing(exception)) return exception.Message;
        Console.Error.WriteLine(exception);
        return fallback;
    }

    public static bool IsUserFacing(Exception exception) =>
        exception is InvalidOperationException or ArgumentException && !string.IsNullOrWhiteSpace(exception.Message) && ContainsCyrillic(exception.Message);

    private static bool ContainsCyrillic(string value)
    {
        foreach (var c in value) if (c is >= 'Ѐ' and <= 'ӿ') return true;
        return false;
    }
}
