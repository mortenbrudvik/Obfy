# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.x.x   | :white_check_mark: |
| < 1.0   | :x:                |

## Reporting a Vulnerability

We take security vulnerabilities seriously. If you discover a security issue, please report it responsibly.

### How to Report

1. **Do not** open a public GitHub issue for security vulnerabilities
2. Email the maintainers directly or use GitHub's private vulnerability reporting feature
3. Include as much detail as possible:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

### What to Expect

- **Acknowledgment**: We will acknowledge receipt within 48 hours
- **Initial Assessment**: We will provide an initial assessment within 7 days
- **Resolution**: We aim to resolve critical issues within 30 days
- **Disclosure**: We will coordinate disclosure timing with you

### Scope

Security issues we are interested in:

- Vulnerabilities in Obfy that could be exploited
- Issues that could lead to unintended code execution
- Bypass of obfuscation protections
- Information disclosure vulnerabilities

### Out of Scope

- Issues in dependencies (report to the respective projects)
- Theoretical attacks without proof of concept
- Issues requiring physical access to the machine
- Social engineering attacks

## Security Best Practices

When using Obfy:

1. **Keep Updated**: Always use the latest version
2. **Secure Keys**: If using custom encryption keys, store them securely
3. **Test Thoroughly**: Always test obfuscated assemblies before deployment
4. **Backup Original**: Keep unobfuscated versions for debugging

## Acknowledgments

We appreciate security researchers who help keep Obfy secure. Contributors who report valid security issues will be acknowledged (with permission) in our release notes.
