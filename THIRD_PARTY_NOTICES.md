# Third-Party Notices

CreoToolkit depends on and/or distributes the following third-party
software, each licensed under terms compatible with the Apache License 2.0
under which CreoToolkit itself is distributed.

## Runtime / Build-time NuGet Packages

| Package | Version | License | Project |
|---|---|---|---|
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | https://github.com/microsoft/vstest |
| System.Diagnostics.DiagnosticSource | 8.0.1 | MIT | https://github.com/dotnet/runtime |
| System.Memory | 4.6.0 | MIT | https://github.com/dotnet/runtime |
| coverlet.collector | 6.0.4 | MIT | https://github.com/coverlet-coverage/coverlet |
| xunit | 2.9.3 | Apache-2.0 | https://github.com/xunit/xunit |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | https://github.com/xunit/visualstudio.xunit |

License texts for the above packages are bundled inside each NuGet package
and are available at https://licenses.nuget.org/ or in each project's
upstream repository.

## Code-generation Toolchain (build-time only, not redistributed)

- **libclang** — part of the LLVM Project.
  License: Apache-2.0 with LLVM Exceptions.
  Used by `tools/CreoToolkit.Generator/` to parse PTC Creo Toolkit C/C++
  headers and emit C# P/Invoke bindings. libclang is loaded via the
  `libclang` PyPI wheel at build time and is NOT redistributed by this
  project.
  Upstream: https://github.com/llvm/llvm-project

- **CMake** — Kitware, Inc.
  License: BSD-3-Clause.
  Required to configure and build the native C++ components. Not
  redistributed.

- **Python 3** — Python Software Foundation.
  License: PSF License.
  Required to drive `tools/CreoToolkit.Generator/`. Not redistributed.

## External (user-provided, NOT redistributed)

- **PTC Creo Toolkit headers and libraries** — proprietary, owned by
  PTC Inc. Users of CreoToolkit must obtain their own valid Creo license
  and install the Creo Toolkit API independently. See `TRADEMARKS.md`.

- **.NET SDK** (`dotnet` CLI；build-time only) — Microsoft Corporation.
  License: MIT. Used to compile SDK-style `net472` projects. **Not** loaded into
  Creo and **not** a substitute for .NET Framework. Any current SDK that can
  target `net472` is sufficient (8/9/10 verified).

- **.NET Framework 4.7.2 / 4.8** — Microsoft Corporation.
  License: Microsoft Software License. The **only** in-process CLR for Creo
  hosting (`v4.0.30319`). Users install separately (typical Creo 4 Windows already has it).

────────────────────────────────────────────────────────────────────────────

If you believe a third-party dependency is missing from this list or
listed incorrectly, please open a GitHub issue.
