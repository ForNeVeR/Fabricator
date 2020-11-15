module Fabricator.Console.EntryPoint

module ExitCodes =
    let Success = 0
    let InvalidArgs = 1

let private printUsage() =
    printfn "Usage:"

let main: string[] -> int = function
| _ ->
    printUsage()
    ExitCodes.InvalidArgs
