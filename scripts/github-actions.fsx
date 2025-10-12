let licenseHeader = """
# SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
#
# SPDX-License-Identifier: MIT

# This file is auto-generated.""".Trim()

#r "nuget: Generaptor.Library, 1.8.0"
open Generaptor
open Generaptor.GitHubActions
open type Generaptor.GitHubActions.Commands
let workflows = [
    workflow "main" [
        header licenseHeader
        name "Main"
        onPushTo "main"
        onPullRequestTo "main"

        let dotNetJob id steps dotNetOptions =
            job id [
                setEnv "DOTNET_CLI_TELEMETRY_OPTOUT" "1"
                setEnv "DOTNET_NOLOGO" "1"
                setEnv "NUGET_PACKAGES" "${{ github.workspace }}/.github/nuget-packages"

                step(
                    name = "Check out the sources",
                    usesSpec = Auto "actions/checkout"
                )
                step(
                    name = "Set up .NET SDK",
                    usesSpec = Auto "actions/setup-dotnet",
                    ?options = dotNetOptions
                )
                step(
                    name = "Cache NuGet packages",
                    usesSpec = Auto "actions/cache",
                    options = Map.ofList [
                        "key", "${{ runner.os }}.nuget.${{ hashFiles('**/*.*proj', '**/*.props') }}"
                        "path", "${{ env.NUGET_PACKAGES }}"
                    ]
                )

                yield! steps
            ]

        dotNetJob "verify-workflows" [
            runsOn "ubuntu-24.04"
            step(run = "dotnet fsi ./scripts/github-actions.fsx verify")
        ] (Some <| Map.ofList [ "dotnet-version", "9.0.x" ])

        dotNetJob "main" [
            runsOn "${{ matrix.environment }}"
            strategy(matrix = [
                "environment", [
                    "macos-13"
                    "ubuntu-22.04"
                    "windows-2022"
                ]
            ], failFast = false)
            step(
                name = "Build",
                run = "dotnet build"
            )
            step(
                name = "Run unit tests",
                run = "cd Fabricator.Tests && dotnet test"
            )
            step(
                name = "Run integration tests",
                run = "cd Fabricator.IntegrationTests && dotnet test"
            )
        ] None
        job "encoding" [
            runsOn "ubuntu-24.04"
            step(
                name = "Check out the sources",
                uses = "actions/checkout@v5"
            )
            step(
                name = "Verify encoding",
                shell = "pwsh",
                run = "Install-Module VerifyEncoding -Repository PSGallery -RequiredVersion 2.2.1 -Force && Test-Encoding"
            )
        ]
        job "licenses" [
            runsOn "ubuntu-24.04"
            step(
                name = "Check out the sources",
                uses = "actions/checkout@v5"
            )
            step(
                name = "REUSE license check",
                uses = "fsfe/reuse-action@v5"
            )
        ]
    ]
]
EntryPoint.Process fsi.CommandLineArgs workflows
