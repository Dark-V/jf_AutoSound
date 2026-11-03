# Security

Treat media trees as administrator-controlled input. This version rejects static symlinks, junctions and root escapes but does not guarantee safety against hostile concurrent directory replacement. Review [the exact trust boundary](docs/limitations.md) before deployment.

Do not post credentials, server URLs containing tokens or full private library listings in public issues. Report a security problem through the repository owner's available GitHub security reporting channel where enabled; otherwise describe the issue without sensitive exploit details and request a private contact route.
