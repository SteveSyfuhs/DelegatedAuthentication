using System.Runtime.InteropServices;
using System.Text;

namespace Shared
{
    public class PasswordCredential : Credential
    {
        private readonly CredentialHandle handle;

        public PasswordCredential(string username, string password, string domain)
        {
            handle = SafeMakeCreds(username, password, domain);
        }

        private unsafe CredentialHandle SafeMakeCreds(string username, string password, string domain)
        {
            return new CredentialHandle(MakeCreds(username, password, domain));
        }

        private unsafe void* MakeCreds(string username, string password, string domain)
        {
            var usernameBytes = Encoding.Unicode.GetBytes(username);
            var passwordBytes = Encoding.Unicode.GetBytes(password);
            var domainBytes = Encoding.Unicode.GetBytes(domain);

            var size = sizeof(SEC_WINNT_AUTH_IDENTITY) +
                       usernameBytes.Length +
                       passwordBytes.Length +
                       domainBytes.Length;

            void* pCreds = SafeAlloc(size);

            SEC_WINNT_AUTH_IDENTITY* creds = (SEC_WINNT_AUTH_IDENTITY*)pCreds;

            creds->UserLength = username.Length;
            creds->PasswordLength = password.Length;
            creds->DomainLength = domain.Length;

            creds->Flags = SEC_WINNT_AUTH_IDENTITY_FLAGS.Unicode;

            Marshal.Copy(
                usernameBytes,
                0,
                PtrIncrement(pCreds, (uint)sizeof(SEC_WINNT_AUTH_IDENTITY)),
                usernameBytes.Length
            );

            Marshal.Copy(
                domainBytes,
                0,
                PtrIncrement(pCreds, (uint)(sizeof(SEC_WINNT_AUTH_IDENTITY) + usernameBytes.Length)),
                domainBytes.Length
            );

            Marshal.Copy(
                passwordBytes,
                0,
                PtrIncrement(pCreds, (uint)(sizeof(SEC_WINNT_AUTH_IDENTITY) + usernameBytes.Length + domainBytes.Length)),
                passwordBytes.Length
            );

            return creds;
        }

        internal override CredentialHandle Structify()
        {
            return handle;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct SEC_WINNT_AUTH_IDENTITY
        {
            public void* User;
            public int UserLength;

            public void* Domain;
            public int DomainLength;

            public void* Password;
            public int PasswordLength;

            public SEC_WINNT_AUTH_IDENTITY_FLAGS Flags;
        }

        internal enum SEC_WINNT_AUTH_IDENTITY_FLAGS
        {
            ANSI = 1,
            Unicode = 2
        }

        /*
typedef struct _SEC_WINNT_AUTH_IDENTITY_A {
   unsigned char *User;
   unsigned long UserLength;
   unsigned char *Domain;
   unsigned long DomainLength;
   unsigned char *Password;
   unsigned long PasswordLength;
   unsigned long Flags;
 } SEC_WINNT_AUTH_IDENTITY_A, *PSEC_WINNT_AUTH_IDENTITY_A;
         */
    }
}
