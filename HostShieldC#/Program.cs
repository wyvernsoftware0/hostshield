using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class HostShield
{
    private const string Version = "1.0.0";
    private const string TaskName = "HostShield AutoStart bcb91be8-0c1c-41ab-a831-deecf97037ac";
    private const string HostsPath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string DomainListUrl = "https://raw.githubusercontent.com/wyvernsoftware0/hostshield/refs/heads/main/domains.txt";
    private const string BeginMarker = "# BEGIN HOSTSHIELD - bcb91be8-0c1c-41ab-a831-deecf97037ac";
    private const string EndMarker = "# END HOSTSHIELD - bcb91be8-0c1c-41ab-a831-deecf97037ac";
    private static readonly HttpClient Http = new HttpClient();

    static async Task<int> Main(string[] args)
    {
        Console.Title = $"HostShield {Version}";
        PrintLine($"HostShield {Version}", ConsoleColor.Cyan);

        string exePath = CopySelf();
        await StartupTask(exePath);

        if (args.Length == 1 && args[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            DelStartupTask();
            ResetHosts();
            PauseExit();
            return 0;
        }

        PrintLine("Hint: Run 'HostShield.exe reset' to undo changes and remove the program from startup.", ConsoleColor.Yellow);

        try
        {
            var domains = await GetDomainBlocklistASync();
            var updated = AddHostShieldSection(
                RemoveHostShieldSection(File.ReadAllLines(HostsPath, Encoding.UTF8)),
                domains
            );
            File.WriteAllLines(HostsPath, updated, Encoding.UTF8);
            PrintLine($"Updated hosts file with {domains.Length} domains.", ConsoleColor.Green);
        }
        catch (UnauthorizedAccessException)
        {
            PrintLine("UnauthorizedAccessException: Did you run as administrator?", ConsoleColor.Red);
        }
        catch (Exception ex)
        {
            PrintLine($"{ex.GetType()}: {ex.Message}", ConsoleColor.Red);
        }

        CountdownExit(3);
        return 0;
    }

    private static string CopySelf()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string targetDir = Path.Combine(appData, "HostShield - bcb91be8-0c1c-41ab-a831-deecf97037ac");
        Directory.CreateDirectory(targetDir);

        string targetExe = Path.Combine(targetDir, "HostShield.exe");
        string currentExe = Process.GetCurrentProcess().MainModule.FileName;

        if (!File.Exists(targetExe) ||
            File.GetLastWriteTimeUtc(targetExe) != File.GetLastWriteTimeUtc(currentExe))
        {
            File.Copy(currentExe, targetExe, overwrite: true);
        }

        return targetExe;
    }

    private static void DelStartupTask()
    {
        PrintLine("Removing startup task...", ConsoleColor.Magenta);
        var psi = new ProcessStartInfo("schtasks", $"/Delete /TN \"{TaskName}\" /F")
        {
            Verb = "runas",
            UseShellExecute = true,
            CreateNoWindow = true
        };

        try
        {
            var proc = Process.Start(psi);
            proc.WaitForExit();
            if (proc.ExitCode == 0)
                PrintLine("Startup task removed.", ConsoleColor.Green);
            else
                PrintLine("Could not remove task or it did not exist.", ConsoleColor.Yellow);
        }
        catch (Exception ex)
        {
            PrintLine($"Error removing task: {ex.Message}", ConsoleColor.Red);
        }
    }

    private static async Task StartupTask(string exePath)
    {
        bool exists = CheckTaskExists();
        if (!exists)
        {
            bool success = CreateStartupTask(exePath);
            PrintLine(success
                ? "Startup task created successfully."
                : "Failed to create startup task.", ConsoleColor.Green);
            await Task.Delay(1000);
        }
    }
    private static bool CreateStartupTask(string exePath)
    {
        string args = $"/Create /TN \"{TaskName}\" /TR \"{exePath}\" /SC ONLOGON /RL HIGHEST /F";
        var psi = new ProcessStartInfo("schtasks", args)
        {
            Verb = "runas",
            UseShellExecute = true,
            CreateNoWindow = true
        };

        try
        {
            var proc = Process.Start(psi);
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckTaskExists()
    {
        var psi = new ProcessStartInfo("schtasks", $"/Query /TN \"{TaskName}\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var proc = Process.Start(psi);
        proc.WaitForExit();
        return proc.ExitCode == 0;
    }

    private static async Task<string[]> GetDomainBlocklistASync()
    {
        PrintLine("Fetching domain list...", ConsoleColor.Yellow);
        var text = await Http.GetStringAsync(DomainListUrl);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var filtered = Array.FindAll(lines, l => !l.StartsWith("#"));
        PrintLine($"Retrieved {filtered.Length} domains.", ConsoleColor.Cyan);
        return filtered;
    }

    private static string[] RemoveHostShieldSection(string[] lines)
    {
        PrintLine("Removing existing HostShield section...", ConsoleColor.White);
        var result = new List<string>();
        bool inSection = false;
        foreach (var line in lines)
        {
            if (line == BeginMarker) { inSection = true; continue; }
            if (line == EndMarker) { inSection = false; continue; }
            if (!inSection) result.Add(line);
        }
        return result.ToArray();
    }

    private static string[] AddHostShieldSection(string[] baseLines, string[] domains)
    {
        PrintLine("Adding new HostShield section...", ConsoleColor.White);
        var result = new List<string>(baseLines) { BeginMarker };
        foreach (var d in domains)
            result.Add($"127.0.0.1 {d}");
        result.Add(EndMarker);
        return result.ToArray();
    }

    private static void ResetHosts()
    {
        PrintLine("Resetting hosts file...", ConsoleColor.Magenta);
        var cleaned = RemoveHostShieldSection(File.ReadAllLines(HostsPath, Encoding.UTF8));
        File.WriteAllLines(HostsPath, cleaned, Encoding.UTF8);
        PrintLine("Reset complete. Hosts file restored.", ConsoleColor.Green);
    }

    private static void PrintLine(string message, ConsoleColor color, int typingDelay = 5, bool noline = false)
    {
        Console.ForegroundColor = color;
        foreach (char c in message)
        {
            Console.Write(c);
            Thread.Sleep(typingDelay);
        }
        Console.ResetColor();

        if(!noline)
            Console.WriteLine();
    }

    private static void CountdownExit(int seconds)
    {
        for (int i = seconds; i > 0; i--)
        {
            PrintLine($"Closing in {i}...", ConsoleColor.DarkYellow, 10);
            Thread.Sleep(1000);
        }
    }

    private static void PauseExit()
    {
        PrintLine("Press any key to close...", ConsoleColor.DarkGray, 10);
        Console.ReadKey();
    }
}