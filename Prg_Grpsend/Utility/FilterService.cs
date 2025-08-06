using System;
using System.Collections.Generic;

namespace Prg_Grpsend.Utility
{
    public class FilterService<T>
    {
        private readonly List<(string ColumnName, object FilterValue, bool IsExclusion, bool IsExactMatch)> cumulativeFilters = new List<(string ColumnName, object FilterValue, bool IsExclusion, bool IsExactMatch)>();

        // Method to add a filter with an optional exclusion and exact match flag
        public void AddFilter(string columnName, object filterValue, bool isExclusion = false, bool isExactMatch = false)
        {
            cumulativeFilters.Add((columnName, filterValue, isExclusion, isExactMatch));
        }

        // Clears all filters
        public void ClearFilters()
        {
            cumulativeFilters.Clear();
        }

        // Applies filters to the given item
        public bool ApplyFilter(T item)
        {
            foreach (var (columnName, filterValue, isExclusion, isExactMatch) in cumulativeFilters)
            {
                var itemValue = GetPropValue(item, columnName)?.ToString() ?? string.Empty; // Safeguard against nulls

                if (filterValue is string stringValue)
                {
                    stringValue = stringValue.Trim();

                    // Adjust for blank values
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        if (isExclusion && string.IsNullOrWhiteSpace(itemValue)) return false;
                        else continue;
                    }

                    // Exact match handling
                    if (isExactMatch)
                    {
                        if (isExclusion)
                        {
                            if (string.Equals(itemValue, stringValue, StringComparison.OrdinalIgnoreCase)) return false;
                        }
                        else
                        {
                            if (!string.Equals(itemValue, stringValue, StringComparison.OrdinalIgnoreCase)) return false;
                        }
                    }
                    else // Contains handling
                    {
                        if (isExclusion)
                        {
                            if (itemValue.Contains(stringValue, StringComparison.OrdinalIgnoreCase)) return false;
                        }
                        else
                        {
                            if (!itemValue.Contains(stringValue, StringComparison.OrdinalIgnoreCase)) return false;
                        }
                    }
                }
                else // Non-string filters
                {
                    if (!CompareValues(itemValue, filterValue)) return false;
                }
            }

            return true;
        }

        private bool CompareValues(object dataValue, object filterValue)
        {
            if (dataValue == null || filterValue == null) return false;
            return dataValue.Equals(filterValue);
        }

        private object GetPropValue(object obj, string propName)
        {
            return obj?.GetType().GetProperty(propName)?.GetValue(obj, null);
        }
    }
}
