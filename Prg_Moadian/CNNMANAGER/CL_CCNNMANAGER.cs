using Dapper;
using Microsoft.Data.SqlClient;
using Prg_Moadian.FUNCTIONS;
using System.Data;
using static Dapper.SqlMapper;

namespace Prg_Moadian.CNNMANAGER
{
    public partial class CL_CCNNMANAGER
    {

        public CL_FUNTIONS TheFunctions = new CL_FUNTIONS();
        public static string CONNECTION_STR { get; set; }// = @"Data Source=SERVERX\SERVER2019;Initial Catalog=bazbini;Integrated Security=True;TrustServerCertificate=True;";
        private string ExtractConnectionString(string udlFilePath)
        {
            string fullConnectionString = File.ReadAllText(udlFilePath);

            // Ensure it's a single line, trim the UDL file special characters/formatting, then split.
            var keyValuePairStrings = fullConnectionString
                .Replace("\n", "")
                .Replace("\r", "")
                .Replace(" - ", "")
                .Split(';');

            var connectionStringBuilder = new SqlConnectionStringBuilder();

            // Track whether certain keys have been provided.
            bool userIdProvided = false;
            bool passwordProvided = false;
            bool integratedSecurityProvided = false;

            foreach (string keyValuePair in keyValuePairStrings)
            {
                var pair = keyValuePair.Split('=');
                if (pair.Length == 2)
                {
                    string key = pair[0].Trim();
                    string value = pair[1].Trim();

                    switch (key)
                    {
                        case "Data Source":
                            connectionStringBuilder.DataSource = value;
                            break;
                        case "Initial Catalog":
                            connectionStringBuilder.InitialCatalog = value;
                            break;
                        case "User ID":
                            userIdProvided = true;
                            connectionStringBuilder.UserID = value;
                            break;
                        case "Password":
                            passwordProvided = true;
                            connectionStringBuilder.Password = value;
                            break;
                        case "Integrated Security":
                            integratedSecurityProvided = true;
                            connectionStringBuilder.IntegratedSecurity =
                                value.Equals("SSPI", StringComparison.OrdinalIgnoreCase) ||
                                value.Equals("True", StringComparison.OrdinalIgnoreCase);
                            break;
                    }
                }
            }

            // Append "TrustServerCertificate=True;" for .NET Core compatibility.
            connectionStringBuilder.TrustServerCertificate = true;

            // If the password was not provided, and we're not using Integrated Security, set it as an empty string.
            if (!passwordProvided && userIdProvided && !integratedSecurityProvided)
            {
                connectionStringBuilder.Password = string.Empty;
            }

            return connectionStringBuilder.ConnectionString;
        }
        public CL_CCNNMANAGER()
        {
            string path0 = @"C:\correct\CNR.udl";
            string pathuspath = File.ReadLines(path0).Last();

            CONNECTION_STR = ExtractConnectionString(path0);

            using (IDbConnection db = new SqlConnection(CONNECTION_STR))
            {
                db.Open();
                var commandDefinition = new CommandDefinition("SELECT GETDATE()");
                var results = db.Query<string>(commandDefinition);
                db?.Close();
            }
            #region OLDWAY
            //if (!pathuspath.Contains("ID=")) //is Window
            //{
            //    //WindowsAuthConnectionString=Data Source = ZENVO\SQL2019;Initial Catalog = Y1401; Integrated Security = True; TrustServerCertificate=True;
            //    var DataSource = "Data Source =" + TheFunctions.GetBetweenStr(pathuspath, "Data Source =", ";") + ";";
            //    var InitialCatalog = "Initial Catalog =" + TheFunctions.GetBetweenStr(pathuspath, "Initial Catalog =", ";") + ";";
            //    var IntegratedSecurity = "Integrated Security =" + TheFunctions.GetBetweenStr(pathuspath, "Integrated Security =", ";") + ";";
            //    var FinalCNN = DataSource + InitialCatalog + IntegratedSecurity;

            //    CONNECTION_STR = FinalCNN + "TrustServerCertificate=True;";

            //}
            //else //SQL
            //{
            //    string persistSecurityInfo = ConnectionStringParser.GetPersistSecurityInfo(pathuspath);
            //    var DataSource = "Data Source =" + TheFunctions.GetBetweenStr(pathuspath, "Data Source =", ";") + ";";
            //    var InitialCatalog = "Initial Catalog =" + TheFunctions.GetBetweenStr(pathuspath, "Initial Catalog =", ";") + ";";
            //    //SQLAuthConnectionString=Data Source = ZENVO\SQL2019;Initial Catalog = Y1401; User ID = yourUsername; Password=yourPassword;
            //    string userID = ConnectionStringParser.GetUserID(pathuspath);
            //    string password = ConnectionStringParser.GetPassword(pathuspath);

            //    string FinalCNN = null;
            //    if (string.IsNullOrEmpty(password))
            //    {
            //        FinalCNN = $"Persist Security Info={persistSecurityInfo};" + DataSource + InitialCatalog + $"User ID = {userID};";
            //    }
            //    else
            //    {
            //        FinalCNN = $"Persist Security Info={persistSecurityInfo};" + DataSource + InitialCatalog + $"User ID = {userID};" + $"Password={password};";
            //    }

            //    CONNECTION_STR = FinalCNN + "TrustServerCertificate=True;";

            //    using (IDbConnection db = new SqlConnection(CONNECTION_STR))
            //    {
            //        db.Open();
            //        var commandDefinition = new CommandDefinition("SELECT GETDATE()");
            //        var results = db.Query<string>(commandDefinition);
            //        db?.Close();
            //    }
            //}
            #endregion
        }
        [System.Diagnostics.DebuggerStepThrough]
        public IEnumerable<TEntity> DoGetDataSQL<TEntity>(string sql, object parameters = null)
        {
            using (IDbConnection db = new SqlConnection(CONNECTION_STR))
            {
                try
                {
                    db.Open();
                    var results = db.Query<TEntity>(sql, parameters, commandTimeout: 3600);
                    return results;
                }
                catch (Exception er)
                {
                    try
                    {
                        File.AppendAllText("C:\\CORRECT\\DBMSLOG.txt", $"\n {DateTime.Now} \n Error in GetDataSQL :[  {sql}  ]\n" +
                                       $"{er.Message} \n {er.InnerException} \n {er.StackTrace} \n {er.Source} \n" +
                                       $"\n Method Name: {er.TargetSite.Name} \n Base Exception: {er.GetBaseException().Message} \n Exception Data: {er.Data}" +
                                       $"\n Help Link: {er.HelpLink} \n  ExceptionType: {er.GetType().FullName} \n" + $"{CL_CCNNMANAGER.CONNECTION_STR}");
                    }
                    catch { }
                    throw; // Re-throw the exception to handle it further up the call stack
                }
                finally
                {
                    db?.Close(); db?.Dispose();
                }
            }
            return null;
        }
        [System.Diagnostics.DebuggerStepThrough]
        public int? DoExecuteSQL(string sql, object parameters = null)
        {
            using (var db = new SqlConnection(CONNECTION_STR))
            {
                try
                {
                    db.Open();
                    var result = db.Execute(sql, parameters, commandTimeout: 3600);
                    return result;
                }
                catch (Exception er)
                {
                    Console.WriteLine("Error in DoExecuteSQL: " + er.Message + sql);
                    try
                    {
                        File.AppendAllText("C:\\CORRECT\\DBMSLOG.txt", $"\n {DateTime.Now}  \n Error in DoExecuteSQL :[  {sql}  ]\n" +
                                 $"{er.Message} \n {er.InnerException} \n {er.StackTrace} \n {er.Source} \n" +
                                 $"\n Method Name: {er.TargetSite.Name} \n Base Exception: {er.GetBaseException().Message} \n Exception Data: {er.Data}" +
                                 $"\n Help Link: {er.HelpLink} \n  ExceptionType: {er.GetType().FullName} \n" + $"{CL_CCNNMANAGER.CONNECTION_STR}");
                    }
                    catch { }

                    throw; // Re-throw the exception to handle it further up the call stack
                }
                finally
                {
                    db?.Close(); db?.Dispose();
                }
            }
            return null;
        }

        //Safe {↓
        public IEnumerable<TEntity> DoGetDataSQL_Safe<TEntity>(string sql, object parameters = null)
        {
            using (IDbConnection db = new SqlConnection(CONNECTION_STR))
            {
                db.Open();
                using (var transaction = db.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        var commandDefinition = new CommandDefinition(sql, parameters: parameters, commandTimeout: 300);
                        var results = db.Query<TEntity>(commandDefinition);
                        transaction.Commit();
                        db?.Close();
                        return results;
                    }
                    catch
                    {
                        transaction.Rollback();
                    }
                    finally
                    {
                        db?.Close();
                    }
                }
                return null;
            }
        }
        public int? DoExecuteSQL_Safe<TEntity>(string sql, object parameters = null)
        {
            using (IDbConnection db = new SqlConnection(CONNECTION_STR))
            {
                db.Open();
                using (var transaction = db.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        var commandDefinition = new CommandDefinition(sql, parameters: parameters, commandTimeout: 300);
                        var results = db.Query<TEntity>(commandDefinition);
                        transaction.Commit();
                        return db.Execute(sql, parameters);
                    }
                    catch
                    {
                        transaction.Rollback();
                    }
                    finally
                    {
                        db?.Close();
                    }
                }
            }
            return null;
        }
        //Safe ↑}

        //var rowsAffected = dbms.DoExecuteSQL("UPDATE MyTable SET Column1 = @value WHERE Id = @id", new { value = "NewValue", id = 1 });
        public class ConnectionStringParser
        {
            public static Dictionary<string, string> ParseConnectionString(string connectionString)
            {
                // Split the connection string into key-value pairs based on the semicolon separator
                string[] parts = connectionString.Split(';');
                var keyValuePairs = new Dictionary<string, string>();

                foreach (string part in parts)
                {
                    string[] keyValue = part.Split('=');
                    if (keyValue.Length == 2)
                    {
                        string key = keyValue[0].Trim();
                        string value = keyValue[1].Trim();
                        keyValuePairs[key] = value;
                    }
                }

                return keyValuePairs;
            }

            public static string GetPersistSecurityInfo(string connectionString)
            {
                var keyValuePairs = ParseConnectionString(connectionString);
                return keyValuePairs.ContainsKey("Persist Security Info") ? keyValuePairs["Persist Security Info"] : null;
            }

            public static string GetDataSource(string connectionString)
            {
                var keyValuePairs = ParseConnectionString(connectionString);
                return keyValuePairs.ContainsKey("Data Source") ? keyValuePairs["Data Source"] : null;
            }

            public static string GetUserID(string connectionString)
            {
                var keyValuePairs = ParseConnectionString(connectionString);
                return keyValuePairs.ContainsKey("User ID") ? keyValuePairs["User ID"] : null;
            }

            public static string GetPassword(string connectionString)
            {
                var keyValuePairs = ParseConnectionString(connectionString);
                return keyValuePairs.ContainsKey("Password") ? keyValuePairs["Password"] : null;
            }
        }
        public void ParseConnectionString(string connectionString)
        {
            var builder = new System.Data.Common.DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };
            string persistSecurityInfo = builder["Persist Security Info"].ToString();
            string dataSource = builder["Data Source"].ToString();
            string userId = builder["User ID"].ToString();
            string password = builder["Password"].ToString();
            string initialcatalog = builder["Initial Catalog"].ToString();
        }
    }
}
