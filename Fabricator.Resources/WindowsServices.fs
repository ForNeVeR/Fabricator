// SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Resources

open Fabricator.Core
open Fabricator.Resources.ResourceUtil
open YamlDotNet.Serialization

module internal WindowsServiceYaml =
    let private serializer = SerializerBuilder().IncludeNonPublicProperties().Build()

    /// Presents the service properties managed by the resource as YAML, to show their changes as a diff.
    let serialize(info: WindowsServiceManager.WindowsServiceInfo): string =
        serializer.Serialize info

type WindowsServices =
    static member createWindowsService(
        name: string,
        account: string,
        commandLine: string,
        ?dependsOn: Resource seq
    ): Resource =
        let desired: WindowsServiceManager.WindowsServiceInfo = { AccountName = account; CommandLine = commandLine }
        let change(existing: WindowsServiceManager.WindowsServiceInfo option) =
            if existing = Some desired then NoChanges
            else
                Diffs.textChange
                    $"service \"{name}\""
                    (existing |> Option.map WindowsServiceYaml.serialize)
                    (WindowsServiceYaml.serialize desired)
        {
            PresentableName = $"Service \"{name}\""
            DependsOn = dependencies dependsOn
            Lock = None
            AlreadyApplied = fun _ -> async {
                return change(WindowsServiceManager.GetService name)
            }
            Apply = fun ctx -> async {
                let reporter = ctx.Reporter
                let existing = WindowsServiceManager.GetService name
                match existing with
                | None -> ()
                | Some _ ->
                    reporter.Status "Stopping the existing service"
                    do! WindowsServiceManager.StopService name
                    reporter.Status "Deleting the existing service"
                    WindowsServiceManager.DeleteService name
                    reporter.Log $"Deleted the existing service \"{name}\"."

                reporter.Status "Creating the service"
                WindowsServiceManager.CreateService(name, desired)
                reporter.Log $"Created service \"{name}\" running as {account}: {commandLine}"
                reporter.Status "Starting the service"
                WindowsServiceManager.StartService name
                return change existing
            }
        }
