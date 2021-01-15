using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CryEncoderComparer
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

        public static async Task<(int w, int h, string pix_fmt, double fps, double duration)> GetMetadata(string input, CancellationToken token)
        {
            // ffprobe.exe -i .\out.mp4 -hide_banner -show_streams -select_streams v:0

            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "ffprobe",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                Arguments = $"-hide_banner -i \"{input}\" -show_streams -select_streams v:0 -show_format"
            });

            p.BeginErrorReadLine();
            p.BeginOutputReadLine();
            string error = "";
            List<string> outputLines = new();
            p.ErrorDataReceived += (a, b) => error += b.Data + "\n";
            p.OutputDataReceived += (a, b) => outputLines.Add(b.Data);
            await p.WaitForExitAsync(token);
            if (p.ExitCode != 0) throw new Exception("Invalid video file! Failed to get metadata.");
            token.ThrowIfCancellationRequested();

            // parse
            try
            {
                int w = 0, h = 0;
                double fps = 0, duration = 0;
                string pix_fmt = "";
                foreach (var l in outputLines)
                {
                    if (string.IsNullOrEmpty(l)) continue;
                    var parts = l.Split('=', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length <= 1) continue;

                    var key = parts[0].ToLowerInvariant().Trim();
                    var val = parts[1];

                    if (key == "width")
                    {
                        //Console.WriteLine($"{key}={val}");
                        w = int.Parse(val);
                    }
                    else if (key == "height")
                    {
                        //Console.WriteLine($"{key}={val}");
                        h = int.Parse(val);
                    }
                    else if (key == "pix_fmt")
                    {
                        //Console.WriteLine($"{key}={val}");
                        pix_fmt = val;
                    }
                    else if (key == "avg_frame_rate" && fps == 0)
                    {
                        //Console.WriteLine($"{key}={val}");
                        var purs = val.Split('/');
                        fps = double.Parse(purs[0]) / double.Parse(purs[1]);
                    }
                    else if (key == "duration" && duration == 0)
                    {
                        //Console.WriteLine($"{key}={val}");
                        if (string.IsNullOrEmpty(val) || val.ToLowerInvariant().Trim() == "n/a")
                        {
                            // invalid duration
                            duration = 0;
                        }
                        else
                        {                     
                            duration = double.Parse(val);
                        }
                    }
                }


                return (w, h, pix_fmt, fps, duration);
            }
            catch
            {
                throw new Exception("Failed to parse video metadata!");
            }
        }
    }
}