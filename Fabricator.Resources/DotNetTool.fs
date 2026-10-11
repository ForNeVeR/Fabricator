// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Resources

open System.Text.Json.Nodes
open Fabricator.Core
open Fabricator.Resources.CommandUtil
open Fabricator.Resources.ResourceUtil
open TruePath
open TruePath.SystemIo

type DotNetTool =
    /// <summary>
    /// The concurrency group of the resources managing the .NET tools installed to the specified directory.
    /// </summary>
    /// <param name="installationPath">The tool installation path.</param>
    static member ConcurrencyGroup(installationPath: AbsolutePath): ConcurrencyGroup =
        ConcurrencyGroup $"DotNetTool:{installationPath.Canonicalize().Value}"

    static member Install(
        name: string,
        version: string,
        installationPath: AbsolutePath,
        ?dependsOn: Resource seq
    ): Resource =
        /// The version of the tool installed to the installation path, or None if it is not installed there.
        let installedVersion(ctx: ResourceContext) = async {
            if not <| installationPath.ExistsDirectory() then return None else
            ctx.Reporter.Status "Querying the installed tools"
            let! execResult =
                runCommand ctx.Reporter "dotnet" [|
                    "tool"
                    "list"
                    "--tool-path"; installationPath.Value
                    "--format"; "json"
                |]
            let document = JsonNode.Parse(execResult.StandardOutput) |> nonNull
            let document = document.AsObject()
            let data = document.["data"] |> nonNull
            let data = data.AsArray()
            let packageId = name.ToLowerInvariant()
            return
                data
                |> Seq.map nonNull
                |> Seq.tryFind (fun entry -> (nonNull entry["packageId"]).AsValue().ToString() = packageId)
                |> Option.map (fun entry -> (nonNull entry["version"]).AsValue().ToString())
        }

        let change(installed: string option) =
            match installed with
            | None -> NamedChange $"new tool {name} {version}"
            | Some installed when installed = version -> NoChanges
            | Some installed -> NamedChange $"tool {name}: {installed} → {version}"

        {
            PresentableName = $"{name} {version}"
            DependsOn = dependencies dependsOn
            Lock = Some(DotNetTool.ConcurrencyGroup installationPath)
            Secrets = Resource.NoSecrets
            AlreadyApplied = fun ctx -> async {
                let! installed = installedVersion ctx
                return change installed
            }
            Apply = fun ctx -> async {
                let! installed = installedVersion ctx
                ctx.Reporter.Status $"Installing version {version}"
                do!
                    runCommand ctx.Reporter "dotnet" [|
                        "tool"
                        "install"
                        name
                        "--version"; version
                        "--tool-path"; installationPath.Value
                    |]
                    |> Async.Ignore
                return change installed
            }
        }

