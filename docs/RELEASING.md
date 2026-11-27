# Releasing

Claude handles releases end to end. For reference:

1. CI must be green on `main` (it builds, tests, and screenshots every page from the real app).
2. Add release notes at `docs/release-notes/<version>.md`.
3. Run the **Release** workflow (Actions → Release → Run workflow) with a version such as `0.2.0`, or push a `v0.2.0` tag.
   Versions with a `-` are published as pre-releases.
4. The workflow tests, publishes the app (self-contained, win-x64), packs it with Velopack and uploads to GitHub Releases.
5. Installed copies see the update on next launch (or within 6 hours) and install it with one click.
