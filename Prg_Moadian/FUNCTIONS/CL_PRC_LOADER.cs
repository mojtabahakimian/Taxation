using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace Prg_Moadian.FUNCTIONS
{
    public static class CL_PRC_LOADER
    {
        private static readonly object _lock = new object();
        private static readonly ConcurrentDictionary<int, Process> _runningProcesses = new ConcurrentDictionary<int, Process>();
        private static readonly List<string> _tempExePaths = new List<string>();

        public static Process Start(string resourceName = null, string arguments = null)
        {
            resourceName = "Prg_Graphicy.ADDON.Preloader.exe";
            arguments = "1354";

            using (Stream stream = Assembly.GetEntryAssembly().GetManifestResourceStream($"Prg_Graphicy.ADDON.Preloader.exe"))
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
                        // Handle the exception as needed
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
        #region OLDVERSION
        //private static string THE_PRC_PATH { get; set; } = "C:\\correct\\prc\\";
        //public static string FULL_PRC_PATH { get; set; } = THE_PRC_PATH + "prc.exe";
        //public static List<Process>? THE_PRC_LST { get; set; }
        //public static void DIR_FILE_EXISTANCE()
        //{
        //    if (!Directory.Exists(THE_PRC_PATH)) Directory.CreateDirectory(THE_PRC_PATH);

        //    var assembly = Assembly.GetEntryAssembly().GetManifestResourceStream("Prg_Graphicy.ADDON.Preloader.exe");
        //    using var stream = assembly;
        //    using var memStream = new MemoryStream();
        //    stream.CopyTo(memStream);
        //    var exeBytes = memStream.ToArray();
        //    File.WriteAllBytes(FULL_PRC_PATH, exeBytes);
        //}
        //public static void KILLALLPRCS()
        //{
        //    foreach (var process in THE_PRC_LST)
        //    {
        //        if (!process.HasExited)
        //        {
        //            process?.Kill();
        //        }
        //    }
        //}
        //public static void STARTPRCTIMES(byte _ttt, bool _isRandomy = false)
        //{
        //    THE_PRC_LST = new List<Process>();
        //    for (int i = 1; i <= _ttt; i++)
        //    {
        //        if (_isRandomy)
        //        {
        //            Random random = new Random();
        //            Thread.Sleep(random.Next(50, 400));
        //        }
        //        var process = Process.Start(FULL_PRC_PATH, "1354");
        //        THE_PRC_LST.Add(process);
        //    }
        //}
        #endregion
    }
}
