// SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Resources

open Fabricator.Core
open Fabricator.Resources.ResourceUtil

type WindowsServices =
    static member createWindowsService(
        name: string,
        account: string,
        commandLine: string,
        ?dependsOn: Resource seq
    ): Resource =
        {
            PresentableName = $"Service \"{name}\""
            DependsOn = dependencies dependsOn
            Lock = None
            AlreadyApplied = fun _ -> async {
                return
                    match WindowsServiceManager.GetService name with
                    | None -> false
                    | Some service -> service.AccountName = account && service.CommandLine = commandLine
            }
            Apply = fun ctx -> async {
                let reporter = ctx.Reporter
                match WindowsServiceManager.GetService name with
                | None -> ()
                | Some _ ->
                    reporter.Status "Stopping the existing service"
                    do! WindowsServiceManager.StopService name
                    reporter.Status "Deleting the existing service"
                    WindowsServiceManager.DeleteService name
                    reporter.Log $"Deleted the existing service \"{name}\"."

                reporter.Status "Creating the service"
                WindowsServiceManager.CreateService(name, { AccountName = account; CommandLine = commandLine })
                reporter.Log $"Created service \"{name}\" running as {account}: {commandLine}"
                reporter.Status "Starting the service"
                WindowsServiceManager.StartService name
            }
        }
