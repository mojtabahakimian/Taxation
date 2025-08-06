using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prg_Grpsend.Utility
{
    // Enum to represent the status of the Moadian lock check
    public enum MoadianLockStatus
    {
        /// <summary>
        /// Subscription is valid.
        /// </summary>
        Valid,

        /// <summary>
        /// The provided memoryId was found, but the subscription has expired.
        /// </summary>
        SubscriptionExpired,

        /// <summary>
        /// No subscription entry was found for the given memoryId, or no entries exist at all.
        /// </summary>
        NoSubscriptionFound,

        /// <summary>
        /// The tindata string is missing, malformed, or does not contain the expected 'moadian:' tag.
        /// </summary>
        InvalidDataFormat,

        /// <summary>
        /// Configuration data (like memoryId or tindata) is missing or invalid.
        /// </summary>
        ConfigurationError,

        /// <summary>
        /// An unexpected internal error occurred during the check.
        /// </summary>
        InternalError
    }

    // Class to hold the detailed result of the MoadianLock check
    public class MoadianLockResult
    {
        public MoadianLockStatus Status { get; }
        public bool IsValid => Status == MoadianLockStatus.Valid;
        public long CurrentDateNumeric { get; }
        public long? SubscriptionExpiryDateNumeric { get; } // Expiry date for the specific memoryId if found
        public string Details { get; }

        public MoadianLockResult(MoadianLockStatus status, long currentDateNumeric, long? subscriptionExpiryDateNumeric = null, string details = null)
        {
            Status = status;
            CurrentDateNumeric = currentDateNumeric;
            SubscriptionExpiryDateNumeric = subscriptionExpiryDateNumeric;
            Details = details;
        }
    }

    public static class MoadianLocker // Assuming Baseknow is a static class, this could be part of it or separate
    {
        private const string MoadianTag = "moadian:";
        private const char DataSeparator = ',';

        // Helper to get current Persian date as long (YYYYMMDD)
        private static long GetCurrentPersianDateNumeric()
        {
            DateTime now = DateTime.Now;
            PersianCalendar pc = new PersianCalendar();
            int year = pc.GetYear(now);
            int month = pc.GetMonth(now);
            int day = pc.GetDayOfMonth(now);
            return Convert.ToInt64($"{year:D4}{month:D2}{day:D2}");
        }

        public static MoadianLockResult CheckMoadianLock() // Renamed for clarity
        {
            long currentDateNumeric = 0;
            long? matchedMemoryIdExpiryDate = null; // Stores the latest expiry date if memoryId is found but expired

            try
            {
                currentDateNumeric = GetCurrentPersianDateNumeric();

                // Take local copies for thread safety if Baseknow members can be modified externally
                string tinData = Baseknow.tindata;
                string memoryId = Baseknow.MEMORYID;

                // 1. Validate inputs
                if (string.IsNullOrWhiteSpace(memoryId))
                {
                    Debug.WriteLine("MoadianLock Error: MEMORYID is null or whitespace.");
                    return new MoadianLockResult(MoadianLockStatus.ConfigurationError, currentDateNumeric, null, "MEMORYID is not configured.");
                }
                string memoryIdToCompare = memoryId.Trim();

                if (string.IsNullOrWhiteSpace(tinData))
                {
                    Debug.WriteLine("MoadianLock Error: tindata is null or whitespace.");
                    return new MoadianLockResult(MoadianLockStatus.InvalidDataFormat, currentDateNumeric, null, "Subscription data (tindata) is missing.");
                }

                // 2. Find the start of Moadian data
                int dataStartIndex = tinData.IndexOf(MoadianTag, StringComparison.OrdinalIgnoreCase);
                if (dataStartIndex < 0)
                {
                    Debug.WriteLine($"MoadianLock Info: Tag '{MoadianTag}' not found in tindata.");
                    return new MoadianLockResult(MoadianLockStatus.InvalidDataFormat, currentDateNumeric, null, $"Tag '{MoadianTag}' not found.");
                }
                dataStartIndex += MoadianTag.Length; // Move past "moadian:"

                // If there's nothing after "moadian:", no entries to process
                if (dataStartIndex >= tinData.Length)
                {
                    Debug.WriteLine("MoadianLock Info: No data found after 'moadian:' tag.");
                    return new MoadianLockResult(MoadianLockStatus.NoSubscriptionFound, currentDateNumeric, null, "No subscription entries after tag.");
                }

                // 3. Iterate through pk,dt pairs
                int currentIndex = dataStartIndex;
                while (currentIndex < tinData.Length)
                {
                    // 3a. Pick up pk ( شناسه حافظه مالیاتی )
                    int pkEndIndex = tinData.IndexOf(DataSeparator, currentIndex);
                    if (pkEndIndex < 0)
                    {
                        // Malformed: pk without a following comma and date. This might be the end of a valid list or an error.
                        // If there's substantial content after currentIndex, it's likely an error.
                        // If currentIndex is near the end, it might just be a trailing pk.
                        // For robustness, we'll assume any non-pair is an issue if not at the very end.
                        string remaining = tinData.Substring(currentIndex).Trim();
                        if (!string.IsNullOrEmpty(remaining))
                        {
                            Debug.WriteLine($"MoadianLock Warning: Malformed entry - no comma after potential PK at index {currentIndex}. Remainder: '{remaining}'");
                        }
                        break; // Stop processing
                    }

                    string pk = tinData.Substring(currentIndex, pkEndIndex - currentIndex).Trim();

                    // 3b. Pick up dt (date)
                    currentIndex = pkEndIndex + 1; // Move past the comma to the start of dt
                    if (currentIndex >= tinData.Length) // Check if we ran past the end of the string
                    {
                        Debug.WriteLine($"MoadianLock Warning: Malformed entry - data ends abruptly after PK '{pk}'.");
                        break; // No date string follows
                    }

                    int dtEndIndex = tinData.IndexOf(DataSeparator, currentIndex);
                    if (dtEndIndex < 0) // If no more commas, dt extends to the end of the string
                    {
                        dtEndIndex = tinData.Length;
                    }

                    // Ensure Substring arguments are valid
                    if (currentIndex > tinData.Length || currentIndex > dtEndIndex)
                    {
                        Debug.WriteLine($"MoadianLock Warning: Malformed entry or parsing error near PK '{pk}'. Skipping an invalid segment.");
                        break; // Prevent ArgumentOutOfRangeException
                    }
                    string dtStr = tinData.Substring(currentIndex, dtEndIndex - currentIndex).Trim();

                    long dtVal = 0;
                    if (string.IsNullOrEmpty(dtStr) || !long.TryParse(dtStr, out dtVal))
                    {
                        // If dtStr is empty or not a valid long, dtVal remains 0 (or its last TryParse result).
                        // Treat as an invalid/expired date for this entry.
                        dtVal = 0; // Explicitly ensure it's 0 if parse fails or empty
                    }

                    // 3c. Check the condition
                    if (!string.IsNullOrEmpty(pk))
                    {
                        if (pk.Equals(memoryIdToCompare, StringComparison.OrdinalIgnoreCase))
                        {
                            if (currentDateNumeric <= dtVal)
                            {
                                // Valid subscription found
                                return new MoadianLockResult(MoadianLockStatus.Valid, currentDateNumeric, dtVal, "Subscription is active.");
                            }
                            else
                            {
                                // Matched memoryId, but it's expired. Keep track of the latest expiry for this ID.
                                if (matchedMemoryIdExpiryDate == null || dtVal > matchedMemoryIdExpiryDate.Value)
                                {
                                    matchedMemoryIdExpiryDate = dtVal;
                                }
                            }
                        }
                    }

                    // 3d. Advance to the start of the next pair (pk)
                    currentIndex = dtEndIndex + 1; // Move past the comma (or end of data for the last dt)
                }

                // 4. After loop: No currently valid subscription found for the memoryId.
                // Check if we found the memoryId but it was expired.
                if (matchedMemoryIdExpiryDate.HasValue)
                {
                    return new MoadianLockResult(MoadianLockStatus.SubscriptionExpired, currentDateNumeric, matchedMemoryIdExpiryDate, $"Subscription for {memoryIdToCompare} expired on {matchedMemoryIdExpiryDate.Value}.");
                }
                else
                {
                    return new MoadianLockResult(MoadianLockStatus.NoSubscriptionFound, currentDateNumeric, null, $"No valid subscription found for {memoryIdToCompare}.");
                }
            }
            catch (FormatException ex)
            {
                // Example: Persian date conversion failed (highly unlikely with current GetCurrentPersianDateNumeric)
                return new MoadianLockResult(MoadianLockStatus.InternalError, currentDateNumeric, null, $"Date formatting error: {ex.Message}");
            }
            catch (OverflowException ex)
            {
                // Example: Persian date conversion resulted in a number too large for long (highly unlikely)
                return new MoadianLockResult(MoadianLockStatus.InternalError, currentDateNumeric, null, $"Date numeric overflow: {ex.Message}");
            }
            catch (Exception ex) // Catch any other unexpected exceptions
            {
                // Log the full exception details for debugging
                // This is a fallback. Ideally, specific exceptions should be caught above.
                return new MoadianLockResult(MoadianLockStatus.InternalError, currentDateNumeric, matchedMemoryIdExpiryDate, $"An unexpected error occurred: {ex.Message}");
            }
        }

        // تابع کمکی برای قالب‌بندی تاریخ عددی فارسی (YYYYMMDD) به رشته YYYY/MM/DD
        public static string FormatPersianNumericDate(long? numericDate)
        {
            if (!numericDate.HasValue || numericDate.Value == 0)
            {
                return "نامشخص";
            }
            string dateStr = numericDate.Value.ToString();
            if (dateStr.Length == 8) // YYYYMMDD
            {
                return $"{dateStr.Substring(0, 4)}/{dateStr.Substring(4, 2)}/{dateStr.Substring(6, 2)}";
            }
            return dateStr; // اگر فرمت مورد انتظار نباشد، خود عدد را برمی‌گرداند
        }

        // تابع کمکی برای ترجمه وضعیت MoadianLockStatus به فارسی
        public static string TranslateMoadianStatusToPersian(MoadianLockStatus status)
        {
            switch (status)
            {
                case MoadianLockStatus.Valid:
                    return "معتبر";
                case MoadianLockStatus.SubscriptionExpired:
                    return "اشتراک منقضی شده";
                case MoadianLockStatus.NoSubscriptionFound:
                    return "اشتراک یافت نشد";
                case MoadianLockStatus.InvalidDataFormat:
                    return "فرمت داده نامعتبر";
                case MoadianLockStatus.ConfigurationError:
                    return "خطای پیکربندی";
                case MoadianLockStatus.InternalError:
                    return "خطای داخلی";
                default:
                    return "وضعیت نامشخص";
            }
        }
    }
}
