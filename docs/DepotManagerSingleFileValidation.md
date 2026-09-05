# Depot Manager Single-File Validation

Depot release assets are published as self-contained .NET single-file Windows executables. The outer executable is a native Windows app host and therefore does not necessarily expose managed metadata directly through `PEReader.HasMetadata`.

Depot Manager validates downloaded release assets as Windows PE executables, then separately verifies the release file version against the selected published release and validates the GitHub SHA-256 digest when available. Requiring direct managed metadata on the outer single-file host is intentionally not part of release validation.
