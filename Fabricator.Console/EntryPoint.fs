// SPDX-FileCopyrightText: 2020-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Console.EntryPoint

open System
open System.Threading
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

/// Runs the command until it completes, cancelling it on the first Ctrl+C. Returns None if the command was cancelled
/// or has failed.
let private runCancellable (ui: ExecutionUi.IExecutionUi) (command: Async<'a>): 'a option =
    use cts = new CancellationTokenSource()
    let onCancelKeyPress = ConsoleCancelEventHandler(fun _ args ->
        // Let the second Ctrl+C terminate the process.
        if not cts.IsCancellationRequested then
            args.Cancel <- true
            ui.WriteLine
                "Cancelling: waiting for the running resources to finish. Press Ctrl+C again to terminate immediately."
            cts.Cancel()
    )

    Console.CancelKeyPress.AddHandler onCancelKeyPress
    try
        try
            Some(Async.RunSynchronously(command, cancellationToken = cts.Token))
        with
        | :? OperationCanceledException when cts.IsCancellationRequested ->
            ui.WriteLine "Execution cancelled."
            None
        | ex ->
            eprintfn $"Execution failed: {ex}"
            None
    finally
        Console.CancelKeyPress.RemoveHandler onCancelKeyPress

/// <summary>Performs tasks on the passed resources according to the passed arguments.</summary>
/// <remarks>
/// <para>
/// The passed resources and all their dependencies (see <see cref="P:Fabricator.Core.Resource.DependsOn"/>) are
/// processed. When applying, a resource is checked and, if required, applied only after all its dependencies are
/// applied; independent resources are processed in parallel. When only checking, all the resources are checked in
/// parallel.
/// </para>
/// <para>
/// On the first Ctrl+C, no new resource checks or applications are started, the running ones are cancelled via their
/// cancellation tokens, and the execution ends with an error after all of them have finished.
/// </para>
/// </remarks>
/// <param name="args">The command-line arguments.</param>
/// <param name="resources">The root resources, i.e., the resources describing the desired environment state.</param>
/// <returns>The process exit code.</returns>
let main (args: string seq) (resources: Resource seq): int =
    let args = Seq.toArray args
    let args =
        if args.Length > 0 && args[0].EndsWith ".fsx"
        then Array.skip 1 args
        else args

    let ui = ExecutionUi.forCurrentConsole()
    match args with
    | [|"apply"|] ->
        match apply ui resources |> runCancellable ui with
        | Some true -> ExitCodes.Success
        | Some false | None -> ExitCodes.ExecutionError
    | [|"check"|] ->
        match check ui resources |> runCancellable ui with
        | Some AllApplied -> ExitCodes.Success
        | Some NotAllApplied -> ExitCodes.NotAllApplied
        | Some CheckError | None -> ExitCodes.ExecutionError
    | [|"--help"|] -> printUsage(); ExitCodes.Success
    | _ -> printUsage(); ExitCodes.InvalidArgs
