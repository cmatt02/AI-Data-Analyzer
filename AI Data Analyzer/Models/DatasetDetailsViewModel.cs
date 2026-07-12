namespace AI_Data_Analyzer.Models
{
    public class DatasetDetailsViewModel
    {
        public string OriginalFileName { get; set; } = "";
        public string StoredFileName { get; set; } = "";

        public int RowCount { get; set; }
        public int ColumnCount { get; set; }

        public List<string> Headers { get; set; } = new();

        // Legacy: joined preview strings (kept for compatibility with the AI insights payload).
        public List<string> PreviewLines { get; set; } = new();

        // New: preview rows already split into cells — safe for quoted-comma fields.
        public List<string[]> PreviewRows { get; set; } = new();

        public int TotalMissingValues { get; set; }
        public Dictionary<string, int> MissingValuesPerColumn { get; set; } = new();
        public List<string> NumericColumns { get; set; } = new();
        public string SelectedChartColumn { get; set; } = "";
        public List<string> TextColumns { get; set; } = new();
        public Dictionary<string, List<double>> NumericSeries { get; set; } = new();
        public List<string> SeriesLabels { get; set; } = new();
        public char Delimiter { get; set; } = ',';
    }
}