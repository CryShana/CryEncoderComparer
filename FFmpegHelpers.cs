using System;
using System.Diagnostics;

namespace cryVideoComparer
{
    public static class FFmpegHelpers
    {
        public static Process Run(string command, Action<string> onErrorReceived = null, Action<string> onProgressChanged = null)
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                Arguments = $"-hide_banner -progress pipe:1 {command}"
            });

            p.BeginErrorReadLine();
            p.BeginOutputReadLine();
            if (onErrorReceived != null) p.ErrorDataReceived += (a, b) => onErrorReceived(b.Data);
            if (onProgressChanged != null) p.OutputDataReceived += (a, b) => onProgressChanged(b.Data);

            return p;
        }
    }
}