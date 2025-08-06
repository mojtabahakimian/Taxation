using Dapper;
using Prg_Moadian.CNNMANAGER;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prg_Grpsend.Utility
{
    public static class Logation
    {
        public static string TruncateString(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input) || maxLength <= 0)
                return string.Empty;

            // Check if the string needs truncation
            if (input.Length > maxLength)
            {
                return input.Substring(0, maxLength);
            }

            // No truncation needed
            return input;
        }
        public static void AMALIYAT_USER(string frm)
        {
            try
            {
                using (var db = new SqlConnection(CL_CCNNMANAGER.CONNECTION_STR))
                {
                    db.Open();

                    string username = "Moadian Bulk | " + Baseknow.UUSER;
                    string _FRM_ = frm;

                    var sql = "INSERT INTO AMALIAT (USERID,USERNAME,ADATE,AMALID) VALUES (@UserId, @Username, GETDATE(), @AmalId)";
                    var parameters = new
                    {
                        UserId = Baseknow.USERCOD,
                        Username = TruncateString(username, 49),
                        AmalId = TruncateString(_FRM_, 49)
                    };
                    db.Execute(sql, parameters);
                }
            }
            catch { }
        }


    }
}
