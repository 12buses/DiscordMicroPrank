using System.Diagnostics;

namespace MicPrank;

internal static class DiscordHelper
{
    internal static readonly string[] ProcessNames =
    {
        "Discord",
        "DiscordPTB",
        "DiscordCanary",
        "DiscordDevelopment"
    };

    public static HashSet<int> GetProcessIds()
    {
        var result = new HashSet<int>();

        foreach (var name in ProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                try
                {
                    result.Add(process.Id);
                }
                catch
                {
                    // Процесс мог завершиться между получением списка и чтением Id.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return result;
    }

    public static bool IsRunning()
    {
        return GetProcessIds().Count > 0;
    }
}
