namespace Fabricator.Core

type IMachineDesignator =
    abstract member PresentableName: string
    abstract member IsCurrentMachine: bool

type Machine = {
    Designator: IMachineDesignator
    Resources: IResource seq
}

module Designators =
    let currentMachine: IMachineDesignator =
        { new IMachineDesignator with
            member _.PresentableName = "[current machine]"
            member _.IsCurrentMachine = true }
