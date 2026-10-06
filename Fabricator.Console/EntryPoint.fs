// SPDX-FileCopyrightText: 2020-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Console.EntryPoint

open System
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

/// <summary>Performs tasks on the passed resources according to the passed arguments.</summary>
/// <remarks>
/// The independent resources are processed in parallel. The dependencies of the passed resources (see
/// <see cref="P:Fabricator.Core.IResource.DependsOn"/>) are processed only when required.
/// </remarks>
/// <param name="args">The command-line arguments.</param>
/// <param name="resources">The root resources, i.e., the resources describing the desired environment state.</param>
/// <returns>The process exit code.</returns>
let main (args: string seq) (resources: IResource seq): int =
    let args = Seq.toArray args
    let args =
        if args.Length > 0 && args[0].EndsWith ".fsx"
        then Array.skip 1 args
        else args

    match args with
    | [|"apply"|] ->
        if (Commands.apply Console.Out resources).GetAwaiter().GetResult()
        then ExitCodes.Success
        else ExitCodes.ExecutionError
    | [|"check"|] ->
        match (Commands.check Console.Out resources).GetAwaiter().GetResult() with
        | AllApplied -> ExitCodes.Success
        | NotAllApplied -> ExitCodes.NotAllApplied
        | CheckError -> ExitCodes.ExecutionError
    | [|"--help"|] -> printUsage(); ExitCodes.Success
    | _ -> printUsage(); ExitCodes.InvalidArgs
