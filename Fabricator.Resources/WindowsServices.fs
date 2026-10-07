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
            AlreadyApplied = fun () -> async {
                return
                    match WindowsServiceManager.GetService name with
                    | None -> false
                    | Some service -> service.AccountName = account && service.CommandLine = commandLine
            }
            Apply = fun () -> async {
                match WindowsServiceManager.GetService name with
                | None -> ()
                | Some _ ->
                    do! WindowsServiceManager.StopService name
                    WindowsServiceManager.DeleteService name

                WindowsServiceManager.CreateService(name, { AccountName = account; CommandLine = commandLine })
                WindowsServiceManager.StartService name
            }
        }
