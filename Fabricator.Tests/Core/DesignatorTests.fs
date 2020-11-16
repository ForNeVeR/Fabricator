module Fabricator.Tests.MachineTests

open Fabricator.Core.Designators

open Xunit

[<Fact>]
let ``currentMachine.IsCurrentMachine always returns true``(): unit =
    Assert.True currentMachine.IsCurrentMachine

[<Fact>]
let ``currentMachine.PresentableName returns an expected name``(): unit =
    Assert.Equal("[current machine]", currentMachine.PresentableName)
