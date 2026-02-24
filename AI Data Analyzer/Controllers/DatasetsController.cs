using AI_Data_Analyzer.Models;
using Microsoft.AspNetCore.Mvc;
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

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("", "Please select a CSV file.");
                return View();
            }

            // Save to /Uploads
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
            Directory.CreateDirectory(uploadsFolder);

            var safeName = Path.GetFileNameWithoutExtension(file.FileName);
            var ext = Path.GetExtension(file.FileName);
            var uniqueName = $"{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}{ext}";
            var filePath = Path.Combine(uploadsFolder, uniqueName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Read lines
            var lines = await System.IO.File.ReadAllLinesAsync(filePath);
            if (lines.Length == 0)
            {
                ModelState.AddModelError("", "CSV is empty.");
                return View();
            }

            // Detect delimiter + split header
            char delimiter = DetectDelimiter(lines[0]);

            // Trim header values to avoid BOM/whitespace issues
            var headers = lines[0].Split(delimiter).Select(h => h.Trim()).ToArray();
            var dataRows = lines.Skip(1).ToList();

            // Preview: header + first 5 data rows
            var preview = lines.Take(Math.Min(lines.Length, 6)).ToList();

            // Profiling
            var missingPerColumn = headers.ToDictionary(h => h, h => 0);
            var numericColumns = new List<string>();
            var textColumns = new List<string>();

            for (int col = 0; col < headers.Length; col++)
            {
                bool isNumeric = true;

                foreach (var row in dataRows)
                {
                    var cells = row.Split(delimiter).Select(c => c.Trim()).ToArray();

                    if (col >= cells.Length || string.IsNullOrWhiteSpace(cells[col]))
                    {
                        missingPerColumn[headers[col]]++;
                        continue;
                    }

                    // Culture-safe numeric detection
                    if (!double.TryParse(cells[col], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                        isNumeric = false;
                }

                if (isNumeric) numericColumns.Add(headers[col]);
                else textColumns.Add(headers[col]);
            }

            // Build series (for dropdown chart)
            int maxPoints = 100;

            // header index lookup
            var headerIndex = headers
                .Select((h, idx) => new { h, idx })
                .ToDictionary(x => x.h, x => x.idx);

            // Prepare storage
            var numericSeries = new Dictionary<string, List<double>>();
            foreach (var colName in numericColumns)
                numericSeries[colName] = new List<double>();

            var seriesLabels = new List<string>();

            int point = 0;
            foreach (var row in dataRows.Take(maxPoints))
            {
                var cells = row.Split(delimiter).Select(c => c.Trim()).ToArray();

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
                StoredFileName = uniqueName,
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
