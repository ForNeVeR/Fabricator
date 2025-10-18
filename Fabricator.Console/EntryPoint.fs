// SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Console.EntryPoint

open Fabricator.Console.Commands
open Fabricator.Core

module ExitCodes =
    let Success = 0
    let InvalidArgs = 1
    let ExecutionError = 2
    let NotAllApplied = 3

let private printUsage() =
    printfn "Arguments:"
    printfn "apply - applies the resources to the current environment"
    printfn "check - checks and shows the upcoming changes to the current environment, no actions taken"

/// Performs tasks on the passed resources according to the passed arguments.
let main (args: string[]) (resources: IResource seq): int =
    match args with
    | [|"apply"|] ->
        if Async.RunSynchronously(Commands.apply resources) then ExitCodes.Success else ExitCodes.ExecutionError
    | [|"check"|] ->
        match Async.RunSynchronously(Commands.check resources) with
        | AllApplied -> ExitCodes.Success
        | NotAllApplied -> ExitCodes.NotAllApplied
        | CheckError -> ExitCodes.ExecutionError
    | [|"--help"|] -> printUsage(); ExitCodes.Success
    | _ -> printUsage(); ExitCodes.InvalidArgs
