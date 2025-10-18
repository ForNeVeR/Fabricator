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
    let dotNetJob id steps =
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
                usesSpec = Auto "actions/setup-dotnet"
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

    workflow "main" [
        header licenseHeader
        name "Main"
        onPushTo "main"
        onPullRequestTo "main"

        dotNetJob "verify-workflows" [
            runsOn "ubuntu-24.04"
            step(run = "dotnet fsi ./scripts/github-actions.fsx verify")
        ]

        dotNetJob "main" [
            runsOn "${{ matrix.environment }}"
            strategy(matrix = [
                "environment", [
                    "macos-15"
                    "ubuntu-24.04"
                    "windows-2025"
                ]
            ], failFast = false)
            step(
                name = "Build",
                run = "dotnet build"
            )
            step(
                name = "Run unit tests",
                run = "dotnet test"
            )
        ]
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

    workflow "docs" [
        header licenseHeader
        name "Docs"
        onPushTo "main"
        onWorkflowDispatch

        workflowPermission(PermissionKind.Actions, AccessKind.Read)
        workflowPermission(PermissionKind.Pages, AccessKind.Write)
        workflowPermission(PermissionKind.IdToken, AccessKind.Write)

        workflowConcurrency(group = "pages", cancelInProgress = true)

        dotNetJob "docs" [
            environment(name = "github-pages", url = "${{ steps.deployment.outputs.page_url }}")
            runsOn "ubuntu-24.04"
            step(
                name = "Restore .NET tools",
                run = "dotnet tool restore"
            )
            step(
                name = "Publish the assemblies",
                run = "dotnet publish -o publish"
            )
            step(
                 name = "Run docfx",
                 run = "dotnet docfx docs/docfx.json"
            )
            step(
                name = "Upload artifact",
                usesSpec = Auto "actions/upload-pages-artifact"
            )
            step(
                name = "Deploy GitHub Pages",
                id = "deployment",
                usesSpec = Auto "actions/deploy-pages"
            )
        ]
    ]
]

exit <| EntryPoint.Process fsi.CommandLineArgs workflows
