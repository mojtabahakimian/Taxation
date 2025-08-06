using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prg_Moadian.CNNMANAGER
{
    public class BulkSendResult
    {
        public int TotalCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount => TotalCount - SuccessCount;
        public List<string> Messages { get; } = new List<string>();
        public List<(long Number, int Tag, string Uid, string ReferenceNumber)> Results { get; } = new List<(long, int, string, string)>();
    }

}
