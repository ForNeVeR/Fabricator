// SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Resources

open System
open System.IO
open System.Runtime.InteropServices
open Fabricator.Core
open TruePath
open TruePath.SystemIo

type HostFile =
    /// <summary>
    /// Gets the default hosts file path for the current platform.
    /// </summary>
    static member internal DefaultHostsPath: AbsolutePath =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            let systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System)
            AbsolutePath(Path.Combine(systemFolder, @"drivers\etc\hosts"))
        else
            AbsolutePath "/etc/hosts"
    
    /// <summary>
    /// Creates a resource for managing a host file entry.
    /// </summary>
    /// <param name="ipAddress">The IP address for the host entry.</param>
    /// <param name="host">The hostname to map to the IP address.</param>
    /// <param name="hostsFilePath">Optional path to the hosts file. Defaults to the platform-specific hosts file path:
    /// Windows: %SystemRoot%\drivers\etc\hosts (resolved dynamically), Linux/macOS: /etc/hosts</param>
    /// <returns>An IResource for managing the host file entry.</returns>
    static member Record(ipAddress: string, host: string, ?hostsFilePath: AbsolutePath): IResource =
        let filePath = defaultArg hostsFilePath HostFile.DefaultHostsPath
        
        let removeInlineComment (line: string) =
            // Remove inline comments (# til the end of the line)
            let commentIndex = line.IndexOf('#')
            if commentIndex >= 0 then
                line.Substring(0, commentIndex)
            else
                line
        
        let parseEntry (line: string) =
            // Parse a line and return (IP, Set<hosts>)
            // Handles multi-host entries like "192.168.1.1 host1 host2 host3"
            let lineWithoutComment = removeInlineComment line
            let parts = lineWithoutComment.Split([|' '; '\t'|], StringSplitOptions.RemoveEmptyEntries)
            if parts.Length >= 2 then
                let ip = parts.[0].Trim().ToLowerInvariant()
                let hosts = parts.[1..] |> Array.map (fun h -> h.Trim().ToLowerInvariant()) |> Set.ofArray
                Some (ip, hosts)
            else
                None
        
        let expectedHost = host.Trim().ToLowerInvariant()
        let expectedIp = ipAddress.Trim().ToLowerInvariant()
        
        let findEntryWithHost (lines: string[]) =
            // Find the line that contains our host
            lines
            |> Array.tryFindIndex (fun line ->
                match parseEntry line with
                | Some (_, hosts) -> hosts |> Set.contains expectedHost
                | None -> false
            )
        
        let entryMatches (line: string) =
            // Check if the entry matches our expected IP and host
            match parseEntry line with
            | Some (ip, hosts) -> 
                ip = expectedIp && hosts |> Set.contains expectedHost
            | None -> false
        
        { new IResource with
            member this.PresentableName = $"Host file entry \"{host}\""
            
            member this.AlreadyApplied() = async {
                if not (filePath.Exists()) then
                    return false
                else
                    let! ct = Async.CancellationToken
                    let! lines = Async.AwaitTask <| File.ReadAllLinesAsync(filePath.Value, ct)
                    
                    match findEntryWithHost lines with
                    | Some index -> return entryMatches lines.[index]
                    | None -> return false
            }
            
            member this.Apply() = async {
                let! ct = Async.CancellationToken
                
                // Ensure the file exists
                if not (filePath.Exists()) then
                    failwithf $"Hosts file not found at \"{filePath.Value}\". Ensure the file exists or specify a valid path using the hostsFilePath parameter."
                
                let! lines = Async.AwaitTask <| File.ReadAllLinesAsync(filePath.Value, ct)
                
                let existingEntryIndex = findEntryWithHost lines
                
                let newLines =
                    match existingEntryIndex with
                    | Some index ->
                        let line = lines.[index]
                        match parseEntry line with
                        | Some (ip, hosts) ->
                            if ip = expectedIp && hosts |> Set.contains expectedHost then
                                // IP and host both match, line is already correct
                                lines
                            else
                                // IP differs or host in multi-host entry needs to be moved
                                let otherHosts = hosts |> Set.remove expectedHost
                                if not (Set.isEmpty otherHosts) then
                                    // Multi-host entry: split into two lines
                                    let joinedHosts = String.Join(" ", otherHosts)
                                    let updatedLine = $"{ip} {joinedHosts}"
                                    let newLine = $"{ipAddress} {host}"
                                    lines 
                                    |> Array.mapi (fun i l -> 
                                        if i = index then updatedLine 
                                        else l)
                                    |> Array.append [| newLine |]
                                else
                                    // Single host entry: replace
                                    lines |> Array.mapi (fun i l ->
                                        if i = index then
                                            $"{ipAddress} {host}"
                                        else
                                            l
                                    )
                        | None -> 
                            failwithf $"Cannot parse line \"{line}\"."
                    | None ->
                        // Add new entry at the end
                        Array.append lines [| $"{ipAddress} {host}" |]
                
                do! Async.AwaitTask(filePath.WriteAllLinesAsync(newLines, ct))
            }
        }
