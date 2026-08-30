# Security Policy

## Reporting a vulnerability

Please report suspected security vulnerabilities privately and avoid publishing exploit details in a public issue, discussion, pull request, or commit message before coordinated remediation.

Preferred channel:

1. Open the repository **Security** tab.
2. Choose **Report a vulnerability** / private vulnerability reporting when available.
3. Include affected version(s), component, reproduction conditions, impact, and any safe proof-of-concept details.

If private vulnerability reporting is not available, create a public issue containing only a request for a private security-reporting channel. Do **not** include exploit steps, credentials, personal data, secrets, or vulnerability details in that public issue.

## What to expect

Reports are triaged according to `docs/compliance/VulnerabilityManagement.md`. Depot aims to acknowledge and classify credible reports promptly, validate affected versions, coordinate remediation and regression testing, and disclose sufficient information for users to identify and install a fixed release without unnecessarily exposing exploit details before remediation is available.

## Supported versions

Preview builds are development builds and are not production-supported. Production support windows and end dates are governed by `docs/compliance/SupportPolicy.md` and must be published for commercially distributed release lines.

## Scope

Security reports may include application vulnerabilities, authentication/authorization bypasses, unsafe database or file handling, secret exposure, dependency vulnerabilities, update/release integrity issues, backup/restore weaknesses, or other behavior that can materially affect confidentiality, integrity, availability, authenticity, or accountability.
