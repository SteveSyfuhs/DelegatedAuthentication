using System;
using System.Runtime.InteropServices;
using System.Text;
using static Shared.NativeMethods;

namespace Shared
{
    public class KeytabCredential : Credential
    {
        private readonly CredentialHandle handle;

        public KeytabCredential(byte[] keytab, string username, string domain)
        {
            handle = SafeMakeCreds(keytab, username, domain);
        }

        private unsafe CredentialHandle SafeMakeCreds(byte[] keytab, string username, string domain)
        {
            var creds = MakeCreds(keytab, username, domain);

            return new CredentialHandle(creds);
        }

        internal unsafe override CredentialHandle Structify()
        {
            return handle;
        }

        private static readonly Guid SEC_WINNT_AUTH_DATA_TYPE_KEYTAB = new Guid("{D587AAE8-F78F-4455-A112-C934BEEE7CE1}");

        private static unsafe SEC_WINNT_AUTH_IDENTITY_EX2* MakeCreds(byte[] keytab, string usernameStr, string domainNameStr)
        {
            var usernameBytes = Encoding.Unicode.GetBytes(usernameStr);
            var domainBytes = Encoding.Unicode.GetBytes(domainNameStr);

            var size = sizeof(SEC_WINNT_AUTH_IDENTITY_EX2) +
                       sizeof(SEC_WINNT_AUTH_PACKED_CREDENTIALS) +
                       keytab.Length +
                       usernameBytes.Length +
                       domainBytes.Length;

            void* pCreds = SafeAlloc(size);

            SEC_WINNT_AUTH_IDENTITY_EX2* creds = (SEC_WINNT_AUTH_IDENTITY_EX2*)pCreds;

            creds->Version = SEC_WINNT_AUTH_IDENTITY_VERSION_2;
            creds->cbHeaderLength = (ushort)Marshal.SizeOf<SEC_WINNT_AUTH_IDENTITY_EX2>();
            creds->cbStructureLength = (uint)size;

            SEC_WINNT_AUTH_PACKED_CREDENTIALS* packedCreds = (SEC_WINNT_AUTH_PACKED_CREDENTIALS*)PtrIncrement(creds, creds->cbHeaderLength);

            packedCreds->cbHeaderLength = (ushort)Marshal.SizeOf<SEC_WINNT_AUTH_PACKED_CREDENTIALS>();
            packedCreds->cbStructureLength = (ushort)(packedCreds->cbHeaderLength + keytab.Length);

            packedCreds->AuthData.CredType = SEC_WINNT_AUTH_DATA_TYPE_KEYTAB;
            packedCreds->AuthData.CredData.ByteArrayLength = (ushort)keytab.Length;
            packedCreds->AuthData.CredData.ByteArrayOffset = (ushort)Marshal.SizeOf<SEC_WINNT_AUTH_PACKED_CREDENTIALS>();

            creds->PackedCredentialsOffset = (uint)Marshal.SizeOf<SEC_WINNT_AUTH_IDENTITY_EX2>();
            creds->PackedCredentialsLength = packedCreds->cbStructureLength;

            Marshal.Copy(
                keytab,
                0,
                PtrIncrement(packedCreds, packedCreds->cbHeaderLength),
                keytab.Length
            );

            creds->UserOffset = creds->PackedCredentialsOffset + creds->PackedCredentialsLength;
            creds->UserLength = (ushort)usernameBytes.Length;

            Marshal.Copy(
                usernameBytes,
                0,
                PtrIncrement(creds, creds->UserOffset),
                creds->UserLength
            );

            creds->DomainOffset = creds->UserOffset + creds->UserLength;
            creds->DomainLength = (ushort)domainBytes.Length;

            Marshal.Copy(
                domainBytes,
                0,
                PtrIncrement(creds, creds->DomainOffset),
                creds->DomainLength
            );

            return creds;
        }
    }
}
