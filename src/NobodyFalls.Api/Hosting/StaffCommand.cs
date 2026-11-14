using NobodyFalls.Application.Staff;

namespace NobodyFalls.Api.Hosting;

/// <summary>
/// <c>dotnet NobodyFalls.Api.dll staff grant &lt;pseudo&gt;</c> makes the first admin
/// (ADR 0017). It works only while the team has no admin: from then on, roles are given
/// from the admin, with two-factor sign-in and an audit trail.
/// </summary>
internal static class StaffCommand
{
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        if (args.Length < 1 || args[0] != "staff")
        {
            return false;
        }

        if (args.Length is < 3 or > 4 || args[1] != "grant" || (args.Length == 4 && args[3] != "admin"))
        {
            await Console.Error.WriteLineAsync("Usage: staff grant <pseudo> [admin]");
            Environment.ExitCode = 2;
            return true;
        }

        var result = await app.Services.GetRequiredService<IStaffStore>()
            .BootstrapAdminAsync(args[2], "operator command: staff grant", CancellationToken.None);
        if (result.IsFailure)
        {
            await Console.Error.WriteLineAsync($"{result.Error!.Code}: {result.Error.Message}");
            Environment.ExitCode = 1;
            return true;
        }

        await Console.Out.WriteLineAsync($"{result.Value.Pseudo} is now an admin. Sign in at /admin to set up two-factor authentication.");
        return true;
    }
}
