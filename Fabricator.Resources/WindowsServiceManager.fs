module internal Fabricator.Resources.WindowsServiceManager

open System
open System.ComponentModel
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop

module private rec Native =
    type DWORD = uint
    type BOOL = bool
    type LPCWSTR = string
    type LPWSTR = string
    type LPDWORD = nativeptr<DWORD>

    type SC_HANDLE() =
        inherit SafeHandle(0, true)
        override this.IsInvalid = this.handle = 0

        override this.ReleaseHandle() = CloseServiceHandle this.handle

    [<Struct; StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)>]
    type QUERY_SERVICE_CONFIGW =
        val dwServiceType: DWORD
        val dwStartType: DWORD
        val dwErrorControl: DWORD
        val lpBinaryPathName: LPWSTR
        val lpLoadOrderGroup: LPWSTR
        val dwTagId: DWORD
        val lpDependencies: LPWSTR
        val lpServiceStartName: LPWSTR
        val lpDisplayName: LPWSTR

    [<Struct>]
    type SERVICE_STATUS =
        val dwServiceType: DWORD
        val dwCurrentState: DWORD
        val dwControlsAccepted: DWORD
        val dwWin32ExitCode: DWORD
        val dwServiceSpecificExitCode: DWORD
        val dwCheckPoint: DWORD
        val dwWaitHint: DWORD

    [<DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)>]
    extern SC_HANDLE OpenSCManagerW(LPCWSTR | null lpMachineName, LPCWSTR | null lpDatabaseName, DWORD dwDesiredAccess)

    [<DllImport("Advapi32.dll", SetLastError = true)>]
    extern BOOL CloseServiceHandle(IntPtr hSCObject)

    [<DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)>]
    extern SC_HANDLE OpenServiceW(SC_HANDLE hSCManager, LPCWSTR lpServiceName, DWORD dwDesiredAccess)

    [<DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)>]
    extern BOOL QueryServiceConfigW(
        SC_HANDLE hService,
        voidptr lpServiceConfig,
        DWORD cbBufSize,
        DWORD& pcbBytesNeeded
    )

    [<DllImport("AdvApi32.dll", SetLastError = true)>]
    extern BOOL ControlService(SC_HANDLE hService, DWORD dwControl, SERVICE_STATUS& lpServiceStatus)

    [<DllImport("Advapi32.dll", SetLastError = true)>]
    extern BOOL QueryServiceStatus(SC_HANDLE hService, SERVICE_STATUS& lpServiceStatus)

    [<DllImport("Advapi32.dll", SetLastError = true)>]
    extern BOOL DeleteService(SC_HANDLE hService)

    [<DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)>]
    extern SC_HANDLE CreateServiceW(
        SC_HANDLE hSCManager,
        LPCWSTR lpServiceName,
        LPCWSTR | null lpDisplayName,
        DWORD dwDesiredAccess,
        DWORD dwServiceType,
        DWORD dwStartType,
        DWORD dwErrorControl,
        LPCWSTR | null lpBinaryPathName,
        LPCWSTR | null lpLoadOrderGroup,
        LPDWORD lpdwTagId,
        LPCWSTR | null lpDependencies,
        LPCWSTR | null lpServiceStartName,
        LPCWSTR | null lpPassword
    )

    [<Literal>]
    let SC_MANAGER_CONNECT = 0x0001u

    [<Literal>]
    let ERROR_SERVICE_DOES_NOT_EXIST = 0x424

    [<Literal>]
    let ERROR_INSUFFICIENT_BUFFER = 0x7A

    [<Literal>]
    let SERVICE_CONTROL_STOP = 0x00000001u

    [<Literal>]
    let SERVICE_STOPPED = 0x00000001u

    [<Literal>]
    let SERVICE_STOP_PENDING = 0x00000003u

    [<Literal>]
    let SERVICE_WIN32_OWN_PROCESS = 0x00000010u

    [<Literal>]
    let SERVICE_AUTO_START = 0x00000002u

    [<Literal>]
    let SERVICE_ERROR_NORMAL = 0x00000001u

type WindowsServiceInfo = {
    AccountName: string | null
    CommandLine: string
}

open Native

let private WithServiceHandle name action =
    use serviceManager = OpenSCManagerW(null, null, SC_MANAGER_CONNECT)
    if serviceManager.IsInvalid then raise <| Win32Exception()
    else

    use service = OpenServiceW(serviceManager, name, SC_MANAGER_CONNECT)
    if service.IsInvalid then
        action(Result.Error <| Marshal.GetLastWin32Error())
    else
        action(Result.Ok service)

let private WithServiceHandleAsync name action = async {
    use serviceManager = OpenSCManagerW(null, null, SC_MANAGER_CONNECT)
    if serviceManager.IsInvalid then raise <| Win32Exception()
    else

    use service = OpenServiceW(serviceManager, name, SC_MANAGER_CONNECT)
    if service.IsInvalid then
        return! action(Result.Error <| Marshal.GetLastWin32Error())
    else
        return! action(Result.Ok service)
}

let GetService(name: string): WindowsServiceInfo option =
    WithServiceHandle name (fun service ->
        match service with
        | Result.Error ERROR_SERVICE_DOES_NOT_EXIST -> None
        | Result.Error other -> raise <| Win32Exception other
        | Result.Ok service ->

#nowarn 9 // NativePtr stuff
        let mutable buffer = NativePtr.stackalloc<byte>(Marshal.SizeOf<QUERY_SERVICE_CONFIGW>())
        let mutable bytesNeeded = 0u
        let mutable success = QueryServiceConfigW(
            service,
            NativePtr.toVoidPtr buffer,
            Checked.uint32 <| Marshal.SizeOf<QUERY_SERVICE_CONFIGW>(),
            &bytesNeeded
        )
        if not success then
            match Marshal.GetLastWin32Error() with
            | ERROR_INSUFFICIENT_BUFFER ->
                buffer <- NativePtr.stackalloc<byte>(Checked.int32 bytesNeeded)
                success <- QueryServiceConfigW(
                    service,
                    NativePtr.toVoidPtr buffer,
                    bytesNeeded,
                    &bytesNeeded
                )
                if not success then raise <| Win32Exception()

            | other -> raise <| Win32Exception other

        let serviceConfig = Marshal.PtrToStructure<QUERY_SERVICE_CONFIGW>(NativePtr.toNativeInt buffer)

        Some {
            CommandLine = serviceConfig.lpBinaryPathName
            AccountName = serviceConfig.lpServiceStartName
        }
    )

let private waitForStop service = async {
    let mutable finished = false
    while not finished do
        let mutable serviceStatus = SERVICE_STATUS()
        let success = QueryServiceStatus(service, &serviceStatus)
        if not success then raise <| Win32Exception()

        match serviceStatus.dwCurrentState with
        | SERVICE_STOPPED -> finished <- true
        | SERVICE_STOP_PENDING -> do! Async.Sleep(TimeSpan.FromMilliseconds(float serviceStatus.dwWaitHint))
        | other -> failwithf $"Unknown service status: {other}."
}

let StopService(name: string): Async<unit> =
    WithServiceHandleAsync name (fun service -> async {
        match service with
        | Result.Error e -> raise <| Win32Exception e
        | Result.Ok service ->

        let mutable serviceStatus = SERVICE_STATUS()
        let success = ControlService(service, SERVICE_CONTROL_STOP, &serviceStatus)
        if not success then raise <| Win32Exception()

        do! waitForStop service
    })

let rec DeleteService(name: string): unit =
    WithServiceHandle name (fun service ->
        match service with
        | Result.Error ERROR_SERVICE_DOES_NOT_EXIST -> ()
        | Result.Error e -> raise <| Win32Exception e
        | Result.Ok service ->

        let success = Native.DeleteService service
        if not success then raise <| Win32Exception()
    )

let CreateService(name: string, info: WindowsServiceInfo): unit =
    use serviceManager = OpenSCManagerW(null, null, SC_MANAGER_CONNECT)
    if serviceManager.IsInvalid then raise <| Win32Exception()
    else

    use service = CreateServiceW(
        serviceManager,
        name,
        null,
        SC_MANAGER_CONNECT,
        SERVICE_WIN32_OWN_PROCESS,
        SERVICE_AUTO_START,
        SERVICE_ERROR_NORMAL,
        info.CommandLine,
        null,
        NativePtr.nullPtr,
        null,
        info.AccountName,
        null
    )
    if service.IsInvalid then raise <| Win32Exception()
