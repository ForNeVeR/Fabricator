module internal Fabricator.Resources.WindowsServiceManager

open System
open System.ComponentModel
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop

module private rec Impl =
    type DWORD = uint
    type BOOL = bool
    type LPCWSTR = string
    type LPWSTR = string

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

    [<Literal>]
    let SC_MANAGER_CONNECT = 0x0001u

    [<Literal>]
    let ERROR_SERVICE_DOES_NOT_EXIST = 0x424

    [<Literal>]
    let ERROR_INSUFFICIENT_BUFFER = 0x7A

type WindowsServiceInfo = {
    AccountName: string
    CommandLine: string
}

open Impl

let GetService(name: string): WindowsServiceInfo option =
    use serviceManager = OpenSCManagerW(null, null, SC_MANAGER_CONNECT)
    if serviceManager.IsInvalid then raise <| Win32Exception()
    else

    use service = OpenServiceW(serviceManager, name, SC_MANAGER_CONNECT)
    if service.IsInvalid then
        match Marshal.GetLastWin32Error() with
        | ERROR_SERVICE_DOES_NOT_EXIST -> None
        | other -> raise <| Win32Exception other
    else

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

let DeleteService name = failwithf "TODO"

let CreateService info = failwithf "TODO"
