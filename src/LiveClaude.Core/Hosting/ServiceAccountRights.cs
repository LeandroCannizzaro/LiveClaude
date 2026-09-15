using System.Runtime.InteropServices;
using System.Security.Principal;

namespace LiveClaude.Core.Hosting;

/// <summary>
/// Grants "Log on as a service" (SeServiceLogonRight) to an account.
///
/// <c>sc create obj= DOMAIN\user password= …</c> does not grant it — the service is created and then
/// refuses to start with error 1069, "the service did not start due to a logon failure". Needs
/// administrator rights, so it runs as part of the elevated install.
/// </summary>
public static class ServiceAccountRights
{
    private const string ServiceLogonRight = "SeServiceLogonRight";
    private const uint PolicyCreateAccount = 0x00000010;
    private const uint PolicyLookupNames = 0x00000800;

    public static bool TryGrantServiceLogon(string accountName, out string message)
    {
        try
        {
            var sid = ResolveSid(accountName);
            var status = AddRight(sid, ServiceLogonRight);

            if (status == 0)
            {
                message = $"Granted 'Log on as a service' to {accountName}.";
                return true;
            }

            var error = LsaNtStatusToWinError(status);
            message = error == 5
                ? "Could not grant 'Log on as a service': access denied. Run the install elevated."
                : $"Could not grant 'Log on as a service' (error {error}). Grant it manually in secpol.msc if the service fails to start.";
            return false;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException or ArgumentException)
        {
            message = $"Could not resolve the account '{accountName}': {ex.Message}";
            return false;
        }
    }

    private static byte[] ResolveSid(string accountName)
    {
        var identity = new NTAccount(accountName);
        var sid = (SecurityIdentifier)identity.Translate(typeof(SecurityIdentifier));
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private static uint AddRight(byte[] sid, string right)
    {
        var attributes = new LSA_OBJECT_ATTRIBUTES();
        var status = LsaOpenPolicy(IntPtr.Zero, ref attributes, PolicyCreateAccount | PolicyLookupNames, out var policy);
        if (status != 0)
            return status;

        var sidHandle = GCHandle.Alloc(sid, GCHandleType.Pinned);
        try
        {
            var rights = new[] { new LSA_UNICODE_STRING(right) };
            return LsaAddAccountRights(policy, sidHandle.AddrOfPinnedObject(), rights, 1);
        }
        finally
        {
            sidHandle.Free();
            LsaClose(policy);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;

        public LSA_UNICODE_STRING(string value)
        {
            Length = (ushort)(value.Length * 2);
            MaximumLength = (ushort)((value.Length + 1) * 2);
            Buffer = Marshal.StringToHGlobalUni(value);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint LsaOpenPolicy(
        IntPtr systemName,
        ref LSA_OBJECT_ATTRIBUTES objectAttributes,
        uint desiredAccess,
        out IntPtr policyHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint LsaAddAccountRights(
        IntPtr policyHandle,
        IntPtr accountSid,
        LSA_UNICODE_STRING[] userRights,
        uint countOfRights);

    [DllImport("advapi32.dll")]
    private static extern uint LsaClose(IntPtr policyHandle);

    [DllImport("advapi32.dll")]
    private static extern int LsaNtStatusToWinError(uint status);
}
