namespace Fabricator.Resources

open Fabricator.Core

type WindowsServices =
    static member createWindowsService(name: string, account: string, commandLine: string): IResource =
        { new IResource with
            member this.PresentableName = $"Service \"{name}\""
            member this.AlreadyApplied() = async {
                return
                    match WindowsServiceManager.GetService(name) with
                    | None -> false
                    | Some service -> service.AccountName = account && service.CommandLine = commandLine
            }
            member this.Apply() = async {
                match WindowsServiceManager.GetService(name) with
                | None -> ()
                | Some service -> WindowsServiceManager.DeleteService(name)

                WindowsServiceManager.CreateService(name, account, commandLine)
            }
        }
