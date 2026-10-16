# Setting up code signing (maintainer checklist)

Everything for signing is already prepared in the repository, and **switched off** until the variables in step 4 exist.
Nothing here has been run against a real SignPath account yet, so expect to adjust small details.

## What is ready
- `.github/workflows/build.yml`: builds the unsigned exe, and **if SignPath is configured and the run is for a `v*` tag**
  sends it to SignPath, waits for the approved signed file, checks that it carries a valid signature, and packs the
  release zip from it. If SignPath is not configured, the same workflow releases the unsigned exe as before.
- `tools/package.ps1` has `-BuildOnly` (build the exe) and `-ExePath <file>` (zip a given exe, for example the signed one).
- `.signpath/artifact-configuration.xml`: what SignPath should sign.
- `docs/code-signing-policy.md`: the policy page SignPath requires. It says honestly that releases are not signed yet.

## Steps
1. **Before applying:** turn on two-factor authentication on your GitHub account (Settings, Password and authentication).
   SignPath requires it for everyone on the team.
2. **Apply** at <https://signpath.org/apply>. You will be asked for the repository URL, license, a description, and links
   to the download page and the code signing policy page (`https://github.com/notkazhons/ClusterWidget/blob/main/docs/code-signing-policy.md`).
   Approval is manual and not guaranteed: SignPath's terms ask for a project with some "verifiable reputation" and do not
   say how much. If it is refused or postponed, ask what they would want to see (users, stars, time) and try again later.
3. **After approval**, in SignPath create the project (name it `ClusterWidget`), add the artifact configuration from
   `.signpath/artifact-configuration.xml`, and set the signing policy so that releases need a manual approval.
   Create an **API token** for the GitHub workflow (a CI user).
4. **In GitHub** (repository, Settings, Secrets and variables, Actions) add:

   | kind | name | value |
   |---|---|---|
   | secret | `SIGNPATH_API_TOKEN` | the CI user's API token |
   | variable | `SIGNPATH_ORGANIZATION_ID` | your SignPath organization id |
   | variable | `SIGNPATH_PROJECT_SLUG` | the project slug, for example `clusterwidget` |
   | variable | `SIGNPATH_SIGNING_POLICY_SLUG` | the policy slug, for example `release-signing` |
   | variable | `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | the configuration slug, for example `default` |

   Also connect the GitHub repository as a trusted build system in SignPath (their "origin verification" check).
5. **Update the policy page:** in `docs/code-signing-policy.md` delete the "not signed yet" box and uncomment the credit
   line. Add a link to the policy page in the README and on the website.
6. **Make a release:** bump `<Version>` in the csproj, update the changelog, push a tag `vX.Y.Z`. Approve the request in
   SignPath. The workflow then publishes the signed zip. If the signed exe does not carry a valid signature the release
   job stops instead of publishing an unsigned file by mistake.

## Good to know
- A signature does not make SmartScreen warnings disappear at once. Reputation builds with downloads over weeks. Always
  sign every release with the same publisher so the reputation carries over.
- Certificates from SignPath Foundation name **SignPath Foundation** as the publisher, not you.
- Pin the `signpath/github-action-submit-signing-request` action to a full version once it works, and keep
  `actions/upload-artifact` / `download-artifact` at v4 (the signing step reads the artifact by id, which needs v4).
- Other routes if SignPath says no: Azure Artifact Signing (about $10 a month, individuals only in the USA and Canada),
  a regular code-signing certificate, or publishing through the Microsoft Store (free re-signing, needs MSIX packaging).
