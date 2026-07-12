using AI_Data_Analyzer.Models;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using System.Globalization;

namespace AI_Data_Analyzer.Controllers
{
    public class DatasetsController : Controller
    {
        [HttpGet]
        public IActionResult Upload() => View();

        private static char DetectDelimiter(string headerLine)
        {
            char[] candidates = new[] { ',', ';', '\t', '|' };

            return candidates
                .Select(d => new { d, count = headerLine.Count(c => c == d) })
                .OrderByDescending(x => x.count)
                .First().d;
        }

        /// <summary>
        /// Parses CSV content from a stream into rows of cells, correctly handling
        /// quoted fields (e.g. "Smith, John") and fields containing embedded newlines.
        /// </summary>
        private static List<string[]> ParseCsv(Stream stream, char delimiter)
        {
            var rows = new List<string[]>();

            using var parser = new TextFieldParser(stream)
            {
                TextFieldType = FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true,
                TrimWhiteSpace = false
            };
            parser.SetDelimiters(delimiter.ToString());

            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields();
                if (fields == null) continue;

                for (int i = 0; i < fields.Length; i++)
                    fields[i] = fields[i].Trim();

                rows.Add(fields);
            }

            return rows;
        }

        /// <summary>
        /// Reads the first worksheet of an .xlsx stream into rows of cells,
        /// producing the same shape as ParseCsv so all downstream logic is shared.
        /// </summary>
        private static List<string[]> ParseXlsx(Stream stream)
        {
            var rows = new List<string[]>();

            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet == null) return rows;

            // RangeUsed() gives the smallest block that actually contains data,
            // ignoring trailing empty rows/columns.
            var range = sheet.RangeUsed();
            if (range == null) return rows;

            int colCount = range.ColumnCount();

            foreach (var row in range.Rows())
            {
                var cells = new string[colCount];
                for (int c = 1; c <= colCount; c++)
                {
                    // GetString() returns the displayed text; cached for formulas.
                    cells[c - 1] = row.Cell(c).GetString().Trim();
                }
                rows.Add(cells);
            }

            return rows;
        }

        /// <summary>
        /// Heuristically checks whether parsed rows look like a single, clean table.
        /// Returns a human-readable reason when the layout looks unsupported
        /// (e.g. multiple side-by-side blocks, grouped/multi-row headers, mostly-empty sheets),
        /// or null when the data looks fine to profile.
        /// </summary>
        private static string? DetectUnsupportedLayout(string[] headers, List<string[]> dataRows)
        {
            if (headers.Length == 0)
                return "The file doesn't have a usable header row.";

            // 1) Too many blank header cells -> likely a grouped/multi-row header
            //    (e.g. group labels on row 1 with most cells empty).
            int blankHeaders = headers.Count(string.IsNullOrWhiteSpace);
            if (headers.Length >= 3 && blankHeaders >= headers.Length / 2.0)
                return "The header row is mostly empty. This usually means the file uses grouped or multi-row headers, which aren't supported yet. Please upload a file with a single header row.";

            // 2) Duplicate header names -> usually several tables placed side by side.
            var nonEmpty = headers.Where(h => !string.IsNullOrWhiteSpace(h))
                                  .Select(h => h.Trim().ToLowerInvariant())
                                  .ToList();
            int distinct = nonEmpty.Distinct().Count();
            if (nonEmpty.Count - distinct >= 2)
                return "Several columns share the same name, which usually means the sheet contains multiple tables placed side by side. Please upload a single table per file.";

            // 3) Mostly-empty data -> placeholder/report template rather than a dataset.
            if (dataRows.Count > 0)
            {
                long totalCells = 0, emptyCells = 0;
                foreach (var row in dataRows)
                {
                    for (int c = 0; c < headers.Length; c++)
                    {
                        totalCells++;
                        if (c >= row.Length || string.IsNullOrWhiteSpace(row[c]))
                            emptyCells++;
                    }
                }
                if (totalCells > 0 && (double)emptyCells / totalCells >= 0.8)
                    return "Most cells in this file are empty. It looks like a template or report layout rather than a simple data table. Please upload a file where each row is a complete record.";
            }

            return null;
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("", "Please select a CSV or Excel file.");
                return View();
            }

            var ext = Path.GetExtension(file.FileName);
            bool isCsv = string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase);
            bool isXlsx = string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase);

            if (!isCsv && !isXlsx)
            {
                ModelState.AddModelError("", "Please upload a .csv or .xlsx file.");
                return View();
            }

            // Read the upload into memory once; we never persist it to disk.
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);

            // Parse into a common shape: rows of cells. Delimiter only applies to CSV.
            char delimiter = ',';
            List<string[]> allRows;
            try
            {
                if (isCsv)
                {
                    // Peek the first line for delimiter detection, then rewind to parse.
                    ms.Position = 0;
                    string? firstLine;
                    using (var peek = new StreamReader(ms, leaveOpen: true))
                    {
                        firstLine = await peek.ReadLineAsync();
                    }

                    if (string.IsNullOrEmpty(firstLine))
                    {
                        ModelState.AddModelError("", "CSV is empty.");
                        return View();
                    }
                    delimiter = DetectDelimiter(firstLine);

                    ms.Position = 0;
                    allRows = ParseCsv(ms, delimiter);
                }
                else
                {
                    ms.Position = 0;
                    allRows = ParseXlsx(ms);
                }
            }
            catch (Exception)
            {
                ModelState.AddModelError("", "Could not parse the file. Please check its format.");
                return View();
            }

            if (allRows.Count == 0)
            {
                ModelState.AddModelError("", "The file has no data.");
                return View();
            }

            // Header + data rows
            var headers = allRows[0];
            var dataRows = allRows.Skip(1).ToList();

            // Reject layouts we can't profile meaningfully (multi-table, grouped headers, templates).
            var layoutProblem = DetectUnsupportedLayout(headers, dataRows);
            if (layoutProblem != null)
            {
                // Surfaced as a popup by the Upload view.
                TempData["UploadError"] = layoutProblem;
                ModelState.AddModelError("", layoutProblem);
                return View();
            }

            // Preview: header + first 5 data rows, rebuilt as display strings
            var preview = allRows
                .Take(Math.Min(allRows.Count, 6))
                .Select(cells => string.Join(delimiter, cells))
                .ToList();

            // Profiling
            var missingPerColumn = headers.ToDictionary(h => h, h => 0);
            var numericColumns = new List<string>();
            var textColumns = new List<string>();

            for (int col = 0; col < headers.Length; col++)
            {
                bool isNumeric = true;

                foreach (var cells in dataRows)
                {
                    if (col >= cells.Length || string.IsNullOrWhiteSpace(cells[col]))
                    {
                        missingPerColumn[headers[col]]++;
                        continue;
                    }

                    if (!double.TryParse(cells[col], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                        isNumeric = false;
                }

                if (isNumeric) numericColumns.Add(headers[col]);
                else textColumns.Add(headers[col]);
            }

            // Build series (for dropdown chart)
            int maxPoints = 100;

            var headerIndex = headers
                .Select((h, idx) => new { h, idx })
                .ToDictionary(x => x.h, x => x.idx);

            var numericSeries = new Dictionary<string, List<double>>();
            foreach (var colName in numericColumns)
                numericSeries[colName] = new List<double>();

            var seriesLabels = new List<string>();

            int point = 0;
            foreach (var cells in dataRows.Take(maxPoints))
            {
                bool anyValueAdded = false;

                foreach (var colName in numericColumns)
                {
                    if (!headerIndex.TryGetValue(colName, out int colIndex)) continue;
                    if (colIndex >= cells.Length) continue;

                    if (double.TryParse(cells[colIndex], NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                    {
                        numericSeries[colName].Add(v);
                        anyValueAdded = true;
                    }
                }

                if (anyValueAdded)
                {
                    seriesLabels.Add((point + 1).ToString());
                    point++;
                }
            }

            // Default selection (avoid "time" if possible)
            var selected = numericColumns.FirstOrDefault(c => !c.Equals("time", StringComparison.OrdinalIgnoreCase))
                           ?? numericColumns.FirstOrDefault()
                           ?? "";

            var vm = new DatasetDetailsViewModel
            {
                OriginalFileName = file.FileName,
                StoredFileName = "",
                RowCount = dataRows.Count,
                ColumnCount = headers.Length,
                Headers = headers.ToList(),
                PreviewLines = preview,

                MissingValuesPerColumn = missingPerColumn,
                TotalMissingValues = missingPerColumn.Values.Sum(),
                NumericColumns = numericColumns,
                TextColumns = textColumns,

                SeriesLabels = seriesLabels,
                NumericSeries = numericSeries,
                SelectedChartColumn = selected
            };

            vm.Delimiter = delimiter;

            return View("Details", vm);
        }
    }
}