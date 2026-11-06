# Riry's Animator Templates

Source of `com.riry.animatortemplates`, Riry's animator templates for YGDR's animator template tool.

- [`com.riry.animatortemplates`](com.riry.animatortemplates): the package. Everything in it is zipped.

Install it from the VRChat Creator Companion: **Settings > Packages > Add Repository** and enter
`https://rionquosue.github.io/rirytools_vpm/index.json`.

Every push to `main` is published there as a new version, and every push to `beta` as a
beta, by [Rionquosue/rirytools_vpm](https://github.com/Rionquosue/rirytools_vpm), with tags
`animatortemplates-v<version>`. Only this package's version changes; RiryTools' does not.

## Setup

1. Add the secret `VPM_DISPATCH_TOKEN`: a fine-grained personal access token with access to
   `Rionquosue/rirytools_vpm` only and the single permission **Actions: Read and write**.
2. Create a `beta` branch from `main`.

## Package folder

- No `package.json`: `rirytools_vpm` adds it from `animatortemplates.package.json`, with the version.
- Every file and folder needs its `.meta`, and its GUID must never change.
- Dot files and dot folders are left out. Symbolic links and Git LFS pointers fail the build.
