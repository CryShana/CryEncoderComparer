using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using static System.Console;

namespace CryEncoderComparer
{
    class Program
    {
        const string tempDirectory = "temp";
        const string reference = "r.yuv";
        const string encoded = "e.temp";
        const string distorted = "d.yuv";
        const string vmafresult = "vmaf.json";
        static CancellationTokenSource csc;

        static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                ConsoleHelpers.ShowHelp();
                return;
            }

            try
            {
                // parse input
                var (input, from, to, presets, threads) = ConsoleHelpers.ParseArguments(args);

                // check if ffmpeg is accessible
                try
                {
                    //_ = FFmpegWrapper.GetFormats();
                }
                catch
                {
                    ConsoleHelpers.WriteLine($"\nERROR! FFmpeg not found! Make sure it's accessible in PATH or in same directory", ConsoleColor.Red);
                    return;
                }

                // handle proper cancelling
                csc = new();
                CancelKeyPress += (a, b) =>
                {
                    b.Cancel = true;
                    csc.Cancel();
                };

                // validate all presets
                foreach (var p in presets)
                {
                    try
                    {
                        EnsureValidPreset(p, out _);
                    }
                    catch (Exception ex)
                    {
                        ConsoleHelpers.WriteLine($"\nINVALID PRESET! {ex.Message} ({p})", ConsoleColor.Red);
                        return;
                    }
                }

                // start main program
                await Start(input, from, to, presets, threads);
            }
            catch (Exception ex)
            {
                ConsoleHelpers.WriteLine($"\nERROR! {ex.Message}", ConsoleColor.Red);
            }
        }

        static async Task Start(string input, string from, string to, string[] presets, int threads)
        {
            CheckForCursor();

            // create temp directory
            Directory.CreateDirectory(tempDirectory);

            // get new file paths inside temp directory
            var filename = Path.GetFileName(input);
            var path_ref = Path.Combine(tempDirectory, reference);
            var path_enc = Path.Combine(tempDirectory, encoded);
            var path_dist = Path.Combine(tempDirectory, distorted);
            var path_vmaf = Path.Combine(tempDirectory, vmafresult);

            // get reference video metadata
            var (w, h, bit, fps, chroma, pix_fmt, duration) = await ReadVideoData(input);
            var fromSec = ParseTimeToSeconds(from);
            var toSec = string.IsNullOrEmpty(to) ? duration : ParseTimeToSeconds(to);
            if (toSec >= 0 && toSec < fromSec) throw new Exception("TO timestamp can not be set before FROM!");
            current_duration = toSec - fromSec;

            WriteLine("Loaded reference clip:");
            ConsoleHelpers.WriteLineMultipart(("Reference:      ", ConsoleColor.Gray), (filename, ConsoleColor.Cyan));
            ConsoleHelpers.WriteLineMultipart(("Width:          ", ConsoleColor.Gray), (w.ToString(), ConsoleColor.DarkCyan));
            ConsoleHelpers.WriteLineMultipart(("Height:         ", ConsoleColor.Gray), (h.ToString(), ConsoleColor.DarkCyan));
            ConsoleHelpers.WriteLineMultipart(("Pixel Format:   ", ConsoleColor.Gray), (pix_fmt, ConsoleColor.DarkCyan));
            ConsoleHelpers.WriteLineMultipart(("Framerate:      ", ConsoleColor.Gray), (fps.ToString(), ConsoleColor.DarkCyan));
            ConsoleHelpers.WriteLineMultipart(("Duration:       ", ConsoleColor.Gray), (duration.ToString() + "sec", ConsoleColor.DarkCyan));

            // start process
            Process ffmpeg = null, vmaf = null;
            try
            {
                var vmafpath = GetVMAFExecutablePath();
                if (!File.Exists(vmafpath)) throw new FileNotFoundException("VMAF executable not found!");

                // convert original to raw .yuv
                ConsoleHelpers.WriteMultipart(("\nConverting to raw YUV format (Duration: ", ForegroundColor), ($"{current_duration}sec", ConsoleColor.Cyan), (")", ForegroundColor));

                ffmpeg = FFmpegHelpers.Run($"-i \"{input}\" -ss {fromSec} {(toSec <= 0 ? "" : $"-to {toSec}")} -an -f rawvideo {path_ref} -y", null, onProgressChanged);
                ffmpeg.StartInfo.RedirectStandardOutput = true;
                await ffmpeg.WaitForExitAsync(csc.Token);
                if (csc.IsCancellationRequested) throw new TaskCanceledException();
                if (ffmpeg.ExitCode != 0) throw new Exception("FFmpeg failed to convert original video to raw YUV format!");
                WriteLine();

                // for each preset do the following:
                foreach (var p in presets)
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        EnsureValidPreset(p, out string format);

                        // encode video normally
                        ConsoleHelpers.WriteMultipart(("\nEncoding using '", ForegroundColor), (p, ConsoleColor.Magenta), ("'", ForegroundColor));

                        var sw2 = Stopwatch.StartNew();
                        ffmpeg = FFmpegHelpers.Run($"-f rawvideo -framerate {fps} -pixel_format {pix_fmt} -video_size {w}x{h} -i \"{path_ref}\" {p} -an {path_enc} -y", null, onProgressChanged);
                        await ffmpeg.WaitForExitAsync(csc.Token);
                        if (csc.IsCancellationRequested) throw new TaskCanceledException();
                        if (ffmpeg.ExitCode != 0) throw new ApplicationException("FFmpeg failed to encode video with preset: " + p);
                        sw2.Stop();
                        WriteLine();

                        ConsoleHelpers.WriteLineMultipart(
                            ("  - Encoding time: ", ForegroundColor), 
                            ($"{sw2.Elapsed.TotalSeconds} sec", ConsoleColor.DarkGray));

                        var size = new FileInfo(path_enc).Length / 1_000_000.0;    
                        ConsoleHelpers.WriteLineMultipart(
                            ("  - Encoded size: ", ForegroundColor), 
                            ($"{size:0.000} MB", ConsoleColor.DarkGray));             

                        // now convert encoded video to raw .yuv
                        Write("  - Converting encoded clip to raw YUV format");

                        ffmpeg = FFmpegHelpers.Run($"-f {format} -i \"{path_enc}\" -f rawvideo {path_dist} -y", null, onProgressChanged);
                        await ffmpeg.WaitForExitAsync(csc.Token);
                        if (csc.IsCancellationRequested) throw new TaskCanceledException();
                        if (ffmpeg.ExitCode != 0) throw new ApplicationException("FFmpeg failed to convert encoded video to raw YUV format!");
                        WriteLine();


                        Write("  - Calculating VMAF score ");
                        // run VMAF and compare original and distorted YUV videos
                        var pinfo = new ProcessStartInfo
                        {
                            FileName = vmafpath,
                            UseShellExecute = false,
                            RedirectStandardError = true,
                            RedirectStandardOutput = true,
                            CreateNoWindow = false,
                            Arguments = $"-r {path_ref} -d {path_dist} -w {w} -h {h} -b {bit} -p {chroma} --threads {threads} --json -o {path_vmaf}"
                        };

                        vmaf = new Process { StartInfo = pinfo };
                        vmaf.Start();
                        //handleVMAFOutput(vmaf);
                        await vmaf.WaitForExitAsync(csc.Token);
                        if (csc.IsCancellationRequested) throw new TaskCanceledException();
                        if (vmaf.ExitCode != 0) throw new ApplicationException("VMAF failed to compare videos");

                        // parse VMAF score
                        var (min, max, mean, hmean) = GetVMAFScore(path_vmaf);
                        WriteLine();
                        ConsoleHelpers.WriteLine($"     - VMAF range: {min} - {max}", ConsoleColor.DarkGray);
                        ConsoleHelpers.WriteLine($"     - VMAF mean: {mean}", ConsoleColor.Gray);
                        ConsoleHelpers.WriteLine($"     - VMAF harmonized mean: {hmean}", ConsoleColor.Green);
                    }
                    catch (TaskCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ConsoleHelpers.WriteLine($"\nERROR! {ex.Message}", ConsoleColor.Red);
                        continue;
                    }
                    finally
                    {
                        sw.Stop();
                        ConsoleHelpers.WriteLine($"  - Elapsed: {sw.Elapsed.TotalSeconds} sec", ConsoleColor.DarkGray);
                    }
                }

            }
            catch (TaskCanceledException)
            {
                ConsoleHelpers.WriteLine($"\nProcess cancelled", ConsoleColor.Red);
            }
            catch (Exception ex)
            {
                ConsoleHelpers.WriteLine($"\nERROR! {ex.Message}", ConsoleColor.Red);
            }
            finally
            {
                if (vmaf?.HasExited == false) vmaf.Kill();
                if (ffmpeg?.HasExited == false) ffmpeg.Kill();

                // wait a bit because we most likely force cancelled everything
                if (csc.IsCancellationRequested) await Task.Delay(200);

                try
                {
                    if (File.Exists(path_enc)) File.Delete(path_enc);
                    if (File.Exists(path_ref)) File.Delete(path_ref);
                    if (File.Exists(path_dist)) File.Delete(path_dist);
                    if (File.Exists(path_vmaf)) File.Delete(path_vmaf);
                    if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory);
                }
                catch
                {
                    ConsoleHelpers.WriteLine($"\nERROR! Failed to delete some temporary files", ConsoleColor.Red);
                }
            }

            WriteLine();
        }

        static async Task<(int width, int height, int bitDepth, double fps, string chroma, string pixelFormat, double duration)> ReadVideoData(string input)
        {
            var (w, h, pix_fmt, fps, duration) = await FFmpegHelpers.GetMetadata(input, csc.Token);

            var bit = 8;
            var chroma = "420";
            if (pix_fmt.Contains("420", StringComparison.Ordinal)) chroma = "420";
            else if (pix_fmt.Contains("422", StringComparison.Ordinal)) chroma = "422";
            else if (pix_fmt.Contains("444", StringComparison.Ordinal)) chroma = "444";

            if (pix_fmt.Contains("p10", StringComparison.Ordinal)) bit = 10;
            else if (pix_fmt.Contains("p12", StringComparison.Ordinal)) bit = 12;

            return (w, h, bit, fps, chroma, pix_fmt, duration);
        }

        static double ParseTimeToSeconds(string time)
        {
            var parts = time.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return double.Parse(parts[0]);
            else if (parts.Length == 2) return int.Parse(parts[0]) * 60 + double.Parse(parts[1]);
            else if (parts.Length == 3) return int.Parse(parts[0]) * 60 + int.Parse(parts[1]) * 60 + double.Parse(parts[2]);
            else throw new FormatException("Time given in invalid format!");
        }

        static string GetVMAFExecutablePath()
        {
            const string linuxPath = "tools/linux/vmaf";
            const string winPath = "tools/win/vmaf.exe";

            if (OperatingSystem.IsLinux()) return linuxPath;
            else if (OperatingSystem.IsWindows()) return winPath;
            else throw new InvalidProgramException("Unsupported operating system!");
        }

        static void EnsureValidPreset(string preset, out string format)
        {
            // must contain `-c:v` or `-codec:v` or `-vcodec`
            // must contain `-f`
            // must not contain `-i`

            var parts = preset.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Contains("-i")) throw new Exception("Preset can not contain an input file argument! (-i)");
            if (!parts.Contains("-f")) throw new Exception("Preset must specify output format! (-f)");

            if (!parts.Contains("-c:v") &&
                !parts.Contains("-codec:v") &&
                !parts.Contains("-vcodec"))
            {
                throw new Exception("Preset must specify output video codec! (-c:v)");
            }

            // get format
            var findex = Array.FindIndex(parts, 0, x => x == "-f");
            if (findex == parts.Length - 1 || string.IsNullOrEmpty(parts[findex + 1])) throw new Exception("Preset must contain a valid format name!");

            format = parts[findex + 1];
        }

        static (double min, double max, double mean, double harmonic_mean) GetVMAFScore(string jsonFile)
        {
            var json = File.ReadAllText(jsonFile);

            var doc = JsonSerializer.Deserialize<JsonDocument>(json);

            var vmaf = doc.RootElement.GetProperty("pooled_metrics").GetProperty("vmaf");
            var min = vmaf.GetProperty("min").GetDouble();
            var max = vmaf.GetProperty("max").GetDouble();
            var mean = vmaf.GetProperty("mean").GetDouble();
            var hmean = vmaf.GetProperty("harmonic_mean").GetDouble();

            return (min, max, mean, hmean);
        }


        #region ProgressHandling
        // PROGRESS VARIABLES
        static double current_duration = 0.0;
        static int progress_lastFrame = 0;
        static double progress_lastFps = 0.0;
        static double progress_lastTime = 0.0;
        static string progress_speed = "0x";
        static int LastCursorLeft = 0;
        static int LastCursorTop = 0;
        static bool cursorAvailable = true;

        static void CheckForCursor()
        {
            try
            {
                _ = CursorTop;
                _ = CursorLeft;
                cursorAvailable = true;
            }
            catch
            {
                cursorAvailable = false;
            }
        }

        static void onProgressChanged(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            var parts = message.Split('=');
            if (parts.Length == 0) return;

            try
            {
                switch (parts[0])
                {
                    case "frame":
                        progress_lastFrame = int.Parse(parts[1]);
                        break;
                    case "fps":
                        progress_lastFps = double.Parse(parts[1]);
                        break;
                    case "out_time_us":
                        progress_lastTime = (int.Parse(parts[1]) / 1000.0) / 1000.0;
                        break;
                    case "speed":
                        progress_speed = parts[1];
                        WriteCurrentProgress();
                        break;
                }
            }
            catch (Exception ex)
            {
                WriteLine("PROGRESS PARSE ERROR! {0}", ex.Message);
            }
        }

        static void handleVMAFOutput(Process p)
        {
            // TODO: for some reason can not read standard output unless NEW LINE is received (VMAF never sends a new line!)
            throw new NotImplementedException();
/*
            var readChars = new List<char>();

            var reader = p.StandardError;

            while (true)
            {
                var r = reader.Read();
                if (r == -1) break;
                readChars.Add((char)r);
            }*/
        }

        static void WriteCurrentProgress()
        {
            if (cursorAvailable)
            {
                LastCursorLeft = CursorLeft;
                LastCursorTop = CursorTop;
            }

            // this is the last key of progress update, update console output here
            var progress = (progress_lastTime / current_duration) * 100;
            if (progress > 99) progress = 100;
            if (progress < 0) progress = 0;

            string msg = $" [{progress:0.00}%] (Speed: {progress_speed})                ";
            if (cursorAvailable)
            {
                Write(msg);
                CursorLeft = LastCursorLeft;
                CursorTop = LastCursorTop;
            }
            else
            {
                WriteLine(msg);
            }
        }
        #endregion
    }
}
