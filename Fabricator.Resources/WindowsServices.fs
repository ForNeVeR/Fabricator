namespace Fabricator.Resources

open Fabricator.Core

type WindowsServices =
    static member createWindowsService(name: string, account: string, commandLine: string): IResource =
        failwithf "TODO"
