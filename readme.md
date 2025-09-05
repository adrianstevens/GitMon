\# GitMon — GitHub Pull Request Review Monitor



GitMon is a lightweight .NET 8 console tool that scans all repositories in a GitHub organization and reports on \*\*Pull Request review activity\*\*.  



It answers questions like:

\- How many PRs were merged without any review?

\- How many had only comments vs formal approvals?

\- What percentage of PRs were reviewed in the last week?



It produces:

\- A \*\*per-repo summary\*\* (counts + percentages)

\- A \*\*CSV export\*\* for further analysis

\- A \*\*list of PRs merged without reviews\*\* (coming soon)



---



\## Features



\- \*\*MVP Metrics\*\*

&nbsp; - Count of merged PRs per repo

&nbsp; - % reviewed (any review), approved, changes requested, commented only, no review

\- \*\*CSV Export\*\* for downstream dashboards

\- \*\*Rate-limit aware\*\* with automatic retries

\- \*\*Configurable lookback window\*\* (default 7 days)

\- \*\*Exclude draft PRs\*\* per policy

\- \*\*Self-reviews ignored\*\* (only external reviews count)



---



\## Quick Start



\### 1. Prerequisites

\- \[.NET 8 SDK](https://dotnet.microsoft.com/en-us/download)

\- A GitHub \*\*Personal Access Token\*\* with:

&nbsp; - `repo` (read-only) access for private repos

&nbsp; - (Optional) `read:org` for listing org repos



---



\### 2. Clone \& build

```bash

git clone https://github.com/<your-org>/GitMon.git

cd GitMon

dotnet build



