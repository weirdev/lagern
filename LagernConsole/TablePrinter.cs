using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupConsole
{
    class TablePrinter
    {
        public string[]? Header { get; set; }
        public List<string[]> BodyRows { get; set; } = new List<string[]>();
        private int[] ColumnSizes { get; set; } = new int[0];

        public void AddHeaderRow(string[] row)
        {
            Header = row;
            UpdateColumnSizes(row);
        }

        public void AddBodyRow(string[] row)
        {
            BodyRows.Add(row);
            UpdateColumnSizes(row);
        }

        private void UpdateColumnSizes(string[] row)
        {
            int[] newsizes;
            if (row.Length > ColumnSizes.Length)
            {
                newsizes = new int[row.Length];
                for (int i = 0; i < ColumnSizes.Length; i++)
                {
                    newsizes[i] = ColumnSizes[i];
                }
                if (Header != null)
                {
                    Header = ExpandStringArray(Header, row.Length);
                }
                for (int i = 0; i < BodyRows.Count; i++)
                {
                    BodyRows[i] = ExpandStringArray(BodyRows[i], row.Length);
                }
            }
            else
            {
                newsizes = ColumnSizes;
            }
            for (int i = 0; i < row.Length; i++)
            {
                if (row[i].Length > newsizes[i])
                {
                    newsizes[i] = row[i].Length;
                }
            }
            ColumnSizes = newsizes;
        }

        private static string[] ExpandStringArray(string[] array, int length)
        {
            string[] newarray = new string[length];
            Array.Copy(array, newarray, array.Length);
            return newarray;
        }

        public override string ToString()
        {
            StringBuilder output = new StringBuilder();
            int totalwidth = -4;
            foreach (var col in ColumnSizes)
            {
                totalwidth += col + 4;
            }
            string formatstring = "";
            for (int i = 0; i < ColumnSizes.Length; i++)
            {
                formatstring += "{" + String.Format("{0},{1}", i, -ColumnSizes[i]) + "}    ";
            }
            formatstring = formatstring.Substring(0, formatstring.Length - 4);
            if (Header != null)
            {
                output.AppendLine(String.Format(formatstring, Header));
                output.AppendLine(new string('-', totalwidth));
            }
            foreach (var row in BodyRows)
            {
                output.AppendLine(String.Format(formatstring, row));
            }
            return output.ToString();
        }
    }
}
