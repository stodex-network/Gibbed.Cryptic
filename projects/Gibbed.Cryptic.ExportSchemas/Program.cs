/* Copyright (c) 2021 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gibbed.Cryptic.ExportSchemas.Exporters;
using Gibbed.Cryptic.ExportSchemas.Runtime;
using NDesk.Options;

namespace Gibbed.Cryptic.ExportSchemas
{
    internal static class Program
    {
        private static string GetExecutablePath()
        {
            using var process = Process.GetCurrentProcess();
            var path = Path.GetFullPath(process.MainModule.FileName);
            return Path.GetFullPath(path);
        }

        public static void Main(string[] args)
        {
            string dumpPath = null;
            string projectName = null;
            string outputPath = null;
            bool showHelp = false;

            var options = new OptionSet()
            {
                { "d|dump=", "path to a memory dump file (.dmp)", v => dumpPath = v },
                {
                    "p|project=",
                    "project name (e.g. 'Star Trek Online/Live')",
                    v => projectName = v
                },
                { "o|output=", "output directory for schemas", v => outputPath = v },
                { "h|help", "show this help message", v => showHelp = v != null },
            };

            try
            {
                options.Parse(args);
            }
            catch (OptionException e)
            {
                Console.WriteLine($"Error: {e.Message}");
                Console.WriteLine("Try --help for usage information.");
                return;
            }

            if (showHelp)
            {
                Console.WriteLine("Usage: Gibbed.Cryptic.ExportSchemas [OPTIONS]");
                Console.WriteLine();
                Console.WriteLine(
                    "Export schema information from a running game client or memory dump."
                );
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine("  Export from running process:");
                Console.WriteLine("    Gibbed.Cryptic.ExportSchemas");
                Console.WriteLine();
                Console.WriteLine("  Export from dump file:");
                Console.WriteLine(
                    "    Gibbed.Cryptic.ExportSchemas -d GameClient.dmp -p \"Star Trek Online/Live\" -o ./output"
                );
                return;
            }

            if (dumpPath != null)
            {
                // Use dump file
                if (projectName == null)
                {
                    Console.WriteLine("Error: --project is required when using --dump");
                    return;
                }

                if (File.Exists(dumpPath) == false)
                {
                    Console.WriteLine($"Error: Dump file not found: {dumpPath}");
                    return;
                }

                using (var runtime = new RuntimeDump())
                {
                    if (runtime.OpenDump(dumpPath) == false)
                    {
                        Console.WriteLine("Failed to open dump file.");
                        return;
                    }

                    Console.WriteLine($"Opened dump file: {dumpPath}");
                    Console.WriteLine($"  Architecture: {(runtime.Is32Bit ? "x86" : "x64")}");
                    Console.WriteLine(
                        $"  Main module base: 0x{runtime.MainModuleBase.ToInt64():X}"
                    );
                    Console.WriteLine($"  Main module size: 0x{runtime.MainModuleSize:X}");

                    Export(runtime, projectName, outputPath);
                }
            }
            else
            {
                // Use running process
                var process = FindSuitableProcess(out projectName);
                if (process == null)
                {
                    Console.WriteLine("No suitable game process found.");
                    Console.WriteLine(
                        "Make sure a game client is running, or use --dump to analyze a dump file."
                    );
                    return;
                }

                using (var runtime = new RuntimeProcess())
                {
                    if (runtime.OpenProcess(process) == false)
                    {
                        Console.WriteLine("Failed to open process.");
                        return;
                    }

                    Export(runtime, projectName, outputPath);
                }
            }
        }

        private static void Export(RuntimeBase runtime, string projectName, string outputBasePath)
        {
            var expressionFunctionExporter = new ExpressionFunctionExporter(runtime);
            var enumExporter = new EnumExporter(runtime);
            var parseExporter = new ParseExporter(runtime);

            string outputPath;
            if (outputBasePath != null)
            {
                outputPath = Path.Combine(outputBasePath, projectName);
            }
            else
            {
                var executablePath = GetExecutablePath();
                var binPath = Path.GetDirectoryName(executablePath);
                outputPath = Path.Combine(binPath, "..", "configs", "schemas", projectName);
            }
            Directory.CreateDirectory(outputPath);

            expressionFunctionExporter.Export(outputPath);
            var enums = enumExporter.Export(outputPath);
            parseExporter.Export(outputPath, enums);
        }

        private static Process FindSuitableProcess(out string projectName)
        {
            var gameNames = new[] { "Champions Online", "Star Trek Online", "Neverwinter" };

            Process process;

            projectName = null;

            process = Process
                .GetProcessesByName("GameClient")
                .FirstOrDefault(p => Array.IndexOf(gameNames, p.MainWindowTitle) >= 0);
            if (process != null)
            {
                var path = process.MainModule.FileName;
                path = Path.GetDirectoryName(path);

                var candidate = Path.GetFileName(path);
                if (candidate == "x86" || candidate == "x64")
                {
                    path = Path.GetDirectoryName(path);
                    path = Path.GetFileName(path);
                }
                else
                {
                    path = candidate;
                }

                if (path == null)
                {
                    throw new InvalidOperationException();
                }

                projectName = Path.Combine(process.MainWindowTitle, path);
                return process;
            }

            foreach (var gameName in gameNames)
            {
                process = Process.GetProcessesByName(gameName).FirstOrDefault();
                if (process != null)
                {
                    projectName = Path.Combine(gameName, "Launcher");
                    return process;
                }
            }

            return null;
        }
    }
}
