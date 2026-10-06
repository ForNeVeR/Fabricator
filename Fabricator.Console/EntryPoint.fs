// SPDX-FileCopyrightText: 2020-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Console.EntryPoint

open System
open System.Threading.Tasks
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

let private runSynchronously(task: Task<'a>): 'a =
    task.GetAwaiter().GetResult()

/// <summary>Performs tasks on the passed resources according to the passed arguments.</summary>
/// <remarks>
/// The passed resources and all their dependencies (see <see cref="P:Fabricator.Core.IResource.DependsOn"/>) are
/// processed. The checks are performed in parallel; the resources are applied in parallel, except that a resource is
/// only applied after all its dependencies.
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
        if apply Console.Out resources |> runSynchronously
        then ExitCodes.Success
        else ExitCodes.ExecutionError
    | [|"check"|] ->
        match check Console.Out resources |> runSynchronously with
        | AllApplied -> ExitCodes.Success
        | NotAllApplied -> ExitCodes.NotAllApplied
        | CheckError -> ExitCodes.ExecutionError
    | [|"--help"|] -> printUsage(); ExitCodes.Success
    | _ -> printUsage(); ExitCodes.InvalidArgs
