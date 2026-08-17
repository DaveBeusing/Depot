# Depot Help Center

Depot ships an integrated, offline Help Center. It uses embedded Markdown files and native WPF `FlowDocument` rendering. It does not use a browser, WebView, HTML, JavaScript, online service, or business-database table.

Help opens as a regular workspace tab in the application shell. Pressing **F1** resolves the current context and activates the Help tab without replacing the user's other open workspaces.

## Content structure

Help content is versioned with the application under `src/Depot/Help`:

```text
Help/
  manifest.json
  getting-started/
  inventory/
  warehouse/
  purchasing/
  approvals/
  reports/
  administration/
  troubleshooting/
```

`manifest.json` defines the content version and every topic. Each topic contains:

| Field | Purpose |
| --- | --- |
| `id` | Stable technical topic ID |
| `title` | English display title |
| `category` | Category shown in the Help Center |
| `file` | Markdown path relative to `Help` |
| `order` | Stable display order |
| `keywords` | Search aliases and terminology |
| `requiredPermission` | Optional permission-catalog code |
| `relatedTopics` | Stable IDs of related topics |

The build embeds the manifest and Markdown files in `Depot.dll`. Content therefore follows the installed application version and is never stored in the operational database.

## Current shell guidance

The Getting Started category documents the current workspace-oriented shell, including:

- activity-bar navigation
- persistent workspace tabs
- contextual section navigation
- `Ctrl+P` Quick Open
- `Ctrl+Shift+P` Command Palette
- `Ctrl+W` tab closing
- `Ctrl+Tab` and `Ctrl+Shift+Tab` tab switching
- F1 context Help
- unsaved-changes protection
- grouped Quick Open results and session-recent records
- direct workflow commands such as New Item, New Purchase Order, Start Inventory Count, Transfer Stock, and Receive Goods

Help topics for affected workflows should mention these direct navigation paths where they materially improve the task.

## Topic ID conventions

Topic IDs are lowercase and use the format `category.topic-name`, for example:

- `getting-started.workspace-navigation`
- `inventory.items`
- `warehouse.inventory-counts`
- `purchasing.goods-receipts`
- `troubleshooting.concurrency-conflict`

IDs are application contracts. Do not rename an existing ID after it has been used for context help or internal links. Change the title when display wording needs to change.

## Supported Markdown subset

The native renderer intentionally supports only:

- headings using `#` through `######`
- paragraphs
- ordered and unordered lists
- bold using `**text**`
- italic using `*text*`
- inline code using backticks
- notes using `> [!NOTE]`
- warnings using `> [!WARNING]`
- images using `![description](resource-path)`
- internal links using `[label](topic:stable.topic-id)`
- simple pipe tables

HTML and arbitrary external links are not supported.

## Adding or updating a topic

1. Create or update an English Markdown file in the appropriate category directory.
2. Use the standard article sections: Summary, Prerequisites, Steps, Result, Common problems, Required permissions, and Related topics where they are relevant.
3. Add one manifest entry for every new stable ID and keep display ordering deterministic.
4. Add search aliases to `keywords` where terminology differs, such as `transfer`, `relocation`, `quick open`, or `command palette`.
5. Add internal and related-topic links only to IDs already present in the manifest.
6. Keep shell shortcuts and direct workflow commands synchronized with the implementation.
7. Run `HelpCenterTests`. Validation fails for duplicate IDs, missing files, unknown permissions, and broken links.

Do not describe planned functionality as available.

## Context help

Main shell items and secondary pages carry a `HelpTopicId`. F1 resolves the currently selected page and opens that topic in the Help workspace tab. A missing or unavailable context topic falls back to `getting-started.first-login`.

Useful workflow links use the routed `HelpCommands.OpenTopic` command:

```xml
<Button
    Command="{x:Static commands:HelpCommands.OpenTopic}"
    CommandParameter="warehouse.transfers"
    Style="{StaticResource ContextHelpButtonStyle}" />
```

Use explicit links for complex workflows and recovery guidance. Do not add a help icon to every field.

## Permissions

`requiredPermission` uses the existing central permission code, such as `GoodsReceipts.View`. `HelpService` filters topic lists, search results, direct topic access, and related topics against the effective permissions of the signed-in user.

Public getting-started and troubleshooting topics omit `requiredPermission`. Help visibility never grants business access; workflow services continue to enforce authorization independently.

## Search and related topics

Search is local and weighted across title, manifest keywords, headings, and body text. All entered terms must match. Titles and keyword aliases rank ahead of body matches.

Related topics are defined by stable IDs in the manifest and are permission-filtered before display.

## Diagnostics

Selected operation-error panels can offer **Open Help** and **Copy diagnostics**. Copied text passes through `DiagnosticsSanitizer`, which masks passwords, connection strings, hashes, salts, secrets, tokens, encryption keys, protected configuration, and sensitive SQL parameter values.
