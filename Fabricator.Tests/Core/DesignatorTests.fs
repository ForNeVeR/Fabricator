// SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.MachineTests

open System.IO

open Fabricator.Core.Designators

open Xunit

[<Fact>]
let ``currentMachine.IsCurrentMachine always returns true``(): unit =
    Assert.True currentMachine.IsCurrentMachine

[<Fact>]
let ``currentMachine.PresentableName returns an expected name``(): unit =
    Assert.Equal("[current machine]", currentMachine.PresentableName)

[<Fact>]
let ``Local machine should be read properly``(): unit =
    let connectionsContent = """{
    "localhost": {
        "kind": "local"
    }
}
"""
    let connectionsFile = Path.GetTempFileName()
    try
        File.WriteAllText(connectionsFile, connectionsContent)

        let machine = fromConnectionsFile connectionsFile "localhost"
        Assert.Equal(currentMachine, machine)
    finally
        File.Delete connectionsFile
