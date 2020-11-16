module Tests

open Fabricator.Console

open Xunit

[<Fact>]
let ``Entry point should return argument parse error when called without arguments``(): unit =
    let exitCode = EntryPoint.main Array.empty Array.empty
    Assert.Equal(EntryPoint.ExitCodes.InvalidArgs, exitCode)
