// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Core.Secrets

open System.Net
open System.Security

/// <summary>
/// Converts a <see cref="T:System.Security.SecureString"/> to a string. <b>Mind that the result is a non-secure
/// string.</b> It will be a subject to normal .NET garbage collection. Call as late as possible, or don't call at all.
/// </summary>
/// <param name="secret">The secure string to convert.</param>
let Materialize(secret: SecureString): string =
    NetworkCredential(null, secret).Password
