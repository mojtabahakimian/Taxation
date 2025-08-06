using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prg_Graphicy.LMethods
{
    public class LogWriter
    {
        private static readonly ILogger logger = new LoggerConfiguration()
            .MinimumLevel.Debug() // Set the minimum log level
            .WriteTo.File($@"C:\CORRECT\LOGS\Log{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.txt", rollingInterval: RollingInterval.Day) // Specify log file path and rolling interval
            .CreateLogger();
        public static void WriteLog(string message)
        {
            logger.Information(message); // Write log message with Serilog
        }
    }
}
