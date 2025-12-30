open System
open System.IO

let licenseHeader = """
# SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
#
# SPDX-License-Identifier: MIT

# This file is auto-generated.""".Trim()

#r "nuget: Generaptor.Library, 1.9.0"
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

        job "encoding" [
            runsOn "ubuntu-24.04"
            step(
                name = "Check out the sources",
                usesSpec = Auto "actions/checkout"
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
                usesSpec = Auto "actions/checkout"
            )
            step(
                name = "REUSE license check",
                usesSpec = Auto "fsfe/reuse-action"
            )
        ]

        dotNetJob "check" [
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

        dotNetJob "check-docs" [
            runsOn "ubuntu-24.04"
            step(
                name = "Restore dotnet tools",
                run = "dotnet tool restore"
            )
            step(
                name = "Publish the assemblies",
                run = "dotnet publish -o publish"
            )
            step(
                name = "Validate docfx",
                run = "dotnet docfx docs/docfx.json --warningsAsErrors"
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
                usesSpec = Auto "actions/upload-pages-artifact",
                options = Map.ofList [
                    "path", "docs/_site"
                ]
            )
            step(
                name = "Deploy GitHub Pages",
                id = "deployment",
                usesSpec = Auto "actions/deploy-pages"
            )
        ]
    ]

    workflow "release" [
        header licenseHeader
        name "Release"
        onPushTo "main"
        onPushTags "v*"
        onPullRequestTo "main"
        onSchedule "0 0 * * 6"
        onWorkflowDispatch
        dotNetJob "nuget" [
            jobPermission(PermissionKind.Contents, AccessKind.Write)
            runsOn "ubuntu-24.04"
            step(
                id = "version",
                name = "Get version",
                shell = "pwsh",
                run = "echo \"version=$(scripts/Get-Version.ps1 -RefName $env:GITHUB_REF)\" >> $env:GITHUB_OUTPUT"
            )
            step(
                run = "dotnet pack --configuration Release -p:Version=${{ steps.version.outputs.version }}"
            )
            step(
                name = "Read changelog",
                usesSpec = Auto "ForNeVeR/ChangelogAutomation.action",
                options = Map.ofList [
                    "output", "./release-notes.md"
                ]
            )

            let projectNames = [
                "Fabricator.Console"
                "Fabricator.Core"
                "Fabricator.Resources"
            ]

            let nuPkgPaths =
                projectNames
                |> Seq.map(fun p -> $"./{p}/bin/Release/FVNever.{p}.${{{{ steps.version.outputs.version }}}}.nupkg")
                |> Seq.toArray

            let filesToUpload =
                nuPkgPaths
                |> Seq.collect(fun p -> [
                    p
                    Path.ChangeExtension(p, "snupkg")
                ])

            let filesToUploadString = String.Join("\n", filesToUpload)

            step(
                name = "Upload artifacts",
                usesSpec = Auto "actions/upload-artifact",
                options = Map.ofList [
                    "path", $"./release-notes.md\n{filesToUploadString}"
                ]
            )
            step(
                condition = "startsWith(github.ref, 'refs/tags/v')",
                name = "Create a release",
                usesSpec = Auto "softprops/action-gh-release",
                options = Map.ofList [
                    "body_path", "./release-notes.md"
                    "files", filesToUploadString
                    "name", "Fabricator v${{ steps.version.outputs.version }}"
                ]
            )

            yield! nuPkgPaths |> Seq.map(fun nuPkg ->
                let fileName = Path.GetFileNameWithoutExtension nuPkg
                let packageName = fileName.Substring(0, fileName.IndexOf(".$"))
                step(
                    condition = "startsWith(github.ref, 'refs/tags/v')",
                    name = $"Push {packageName} to NuGet",
                    run = $"dotnet nuget push \"{nuPkg}\" --source https://api.nuget.org/v3/index.json --api-key ${{{{ secrets.NUGET_TOKEN }}}}"
                )
            )
        ]
    ]
]

exit <| EntryPoint.Process fsi.CommandLineArgs workflows
