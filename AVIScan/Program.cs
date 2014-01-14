using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;

namespace AVIScan
{
    class Program
    {
        static string ScanFolder;

        /// <summary>
        /// Maximum time to allow FFMPEG process to run (sometimes hangs up on badly broken files)
        /// </summary>
        static int Timeout = 10;

        /// <summary>
        /// Some basic stats
        /// </summary>
        static int Successes = 0;
        static int Fails = 0;

        /// <summary>
        /// StreamWriter that forms the basis of a simple logger
        /// </summary>
        static StreamWriter LogWriter = new StreamWriter("log-" + DateTime.Now.ToString("dd-MM-yyyy-hh-mm-ss") + ".txt", false);

        static void Main(string[] args)
        {
            // hook processExit to clean up log writer
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { if (LogWriter != null) { LogWriter.Close(); } };

            if (args.Length < 1)
            {
                die("Specify the folder you want to scan as the argument");
            }

            ScanFolder = args[0];

            if (!Directory.Exists(ScanFolder))
            {
                die("Specified scan directory does not exist: " + ScanFolder);
            }

            PrintLine("Starting scan of AVI files within '" + ScanFolder + "'" + Environment.NewLine);

            BeginScan(ScanFolder);

            if (Debugger.IsAttached)
            {
                PrintLine("Done! Press any key to exit...");
                Console.ReadKey();
            }
            else
            {
                PrintLine("Done!");
            }
        }

        /// <summary>
        /// Function to print to the screen & write to the log
        /// </summary>
        /// <param name="text">Text to print</param>
        static void Print(string text)
        {
            Console.Write(text);
            LogWriter.Write(text);
            LogWriter.Flush();
        }

        /// <summary>
        /// Function to print a line to the screen & write to the log
        /// </summary>
        /// <param name="text">Text to print</param>
        static void PrintLine(string text)
        {
            Console.WriteLine(text);
            LogWriter.WriteLine(text);
            LogWriter.Flush();
        }

        /// <summary>
        /// Begins the scan operation on a particular folder. Recursively checks all sub-folders
        /// </summary>
        /// <param name="scanFolder">Folder to scan (required for recursion)</param>
        static void BeginScan(string scanFolder)
        {
            // clear counts
            Successes = 0;
            Fails = 0;

            // only print if we have files
            string[] files = Directory.GetFiles(scanFolder, "*.avi");
            if (files.Length > 0)
            {
                PrintLine("[" + scanFolder + "]");
                foreach (string file in files)
                {
                    ScanFile(file);
                }
                // write totals
                PrintLine("[Totals: " + Successes + " success(es), " + Fails + " failure(s)]" + Environment.NewLine);
            }

            // recurse into subdirectories
            string[] directories = Directory.GetDirectories(scanFolder);
            foreach (string directory in directories)
            {
                BeginScan(directory);
            }
        }

        /// <summary>
        /// Begins the scan operation on a particular file. Handles printing the result to the screen
        /// </summary>
        /// <param name="file">Path of the file to scan</param>
        static void ScanFile(string file)
        {
            Print(file.Replace(ScanFolder, "") + " -- ");
            if (_testFile(file))
            {
                Successes++;
                Console.ForegroundColor = ConsoleColor.Green;
                PrintLine("OK");
                Console.ResetColor();
            }
            else
            {
                Fails++;
                Console.ForegroundColor = ConsoleColor.Red;
                PrintLine("FAIL");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Tests the specified file using FFMPEG to determine whether it is a valid AVI file
        /// </summary>
        /// <param name="file">Path of the file to test</param>
        /// <returns></returns>
        static bool _testFile(string file)
        {
            using (Process p = new Process())
            {
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.FileName = "ffmpeg.exe";
                p.StartInfo.Arguments = "-i \"" + file + "\" -v error -c copy -f null -";

                StringBuilder errorSB = new StringBuilder();
                p.ErrorDataReceived += (sender, e) =>
                {
                    errorSB.AppendLine(e.Data);
                };

                p.Start();
                p.BeginErrorReadLine();

                if (!p.WaitForExit(Timeout * 1000))
                {
                    p.Kill();
                }

                string error = errorSB.ToString();
                if (!String.IsNullOrWhiteSpace(error))
                {
                    // if we have an err * 1000ng, we might just have some warnings we dont care about

                    string[] lines = error.Replace("\r\n", "\n").Split('\n');
                    List<string> validErrors = new List<string>();

                    foreach (string line in lines)
                    {
                        // idea is to continue past any lines we dont want

                        if (String.IsNullOrWhiteSpace(line)) continue;

                        if (line.Contains("Application provided invalid, non monotonically increasing dts to muxer") ||
                            line.Contains("Last message repeated"))
                        {
                            continue;
                        }

                        // only consider "header missing" an error if the hex location specified is 0 (start of file)
                        // the ones that are partway in seem to play alright
                        Regex r = new Regex(@"@ ([a-z\d]+)] header missing", RegexOptions.IgnoreCase);
                        Match m = r.Match(line);
                        if (m.Success)
                        {
                            int location = Convert.ToInt32(m.Groups[1].Captures[0].Value, 16);
                            if (location > 0)
                            {
                                continue;
                            }
                        }

                        validErrors.Add(line);
                    }

                    if (validErrors.Count > 0)
                    {
                        if (Debugger.IsAttached)
                        {
                            // while testing, print any errors we come across for double-checking later
                            // PrintLine(String.Join(", ", validErrors));
                        }
                        return false;
                    }
                }

                // if the error string is empty, there was no error. easy!
                return true;
            }
        }

        static void die(string message)
        {
            PrintLine(message);
            Environment.Exit(-1);
        }
    }
}
