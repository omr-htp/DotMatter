# Third-Party Notices

## Project CHIP / connectedhomeip

DotMatter vendors Matter cluster definition inputs under `DotMatter.CodeGen/Xml/` for design-time code generation.

- Source project: `project-chip/connectedhomeip`
- Upstream repository: https://github.com/project-chip/connectedhomeip
- Vendored content in this repo: Matter cluster XML inputs used by `DotMatter.CodeGen`
- Provenance: files were taken from the public upstream project and keep their original upstream license headers
- License: Apache License 2.0
- License text: `LICENSES/Apache-2.0.txt`
- Required upstream NOTICE text: `NOTICE`

These inputs are used at design time to generate cluster-related source code for DotMatter.

Generated cluster source files under `DotMatter.Core/Clusters/` are produced from these inputs and are distributed as part of DotMatter. DotMatter-authored code is licensed under this repository's MIT license; portions derived from the upstream Matter SDK inputs remain subject to the upstream Apache License 2.0 terms and NOTICE requirements.

## Matter protocol schema inputs

DotMatter also vendors protocol schema JSON files under `DotMatter.CodeGen/Protocols/` for design-time generation of protocol message types.

- Vendored content in this repo: Interaction Model and security message schema JSON files used by `DotMatter.CodeGen`
- Provenance: project-local schema descriptions derived from the Matter protocol model used by this implementation
- Generated output in this repo: source files under `DotMatter.Core/Protocol/Generated/`

## NuGet dependencies

Runtime and build dependencies are declared in the project files (`*.csproj`) and resolved from NuGet. Review package license metadata for the exact dependency graph used by a given restore, especially before redistributing binary packages.
