using System;
using System.IO;
using System.Collections.Generic;

namespace CryEncoderComparer
{
    public static class ConsoleHelpers
    {
        public static void Write(string message, ConsoleColor color)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(message);
            Console.ForegroundColor = old;
        }

        public static void WriteLine(string message, ConsoleColor color) => Write(message + "\n", color);

        public static void WriteMultipart(params (string message, ConsoleColor color)[] messages)
        {
            foreach (var m in messages) Write(m.message, m.color);
        }

        public static void WriteLineMultipart(params (string message, ConsoleColor color)[] messages)
        {
            foreach (var m in messages) Write(m.message, m.color);
            Console.WriteLine();
        }

        public static void ShowHelp()
        {
            var exe = "CryEncoderComparer";

            Console.WriteLine(
                $"Tool for comparing different ffmpeg video encoders based on a given reference clip.\n\n" +
                $"Usage: ./{exe} [options] <REFERENCE CLIP> [options]\n\n" +
                $"Options:\n" +
                $"   --from <timestamp>      Select the FROM timestamp for reference clip. (example: 52.3)\n" +
                $"   --to <timestamp>        Select the TO timestamp for reference clip. (example: 01:10)\n" +
                $"   --threads <threads>     Number of threads for calculating VMAF. (default: 8)\n" +
                $"   --presets <filename>    Select file that contains all presets you want to compare, separated by lines\n");
        }

        public static (string input, string from, string to, string[] presets, int threads) ParseArguments(string[] args)
        {
            var input = @"";
            var from = "00:00";
            string to = null;
            var presetsFile = "";

            var threads = 8;

            // check which flags/options are present and mark the indexes of their values
            var optionIndexes = new List<int>();
            int fromIndex = -1, toIndex = -1, threadsIndex = -1, presetsIndex = -1;
            for (int i = 0; i < args.Length; i++)
            {
                var v = args[i].ToLowerInvariant().Trim();
                switch (v)
                {
                    // TODO: tidy up and remove duplicated code
                    case "-f":
                    case "--from":
                        if (fromIndex != -1) throw new InvalidDataException("FROM timestamp already defined!");
                        if (optionIndexes.Contains(i + 1) || i + 1 >= args.Length) throw new InvalidDataException("Some options are missing values!");
                        fromIndex = i + 1;
                        optionIndexes.Add(i);
                        optionIndexes.Add(i + 1);
                        break;
                    case "-t":
                    case "--to":
                        if (toIndex != -1) throw new InvalidDataException("TO timestamp already defined!");
                        if (optionIndexes.Contains(i + 1) || i + 1 >= args.Length) throw new InvalidDataException("Some options are missing values!");
                        toIndex = i + 1;
                        optionIndexes.Add(i);
                        optionIndexes.Add(i + 1);
                        break;
                    case "--threads":
                        if (threadsIndex != -1) throw new InvalidDataException("THREADS option already defined!");
                        if (optionIndexes.Contains(i + 1) || i + 1 >= args.Length) throw new InvalidDataException("Some options are missing values!");
                        threadsIndex = i + 1;
                        optionIndexes.Add(i);
                        optionIndexes.Add(i + 1);
                        break;
                    case "-p":
                    case "--presets":
                        if (presetsIndex != -1) throw new InvalidDataException("PRESETS option already defined!");
                        if (optionIndexes.Contains(i + 1) || i + 1 >= args.Length) throw new InvalidDataException("Some options are missing values!");
                        presetsIndex = i + 1;
                        optionIndexes.Add(i);
                        optionIndexes.Add(i + 1);
                        break;
                }
            }

            if (fromIndex != -1) from = args[fromIndex];
            if (toIndex != -1) to = args[toIndex];
            if (threadsIndex != -1)
                if (int.TryParse(args[threadsIndex], out threads) == false)
                    threads = 8;

            if (presetsIndex != -1) presetsFile = args[presetsIndex];
            else throw new InvalidDataException("No PRESETS file has been set!");
            var presets = File.ReadAllLines(presetsFile);

            // the input reference file is whichever file has no option beforehand
            for (int i = 0; i < args.Length; i++)
            {
                if (optionIndexes.Contains(i)) continue;

                input = args[i];
                break;
            }

            return (input, from, to, presets, threads);
        }
    }
}