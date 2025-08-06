using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Prg_TrackSentInvoice.ADDON
{
    public static class CL_INDICATOR
    {
        private static readonly object _lock = new object();
        private static readonly ConcurrentDictionary<int, Process> _runningProcesses = new ConcurrentDictionary<int, Process>();
        private static readonly List<string> _tempExePaths = new List<string>();

        public static Process Start(string resourceName = null, string arguments = null)
        {
            resourceName = "Prg_TrackSentInvoice.ADDON.Preloader.exe";
            arguments = "1354";

            using (Stream stream = Assembly.GetEntryAssembly().GetManifestResourceStream($"{resourceName}"))
            {
                byte[] exeBytes = new byte[stream.Length];
                stream.Read(exeBytes, 0, exeBytes.Length);

                // Save the byte array to a temporary file
                string tempExePath = Path.GetTempFileName() + ".exe";
                _tempExePaths.Add(tempExePath); // Store the temp path for later cleanup

                File.WriteAllBytes(tempExePath, exeBytes);

                // Start the process from the byte array with arguments
                var process = new Process
                {
                    StartInfo =
                            {
                                FileName = tempExePath,
                                Arguments = arguments,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                };
                process.Start();

                // Store the process in the dictionary
                _runningProcesses.TryAdd(process.Id, process);

                return process;
            }
        }

        public static void Stop(Process process)
        {
            if (process == null)
            {
                return;
            }
            if (_runningProcesses.TryRemove(process.Id, out var removedProcess))
            {
                removedProcess.Kill();
                removedProcess.WaitForExit();
                removedProcess.Dispose();

                // Clean up temporary file
                string tempExePath = _tempExePaths.FirstOrDefault(p => string.Equals(p, removedProcess.StartInfo.FileName, StringComparison.OrdinalIgnoreCase));
                if (tempExePath != null)
                {
                    try
                    {
                        File.Delete(tempExePath);
                        _tempExePaths.Remove(tempExePath);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        public static void Dispose()
        {
            foreach (var process in _runningProcesses.Values)
            {
                Stop(process);
            }
        }
    }
}
