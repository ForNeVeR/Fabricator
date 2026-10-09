// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Core

/// The unit of the values reported via <see cref="T:Fabricator.Core.IProgressReporter"/>, used to present them.
type ProgressUnit =
    /// Abstract items, e.g. processed files. Presented as a percentage of the total.
    | Items
    /// Bytes, e.g. downloaded data. Presented as data sizes.
    | Bytes

/// Reports the progress of an operation started via <see cref="M:Fabricator.Core.IReporter.WithProgress"/>.
[<Interface>]
type IProgressReporter =
    /// <summary>Reports the current progress value, i.e. the amount of work done since the operation start.</summary>
    abstract Report: current: int64 -> unit

/// <summary>
/// Reports the status, progress, and log of the resource check or application it is passed to.
/// </summary>
/// <remarks>All the members are thread-safe.</remarks>
[<Interface>]
type IReporter =
    /// <summary>
    /// Sets a short status message describing what the resource check or application is currently doing. The status
    /// is only shown while the operation is running, and is not logged.
    /// </summary>
    abstract Status: newStatus: string -> unit

    /// <summary>
    /// Writes a line to the log of the resource check or application. The log lines of different operations are never
    /// interleaved with each other: while some other operation's log is being shown, the lines are buffered.
    /// </summary>
    abstract Log: message: string -> unit

    /// <summary>
    /// Runs the <paramref name="action"/>, showing the progress it reports.
    /// </summary>
    /// <param name="header">The status shown while the action is running.</param>
    /// <param name="total">The total amount of work, or <c>None</c> if unknown.</param>
    /// <param name="progressUnit">The unit of the reported values.</param>
    /// <param name="action">The action, receiving the reporter of its progress.</param>
    /// <remarks>
    /// The progress is shown as a part of the operation that has received this reporter. It is reset after the action
    /// finishes. If several such actions run concurrently within the same operation, the last reported value is shown.
    /// </remarks>
    abstract WithProgress<'a>:
        header: string * total: int64 option * progressUnit: ProgressUnit * action: (IProgressReporter -> Async<'a>)
            -> Async<'a>

/// Helper functions to work with <see cref="T:Fabricator.Core.IProgressReporter"/>.
module ProgressReporter =
    /// A progress reporter ignoring all the reported values.
    let Null: IProgressReporter = { new IProgressReporter with member _.Report _ = () }

/// Helper functions to work with <see cref="T:Fabricator.Core.IReporter"/>.
module Reporter =
    /// A reporter ignoring everything reported to it.
    let Null: IReporter = {
        new IReporter with
            member _.Status _ = ()
            member _.Log _ = ()
            member _.WithProgress(_, _, _, action) = action ProgressReporter.Null
    }

/// The context of a resource check or application.
type ResourceContext =
    {
        /// The reporter of the status, progress, and log of the operation.
        Reporter: IReporter
    }

/// Helper functions to work with <see cref="T:Fabricator.Core.ResourceContext"/>.
module ResourceContext =
    /// A context ignoring everything reported to it, e.g. for calling the resource functions directly in tests.
    let Null: ResourceContext = { Reporter = Reporter.Null }
