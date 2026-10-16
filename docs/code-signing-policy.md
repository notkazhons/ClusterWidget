# Code signing policy

> **Status: not signed yet.** ClusterWidget releases are currently **not** code-signed. The project is applying for free
> code signing from the [SignPath Foundation](https://signpath.org). Until that is approved and set up, Windows may show
> a SmartScreen warning or block the download; see "If Windows won't let you run it" in the [README](../README.md).
> This page describes how signing will work, and will be updated when it is active.

<!--
  AFTER SignPath approves the project, delete the status box above and uncomment this credit (SignPath requires it
  on this page and on the download pages):

  Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).
-->

## What gets signed
Only `ClusterWidget.exe`, the program shipped in the release zip. Nothing else is signed, and nothing from other projects is.
The product name in the file properties is `ClusterWidget`, and the version matches the release (for example `1.0.0`).

## How a release is made
1. The code is public in [github.com/notkazhons/ClusterWidget](https://github.com/notkazhons/ClusterWidget), under the MIT license.
2. A release is a git tag (`v1.x.x`). Pushing the tag runs the build workflow in `.github/workflows/build.yml` on GitHub's
   servers. The executable is built there from the tagged source, never on a personal computer.
3. The unsigned executable is sent from that workflow to SignPath. A named approver must approve each signing request by hand.
4. The signed file is packed into the release zip, which is published on the project's Releases page together with a SHA-256 checksum.

## Team roles
| Role | Who | What they do |
|---|---|---|
| Author | [notkazhons](https://github.com/notkazhons) | commits to the repository |
| Reviewer | [notkazhons](https://github.com/notkazhons) | reviews contributions from other people before they are merged |
| Approver | [notkazhons](https://github.com/notkazhons) | approves each signing request |

All team members use multi-factor authentication for GitHub and for SignPath.

## Privacy policy
This program will not transfer any information to other networked systems unless specifically requested by the user or
the person installing or operating it. ClusterWidget makes no network connections at all. It stores its settings,
copies of dropped shortcuts and chosen pictures only in `%APPDATA%\ClusterWidget` on the user's own computer.

## Changes the program makes to the system
- **Start with Windows** (off by default; the user turns it on in Settings, Behavior) adds one value,
  `ClusterWidget`, under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Turning it off removes it.
- Nothing else outside `%APPDATA%\ClusterWidget` is modified.

## Removing the program
There is no installer. Delete the folder the zip was extracted to, turn off *Start with Windows* first (or delete the
`ClusterWidget` value above), and delete `%APPDATA%\ClusterWidget` if you also want your settings gone.

## Reporting a problem
Open an issue at [github.com/notkazhons/ClusterWidget/issues](https://github.com/notkazhons/ClusterWidget/issues).
