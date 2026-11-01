# CI & Quest Release Workflows

This directory contains pre-configured GitHub Actions workflows for continuous integration and automated Meta Quest APK packaging:

- `ci.yml`: Runs on push and PR to verify Node 22 runtime, execute module syntax checks across all 16 server/browser modules, run the 91 unit & soak tests, and test Docker image builds.
- `build-apk.yml`: Uses GameCI to build `movie-night.apk` for Meta Quest (Android / IL2CPP) and attach it to releases.

To activate these workflows on GitHub:
Copy `ci.yml` and `build-apk.yml` into `.github/workflows/` on your repository.
