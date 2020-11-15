module Fabricator.Console.EntryPoint

open Fabricator.Core

module ExitCodes =
    let Success = 0
    let InvalidArgs = 1

let private printUsage() =
    printfn "Usage:"

/// Performs tasks on passed cluster according to the passed arguments
let main (args: string[]) (cluster: Machine seq): int =
    printUsage()
    ExitCodes.InvalidArgs
