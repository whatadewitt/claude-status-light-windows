using System.IO;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using StatusLight.Core.Wsl;

namespace StatusLight.App.Platform;

[SupportedOSPlatform("windows")]
public sealed class WslCommandRunner : ICommandRunner
{
    public CommandResult Run(IReadOnlyList<string> args, TimeSpan timeout)
    {
        try
        {
            var psi = new ProcessStartInfo("wsl.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["WSL_UTF8"] = "1";

            using var process = Process.Start(psi);
            if (process is null) return new CommandResult(-1, "");

            // Read asynchronously so a full pipe buffer cannot block the child.
            using var buffer = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(buffer);
            process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            try { process.StandardInput.Close(); } catch (IOException) { }

            if (!process.WaitForExit(timeout))
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { }
                return new CommandResult(-1, "");
            }

            // Exit means the pipe is closed (or about to be); bound the drain.
            copy.Wait(TimeSpan.FromSeconds(1));
            return new CommandResult(process.ExitCode, Decode(buffer.ToArray()));
        }
        catch (Exception)
        {
            return new CommandResult(-1, "");
        }
    }

    // WSL_UTF8=1 makes wsl.exe emit UTF-8; older builds still emit UTF-16LE (bytes contain NULs).
    private static string Decode(byte[] bytes)
        => (Array.IndexOf(bytes, (byte)0) >= 0 ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes))
            .TrimStart('\uFEFF');
}
