# Security Policy

## Supported scope

KeyHub Desktop protects secrets at rest with Windows DPAPI for the current user and reduces accidental plaintext sprawl. It avoids placing secret values in logs, project manifests, command-line arguments, Git history, or remote shell commands.

It does not claim to protect against malware running as the same Windows user, administrators, kernel-level access, memory inspection, or a compromised remote server.

## Reporting

Please report security issues privately through GitHub Security Advisories rather than a public issue. Do not include real credentials in reports.

## Release practices

- Dependencies are checked by NuGet audit during restore.
- CI runs Gitleaks before building.
- Release artifacts include SHA256 checksums.
- Binaries are currently unsigned.
