using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Amiga500.UI;

public static class NativeFileDialog
{
    public static string? SelectFile(string title, string filter, string defaultExt = "")
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return SelectFileWindows(title, filter);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return SelectFileLinux(title, filter);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return SelectFileMac(title, filter);
        }

        return ConsoleFallback(title);
    }

    private static string? SelectFileWindows(string title, string filter)
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Execute PowerShell System.Windows.Forms.OpenFileDialog script for a clean native dialog
                string script = $"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.OpenFileDialog; $f.Title = '{title}'; $f.Filter = '{filter}'; if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {{ Write-Host $f.FileName }}";
                var psi = new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();
                    if (!string.IsNullOrWhiteSpace(output) && File.Exists(output))
                    {
                        result = output;
                    }
                }
            }
            catch
            {
                // Ignore
            }
        });
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        thread.Join();

        return result;
    }

    private static string? SelectFileLinux(string title, string filter)
    {
        try
        {
            // Try zenity first
            var psi = new ProcessStartInfo("zenity", $"--file-selection --title=\"{title}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (!string.IsNullOrWhiteSpace(output) && File.Exists(output))
                {
                    return output;
                }
            }
        }
        catch { }

        try
        {
            // Try kdialog
            var psi = new ProcessStartInfo("kdialog", $"--getopenfilename . --title \"{title}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (!string.IsNullOrWhiteSpace(output) && File.Exists(output))
                {
                    return output;
                }
            }
        }
        catch { }

        return ConsoleFallback(title);
    }

    private static string? SelectFileMac(string title, string filter)
    {
        try
        {
            string script = $"POSIX path of (choose file with prompt \"{title}\")";
            var psi = new ProcessStartInfo("osascript", $"-e '{script}'")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (!string.IsNullOrWhiteSpace(output) && File.Exists(output))
                {
                    return output;
                }
            }
        }
        catch { }

        return ConsoleFallback(title);
    }

    private static string? ConsoleFallback(string title)
    {
        Console.WriteLine($"\n=== {title} ===");
        Console.Write("Enter path to file (or press Enter to cancel): ");
        string? input = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(input) && File.Exists(input))
        {
            return input;
        }
        return null;
    }
}
