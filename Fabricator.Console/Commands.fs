// SPDX-FileCopyrightText: 2021-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Console.Commands

open Fabricator.Core

let private checkIfApplied(resource: IResource) = async {
    try
        let! result = resource.AlreadyApplied()
        return Result.Ok result
    with
        | error -> return Result.Error error
}

let private applyResource(resource: IResource) = async {
    try
        do! resource.Apply()
        return Result.Ok(())
    with
        | error -> return Result.Error error
}

let apply(resources: IResource seq): Async<bool> = async {
    printfn "Applying changes to the current environment."

    let mutable success = true
    for resource in resources do
        if success then

            printf $"{resource.PresentableName}: "
            let! applied = checkIfApplied resource
            match applied with
            | Ok true -> printfn "already applied."
            | Ok false ->
                printf "applying… "

                let! result = applyResource resource
                match result with
                | Result.Ok _ ->
                    printfn "applied."
                | Result.Error e ->
                    success <- false
                    printfn $"error:\n{e}"
            | Error e ->
                success <- false
                printfn $"error:\n{e}"

    return success
}

type CheckStatus = AllApplied | NotAllApplied | CheckError
let check(resources: IResource seq): Async<CheckStatus> = async {
    printfn "Applying changes to the current environment."

    let mutable result = AllApplied
    for resource in resources do
        printf $"{resource.PresentableName}: "
        let! applied = checkIfApplied resource
        match applied with
        | Ok true -> printfn "already applied."
        | Ok false ->
            printfn "not applied."
            if result = AllApplied then result <- NotAllApplied
        | Error e ->
            result <- CheckError
            printfn $"error:\n{e}"

    return result
}
