# AI Data Analyzer

Upload a CSV → automatic profiling + charts → AI generates insights → save as a report.

## Tech Stack
- ASP.NET MVC (Razor) + .NET 10
- SQL Server + EF Core
- Chart.js
- OpenAI API (AI insights)

## MVP Roadmap
- [ ] Create Projects
- [ ] Upload CSV to a Project
- [ ] Dataset profiling (rows/cols, missing values, column types)
- [ ] Dataset details page (preview table)
- [ ] Charts (histogram/line/bar based on column type)
- [ ] AI insights (JSON output)
- [ ] Save insight runs + generate reports
- [ ] Deployment

## Data Privacy
The AI feature will not send full raw datasets by default—only summary statistics and small samples.
