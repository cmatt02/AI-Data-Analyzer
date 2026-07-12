using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AI_Data_Analyzer.Controllers
{
    // Request shape posted from the Details page. Only summary/profile data is sent,
    // never the full raw file.
    public class InsightsRequest
    {
        public string FileName { get; set; } = "";
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
        public int TotalMissingValues { get; set; }
        public List<string> NumericColumns { get; set; } = new();
        public List<string> TextColumns { get; set; } = new();
        public Dictionary<string, int> MissingValuesPerColumn { get; set; } = new();
        // A few sample rows (already trimmed by the client) for light context.
        public List<string> SampleRows { get; set; } = new();
    }

    public class InsightsController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public InsightsController(IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost]
        public async Task<IActionResult> Generate([FromBody] InsightsRequest req)
        {
            var apiKey = _config["Anthropic:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return StatusCode(500, new { error = "AI is not configured on the server. Set Anthropic:ApiKey." });
            }

            // Model is configurable; default to a fast, low-cost model.
            var model = _config["Anthropic:Model"] ?? "claude-haiku-4-5-20251001";

            var prompt = BuildPrompt(req);

            var payload = new
            {
                model,
                max_tokens = 700,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            httpReq.Headers.Add("x-api-key", apiKey);
            httpReq.Headers.Add("anthropic-version", "2023-06-01");
            httpReq.Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                using var resp = await client.SendAsync(httpReq);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    return StatusCode((int)resp.StatusCode,
                        new { error = "The AI service returned an error. Please try again." });
                }

                // Response: { "content": [ { "type": "text", "text": "..." }, ... ] }
                using var doc = JsonDocument.Parse(body);
                var sb = new StringBuilder();
                if (doc.RootElement.TryGetProperty("content", out var content))
                {
                    foreach (var block in content.EnumerateArray())
                    {
                        if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                            && block.TryGetProperty("text", out var txt))
                        {
                            sb.Append(txt.GetString());
                        }
                    }
                }

                var text = sb.ToString().Trim();
                if (string.IsNullOrEmpty(text))
                    text = "No insights were returned. Please try again.";

                return Json(new { insights = text });
            }
            catch (TaskCanceledException)
            {
                return StatusCode(504, new { error = "The AI request timed out. Please try again." });
            }
            catch (Exception)
            {
                return StatusCode(500, new { error = "Something went wrong contacting the AI service." });
            }
        }

        private static string BuildPrompt(InsightsRequest req)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are a data analyst. Given the PROFILE of a dataset (not the full data),");
            sb.AppendLine("write a brief, practical analysis for a non-technical user.");
            sb.AppendLine("Cover: (1) what the dataset appears to contain, (2) data-quality notes such as");
            sb.AppendLine("missing values, (3) 2-3 concrete next analysis steps. Keep it under 200 words.");
            sb.AppendLine("Be careful not to over-claim; you only have a profile and a small sample.");
            sb.AppendLine();
            sb.AppendLine($"File: {req.FileName}");
            sb.AppendLine($"Rows: {req.RowCount}, Columns: {req.ColumnCount}");
            sb.AppendLine($"Total missing values: {req.TotalMissingValues}");
            sb.AppendLine($"Numeric columns: {string.Join(", ", req.NumericColumns)}");
            sb.AppendLine($"Text columns: {string.Join(", ", req.TextColumns)}");

            if (req.MissingValuesPerColumn.Count > 0)
            {
                sb.AppendLine("Missing values per column:");
                foreach (var kv in req.MissingValuesPerColumn)
                    sb.AppendLine($"  - {kv.Key}: {kv.Value}");
            }

            if (req.SampleRows.Count > 0)
            {
                sb.AppendLine("Sample rows (small excerpt):");
                foreach (var r in req.SampleRows.Take(5))
                    sb.AppendLine($"  {r}");
            }

            return sb.ToString();
        }
    }
}