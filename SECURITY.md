# Security Policy

Shelfbound is a local tool that reads files from your Steam installation. The most relevant security
concerns are: reading data beyond what is needed, leaking personal data into a snapshot, or a crash/
exploit triggered by parsing malformed local files.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

- Preferred: use GitHub's **private vulnerability reporting** for this repository (Security →
  Report a vulnerability), if enabled.
- Or email **wolfsblvt@gmail.com** with details and, ideally, a reproduction.

Please give a reasonable amount of time for a fix before any public disclosure.

## Scope

In scope: the scanner reading outside the documented local files; a snapshot including data it
shouldn't (credentials, save data, full install paths); parser crashes/exploits from crafted
`.vdf`/`.acf` input; anything that could expose user data.

Out of scope: the separate hosted product (reported privately), and issues in third-party
dependencies (report upstream).

## Expectations

This is a hobby/side project maintained on a best-effort basis — no guaranteed response time, but
security reports are taken seriously and prioritized.
